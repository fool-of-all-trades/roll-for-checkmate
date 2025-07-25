using System.Collections.Generic;
using UnityEngine;
using Pieces;
using System.Linq;
using System;

namespace Controller
{
    [DisallowMultipleComponent]
    public class GameControllerMono : MonoBehaviour, IGameController
    {
        /* ───────────── IGameController contract ───────────── */

        public event Action<MoveResult> OnMoveAccepted;

        /// <summary>Called once by ChessBoard in Awake to give us the live piece list.</summary>
        public void Initialize(IEnumerable<Piece> allPieces) =>
            pieces = new List<Piece>(allPieces);

       
        public Piece PieceAt(int row, int col) =>
            pieces.FirstOrDefault(p => p.Row == row && p.Col == col);

        /* ───────────── public helpers (unchanged from old GC) ───────────── */

        public bool Duel(int rollResult, Piece attacker, Piece defender)
        {
            int threshold = 5 + defender.Level - attacker.Level;
            return rollResult > threshold;
        }

        public Piece GetKing(bool team) =>
            pieces.FirstOrDefault(p => p is King && p.Team == team);

        public bool IsCheck(Piece king, Piece lastPiece) =>
            lastPiece.IsValidMove(king.Row, king.Col);

        public bool CheckCheck(Piece piece, int destRow, int destCol) =>
            checker.PredictDanger(piece, GetKing(piece.Team), destRow, destCol, this);

        public bool IsGameOver(Piece lastMovedPiece)
        {
            bool teamToMove = turnMgr.WhiteTurn;
            Piece king = GetKing(teamToMove);

            foreach (var p in pieces.Where(x => x.Team == teamToMove))
            {
                for (int r = 0; r < 8; r++)
                    for (int c = 0; c < 8; c++)
                        if (p.IsValidMove(r, c) && !CheckCheck(p, r, c))
                            return false;
            }

            Debug.Log(IsCheck(king, lastMovedPiece) ? "CheckMate!" : "StaleMate!");
            return true;
        }

        /* ───────────── internal state & helpers ───────────── */

        List<Piece> pieces;                 // set in Initialize()
        Checker checker;                    // same class you had before

        MoveValidator validator;
        TurnManager turnMgr;
        CaptureManager captureManager = new();


        void Awake()
        {
            checker = new Checker(PieceAt);
            turnMgr = new TurnManager();
            validator = new MoveValidator(turnMgr, PieceAt, checker, GetKing, this);
        }

        public bool TryMove(Piece piece, int toRow, int toCol)
        {
            if (!validator.IsLegalMove(piece, toRow, toCol, out var info))
                return false;

            int fromRow = piece.Row;
            int fromCol = piece.Col;

            // ----- captures (normal target square) -----
            Piece captured = PieceAt(toRow, toCol);
            if (captured != null)
                captureManager.CapturePiece(pieces, captured, piece);

            // ----- en‑passant capture BEFORE commit -----
            if (piece is Pawn pawn && captured == null)
            {
                var eps = turnMgr.EnPassantSquare;
                if (eps.HasValue && eps.Value.row == toRow && eps.Value.col == toCol)
                {
                    int dir = pawn.Team ? 1 : -1;
                    Piece victim = PieceAt(toRow - dir, toCol);
                    if (victim is Pawn && victim.Team != pawn.Team)
                    {
                        captureManager.CapturePiece(pieces, victim, pawn);
                        captured = victim;           // include in MoveResult
                    }
                }
            }

            // ----- commit move -----
            piece.SetBoardCoords(toRow, toCol);
            piece.MarkMoved(); // needed for castling cuz those towers do be cheating

            // set new en‑passant square if pawn double‑stepped
            if (piece is Pawn p && Math.Abs(toRow - fromRow) == 2)
            {
                int midRow = (fromRow + toRow) / 2;
                turnMgr.SetEnPassant(midRow, fromCol, p);
            }

            // ----- castling rook move + events -----
            if (info.IsCastle)
            {
                var rook = info.CastleRook;
                int dir = Math.Sign(toCol - fromCol);
                int rookToCol = toCol - dir;
                int rookFromCol = rook.Col;

                rook.SetBoardCoords(piece.Row, rookToCol);
                rook.MarkMoved();

                OnMoveAccepted?.Invoke(new MoveResult(piece, fromRow, fromCol, toRow, toCol, captured));
                OnMoveAccepted?.Invoke(new MoveResult(rook, piece.Row, rookFromCol, piece.Row, rookToCol));
            }
            else
            {
                OnMoveAccepted?.Invoke(new MoveResult(piece, fromRow, fromCol, toRow, toCol, captured));
            }

            // TODO: promotion UI / replacement here

            turnMgr.ToggleTurn();          // flip side & clear old en‑passant square
            return true;
        }


        // REMOVE LATER CUZ THIS IS NOT A GOOD WAY OF DOING THIS, JUST FOR TESTS
        // expose current side
        public bool IsWhiteTurn => turnMgr.WhiteTurn;

        // let view flip turn manually (HandleCaptureOrDuel uses it)
        public void ToggleTurn() => turnMgr.ToggleTurn();

        // capture wrapper
        public void CapturePiece(Piece captured, Piece winner)
        {
            captureManager.CapturePiece(pieces, captured, winner);
        }

    }
}
