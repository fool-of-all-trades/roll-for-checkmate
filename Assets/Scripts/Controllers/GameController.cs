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
        /* ───────────── IGameController event contracts ───────────── */

        public event Action<MoveResult> OnMoveAccepted;
        public event Action<int> OnDuelRolled;

        public event Action<IReadOnlyList<Vector2Int>, IReadOnlyList<Vector2Int>> OnRoadsChanged
        {
            add => sacredRoad.OnRoadsChanged += value;
            remove => sacredRoad.OnRoadsChanged -= value;
        }


        /// <summary>Called once by ChessBoard in Awake to give us the live piece list.</summary>
        public void Initialize(IEnumerable<Piece> allPieces) =>
            pieces = new List<Piece>(allPieces);

        
        public Piece PieceAt(int row, int col) =>
            pieces.FirstOrDefault(p => p.Row == row && p.Col == col);

        /* ───────────── public helpers (unchanged from old GC) ───────────── */

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

        [SerializeField] private MonoBehaviour duelServiceRoot;  // drag in Inspector
        IDuelService duels;

        [SerializeField] private MonoBehaviour curseServiceRoot;
        ICurseService queensCurse;

        [SerializeField] MonoBehaviour sacredRoadRoot;
        ISacredRoadService sacredRoad;

        [SerializeField] private MonoBehaviour resurrectionRoot;
        IResurrectionService resurrector;

        void Awake()
        {
            checker = new Checker(PieceAt);
            turnMgr = new TurnManager();
            validator = new MoveValidator(turnMgr, PieceAt, checker, GetKing, this);

            duels = (IDuelService)duelServiceRoot;
            duels.Init(PieceAt, queensCurse);
            queensCurse = (ICurseService)curseServiceRoot;
            sacredRoad = (ISacredRoadService)sacredRoadRoot;

            resurrector = (ResurrectionService)resurrectionRoot;
            resurrector.Init(PieceAt, AddPieceToBoard, captureManager);
        }


        public bool TryMove(Piece piece, int toRow, int toCol)
        {
            if (!validator.IsLegalMove(piece, toRow, toCol, out var info))
                return false;

            int fromRow = piece.Row;
            int fromCol = piece.Col;

            Piece captured = null;

            // ----- resolve capture or duel BEFORE commit -----
            Piece target = PieceAt(toRow, toCol);      // might be null

            if (target != null && target.Team != piece.Team)
            {
                // Need a duel only if defender's level is higher
                if (target.Level > piece.Level)
                {
                    var duelResult = duels.ResolveDuel(piece, target);
                    OnDuelRolled?.Invoke(duelResult.RawRoll);

                    if (!duelResult.AttackerWon)
                    {
                        // Attacker died, defender survives – turn ends here
                        captureManager.CapturePiece(pieces, piece, target);   // defender levels up

                        turnMgr.ToggleTurn();
                        queensCurse.TickTurn();
                        return true;
                    }
                    // else fall through: attacker wins, defender captured
                }

                // Auto-capture or attacker won duel
                captureManager.CapturePiece(pieces, target, piece);       // attacker levels up
                captured = target;           // for MoveResult

                if (target is Queen)
                    queensCurse.ApplyCurse(piece, 6);
            }


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

            // Bishop passive: make a new sacred path on every bishop move
            if (piece is Bishop)
            {
                var path = BuildBishopPath(fromRow, fromCol, toRow, toCol);
                sacredRoad.ActivateRoad(piece, path);   // replaces old road for that team
            }

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

            sacredRoad.ProcessMove(piece);

            turnMgr.ToggleTurn();          // flip side & clear old en‑passant square
            queensCurse.TickTurn(); 
            return true;
        }


        // REMOVE LATER CUZ THIS IS NOT A GOOD WAY OF DOING THIS, JUST FOR TESTS
        // expose current side
        public bool IsWhiteTurn => turnMgr.WhiteTurn;

        // GameControllerMono.cs
        public void CapturePiece(Piece captured, Piece winner)
        {
            captureManager.CapturePiece(pieces, captured, winner);
            if (captured is Queen)
                queensCurse.ApplyCurse(winner, 6);  // 6 half-moves = 3 full turns
        }


        // for the King's ultimate
        public bool TryRelocate(Piece piece, int toRow, int toCol)
        {
            if (PieceAt(toRow, toCol) != null) return false;

            int fromRow = piece.Row;
            int fromCol = piece.Col;

            piece.SetBoardCoords(toRow, toCol);
            OnMoveAccepted?.Invoke(new MoveResult(piece, fromRow, fromCol, toRow, toCol));
            return true;
        }

        List<Vector2Int> BuildBishopPath(int fr, int fc, int tr, int tc)
        {
            var list = new List<Vector2Int>();
            int dr = Math.Sign(tr - fr);
            int dc = Math.Sign(tc - fc);

            // squares BETWEEN start and end (exclude both ends)
            for (int r = fr + dr, c = fc + dc; r != tr && c != tc; r += dr, c += dc)
                list.Add(new Vector2Int(r, c));

            return list;
        }

        private void AddPieceToBoard(Piece p, int row, int col)
        {
            // Add to our authoritative list
            if (!pieces.Contains(p))
                pieces.Add(p);

            // Set board coords (no visuals)
            p.SetBoardCoords(row, col);

            // Let the board render it: fromRow/fromCol = -1 indicates "spawn"
            OnMoveAccepted?.Invoke(new MoveResult(p, -1, -1, row, col));
        }

        public bool ResurrectPawn(bool team) => resurrector.ResurrectPawn(team);
        public bool ResurrectPiece(bool team) => resurrector.ResurrectPiece(team);
        public Vector2Int? GetResurrectionSquare(bool team) => resurrector.GetResurrectionSquare(team);


    }
}
