using System.Collections.Generic;
using UnityEngine;
using Pieces;
using System.Linq;
using System;
using System.Reflection;
using System.Text.RegularExpressions;

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

        private bool IsCastleAttempt(Piece piece, int toRow, int toCol)
        {
            return piece is King k
                && !k.HasMoved
                && toRow == k.Row
                && Mathf.Abs(toCol - k.Col) == 2;
        }

        private bool SquaresAreSafeForKing(King king, int fromCol, int toCol)
        {
            int dir = Math.Sign(toCol - fromCol);
            for (int c = fromCol; c != toCol + dir; c += dir)
            {
                // The king hypothetically stands on this square
                if (CheckCheck(king, king.Row, c))    // uses checker.PredictDanger(...) internally
                    return false;
            }
            return true;
        }

        private Rook FindCastlingRook(King king, int toCol)
        {
            int dir = Math.Sign(toCol - king.Col);
            // rook should be at edge (0 or 7) in that direction
            int rookCol = dir < 0 ? 0 : 7;
            return PieceAt(king.Row, rookCol) as Rook;
        }
        private bool PathClearExceptEndpoints(int row, int fromCol, int toCol)
        {
            int dir = Math.Sign(toCol - fromCol);
            for (int c = fromCol + dir; c != toCol; c += dir)
            {
                if (PieceAt(row, c) != null) return false;
            }
            return true;
        }

        public bool TryMove(Piece piece, int toRow, int toCol)
        {
            if (piece == null) return false;
            if (piece.Team != whiteTurn) return false;

            int fromRow = piece.Row;
            int fromCol = piece.Col;

            // 1. Shape/geometry check (piece.IsValidMove just shape & path, no danger)
            if (!piece.IsValidMove(toRow, toCol))
                return false;

            // 2. Special case: castling checks
            bool isCastle = IsCastleAttempt(piece, toRow, toCol);

            if (isCastle)
            {
                var king = (King)piece;
                // Are path squares safe?
                if (!SquaresAreSafeForKing(king, fromCol, toCol))
                    return false;

                // Is there a rook & has it not moved?
                var rook = FindCastlingRook(king, toCol);
                if (rook == null || rook.HasMoved)
                    return false;

                // Also ensure squares between king & rook are empty
                if (!PathClearExceptEndpoints(king.Row, fromCol, rook.Col))
                    return false;
            }

            // 3. Simulate the move to see if your king will be in check (non-castle, general case)
            // (Castling safety for squares is done above; but still ensure after castling final square is safe.)
            if (!isCastle && CheckCheck(piece, toRow, toCol))
                return false;

            // 4. Commit: handle capture if any
            Piece captured = PieceAt(toRow, toCol);
            if (captured != null && captured.Team == piece.Team)
                return false;

            if (captured != null) CapturePiece(captured, piece);

            // Move the piece (no visuals here)
            piece.SetBoardCoords(toRow, toCol);

            // 5. If castling, move the rook too (and consider firing a second MoveResult or extend the struct)
            if (isCastle)
            {
                var king = (King)piece;
                var rook = FindCastlingRook(king, toCol);
                int dir = Math.Sign(toCol - fromCol);

                int rookFromCol = rook.Col;
                int rookToCol = toCol - dir;  // rook ends up next to king
                rook.SetBoardCoords(king.Row, rookToCol);

                // Option A: fire two events (king move, rook move)
                OnMoveAccepted?.Invoke(new MoveResult(piece, fromRow, fromCol, toRow, toCol, captured));
                OnMoveAccepted?.Invoke(new MoveResult(rook, king.Row, rookFromCol, king.Row, rookToCol));
            }
            else
            {
                // Normal move, single event
                OnMoveAccepted?.Invoke(new MoveResult(piece, fromRow, fromCol, toRow, toCol, captured));
            }

            ToggleTurn();
            return true;
        }


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
            bool teamToMove = whiteTurn;
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

        [SerializeField] private bool whiteStarts = true;

        public bool IsWhiteTurn => whiteTurn;

        bool whiteTurn;
        readonly List<Piece> whiteCaptured = new();
        readonly List<Piece> blackCaptured = new();
        List<Piece> pieces;                 // set in Initialize()
        Checker checker;                    // same class you had before

        void Awake()
        {
            whiteTurn = whiteStarts; 
            checker = new Checker(PieceAt);
        }

        public void ToggleTurn() => whiteTurn = !whiteTurn;

        void TrackCapture(Piece captured, Piece winner)
        {
            if (captured.Team) whiteCaptured.Add(captured);
            else blackCaptured.Add(captured);

            // level-up logic (same as before)
            if (winner.Level < 5)
                winner.UpdateLevel(captured is Pawn ? 1 : 2);
        }

        public void CapturePiece(Piece captured, Piece winner)   // NEW
        {
            if (captured == null) return;

            pieces.Remove(captured);
            captured.gameObject.SetActive(false);   // visual tidy-up
            TrackCapture(captured, winner);         // XP / level logic
        }
    }
}
