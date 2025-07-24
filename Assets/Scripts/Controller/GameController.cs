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

        public bool TryMove(Piece piece, int toRow, int toCol)
        {
            if (piece == null) return false;
            if (piece.Team != whiteTurn) return false;   // not that side’s turn

            // ── capture? ───────────────────────────────────────────────────
            Piece captured = PieceAt(toRow, toCol);
            if (captured != null && captured.Team == piece.Team) return false; // own piece

            // **Legal-move test is still done by ChessBoard before it calls us,
            //   so we don’t re-check IsValidMove here.**

            int fromRow = piece.Row;
            int fromCol = piece.Col;

            // update internal state
            if (captured != null)
            {
                pieces.Remove(captured);
                TrackCapture(captured, piece);
            }

            piece.SetBoardCoords(toRow, toCol);

            ToggleTurn();

            // fire the event *after* state change
            OnMoveAccepted?.Invoke(
                new MoveResult(piece, fromRow, fromCol, toRow, toCol, captured));

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
