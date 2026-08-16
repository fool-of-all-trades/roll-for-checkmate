using System.Collections.Generic;
using UnityEngine;
using Pieces;
using System.Linq;
using System;
using Unity.Netcode;
using static UnityEngine.GraphicsBuffer;
using System.Reflection;

namespace Controller
{
    [DisallowMultipleComponent]
    public class GameControllerMono : NetworkBehaviour, IGameController
    {
        List<Piece> pieces;

        /// <summary>
        /// Called once by ChessBoard in Awake to give us the live piece list.
        /// </summary>
        public void Initialize(IEnumerable<Piece> allPieces)
        {
            pieces = new List<Piece>(allPieces);
        }

        /* ───────────── GameController event contracts ───────────── */

        public event Action<MoveResult> OnMoveAccepted;
        public event Action<int> OnDuelRolled;

        public event Action<
            IReadOnlyList<Vector2Int>, 
            IReadOnlyList<Vector2Int>
            > OnRoadsChanged
        {
            add => sacredRoad.OnRoadsChanged += value;
            remove => sacredRoad.OnRoadsChanged -= value;
        }

        /* ────────────────────── Services ───────────────────────── */

        Checker checker;
        TurnManager turnMgr;
        CaptureManager captureManager;
        MoveValidator validator;

        [SerializeField] private MonoBehaviour duelServiceRoot;
        IDuelService duels;

        [SerializeField] private MonoBehaviour curseServiceRoot;
        ICurseService queensCurse;

        [SerializeField] MonoBehaviour sacredRoadRoot;
        ISacredRoadService sacredRoad;

        [SerializeField] private MonoBehaviour resurrectionRoot;
        IResurrectionService resurrector;


        private Dictionary<ulong, bool> _clientTeams = new Dictionary<ulong, bool>();
        public static GameControllerMono Instance { get; private set; }

        void Awake()
        {
            checker = new Checker(PieceAt);
            turnMgr = new TurnManager();
            captureManager = new CaptureManager();
            validator = new MoveValidator(turnMgr, PieceAt, checker, GetKing, this);

            duels = (IDuelService)duelServiceRoot;
            duels.Init(PieceAt, queensCurse);
            queensCurse = (ICurseService)curseServiceRoot;
            sacredRoad = (ISacredRoadService)sacredRoadRoot;

            resurrector = (ResurrectionService)resurrectionRoot;
            resurrector.Init(PieceAt, AddPieceToBoard, captureManager);


            if (Instance != null && Instance != this)
                Destroy(gameObject);
            else
                Instance = this;
        }

        /// <summary>
        /// Attempts to move a piece. Handles captures, duels, special rules, and events.
        /// Legality of the movement is checked before in the MoveValidator.IsLegalMove method.
        /// </summary>
        /// <returns>True if moved, false if move not possible</returns>
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
                        // Attacker died, defender survives and levels up – turn ends here
                        captureManager.CapturePiece(pieces, piece, target);

                        // After client reconnect they won't see ghosts of captured pieces
                        if (IsServer)
                        {
                            var npA = piece.GetComponent<NetworkPiece>();
                            if (npA) npA.IsCaptured.Value = true;
                        }

                        AdvanceTurn();

                        if (IsGameOver(piece))
                        {
                            //OnGameOver?.Invoke();               // <- add an event so ChessBoard can show UI
                            return true;                        // game ends
                        }

                        return true;
                    }
                    // else fall through: attacker wins, defender captured
                }

                // Auto-capture or attacker won duel
                captureManager.CapturePiece(pieces, target, piece);       // attacker levels up
                captured = target;           // for MoveResult

                // After client reconnect they won't see ghosts of captured pieces
                if (IsServer)
                {
                    var npD = target.GetComponent<NetworkPiece>();
                    if (npD) npD.IsCaptured.Value = true;
                }

                if (target is Queen)
                {
                    queensCurse.ApplyCurse(piece, 6);

                    if (NetworkManager.Singleton.IsServer)
                    {
                        var np = piece.GetComponent<NetworkPiece>();
                        if (np) np.CursedTurns.Value = 6;
                    }
                }
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

                        // After client reconnect they won't see ghosts of captured pieces
                        if (IsServer)
                        {
                            var npV = victim.GetComponent<NetworkPiece>();
                            if (npV) npV.IsCaptured.Value = true;
                        }
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

            var context = new MoveCommitContext(
                piece,
                fromRow,
                fromCol,
                toRow,
                toCol,
                captured);

            ProcessPostMoveEffects(context);

            AdvanceTurn();

            if (IsGameOver(piece))
            {
                //OnGameOver?.Invoke();               // <- add an event so ChessBoard can show UI
                return true;                        // game ends
            }

            return true;
        }

        public bool TryUseUltimate(Piece piece)
        {
            if (piece == null) return false;
            if (pieces == null || !pieces.Contains(piece)) return false;
            if (piece.Team != turnMgr.WhiteTurn) return false;
            if (!piece.CanUseUltimate()) return false;
            if (piece.StunnedTurns > 0) return false;

            piece.UseUltimateAbility(this);
            return true;
        }

        private readonly struct MoveCommitContext
        {
            public MoveCommitContext(
                Piece mover,
                int fromRow,
                int fromCol,
                int toRow,
                int toCol,
                Piece capturedPiece)
            {
                Mover = mover;
                FromRow = fromRow;
                FromCol = fromCol;
                ToRow = toRow;
                ToCol = toCol;
                CapturedPiece = capturedPiece;
            }

            public Piece Mover { get; }
            public int FromRow { get; }
            public int FromCol { get; }
            public int ToRow { get; }
            public int ToCol { get; }
            public Piece CapturedPiece { get; }
        }

        private void ProcessPostMoveEffects(MoveCommitContext context)
        {
            sacredRoad.ProcessMove(context.Mover);
        }

        void AdvanceTurn()
        {
            turnMgr.ToggleTurn();
            TurnSync.Instance?.CommitTurn(turnMgr.WhiteTurn);
            queensCurse.TickTurn();
            foreach (var p in pieces) p.TickStunnedTurns();
        }


        /// <summary>
        /// Relocates a piece instantly if destination is empty (used for King ultimate).
        /// </summary>
        public bool TryRelocate(Piece piece, int toRow, int toCol)
        {
            if (PieceAt(toRow, toCol) != null) return false;

            int fromRow = piece.Row;
            int fromCol = piece.Col;

            piece.SetBoardCoords(toRow, toCol);
            OnMoveAccepted?.Invoke(new MoveResult(piece, fromRow, fromCol, toRow, toCol));
            return true;
        }

        /// <summary>
        /// Finds the Piece at a given board coordinate, or null if empty.
        /// </summary>
        public Piece PieceAt(int row, int col) =>
            pieces.FirstOrDefault(p => p.Row == row && p.Col == col);

        /// <summary>
        /// Retrieves the king Piece for the specified team.
        /// </summary>
        public Piece GetKing(bool team) =>
            pieces.FirstOrDefault(p => p is King && p.Team == team);

        public bool IsCheck(Piece king, Piece lastPiece) =>
            lastPiece.IsValidMove(king.Row, king.Col);

        public bool CheckCheck(Piece piece, int destRow, int destCol) =>
            checker.PredictDanger(piece, GetKing(piece.Team), destRow, destCol, this);

        /// <summary>
        /// Determines if the current side has no legal moves (checkmate or stalemate).
        /// </summary>
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

        /// <summary>
        /// Generates the squares between two positions along a diagonal (excluding both endpoints).
        /// </summary>
        List<Vector2Int> BuildBishopPath(int fr, int fc, int tr, int tc)
        {
            var list = new List<Vector2Int>();
            int dr = Math.Sign(tr - fr);
            int dc = Math.Sign(tc - fc);

            for (int r = fr + dr, c = fc + dc; r != tr && c != tc; r += dr, c += dc)
                list.Add(new Vector2Int(r, c));

            return list;
        }

        /// <summary>
        /// Places or re‐adds a Piece in the game’s piece list and raises a spawn event.
        /// </summary>
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

        // Proxies
        public bool ResurrectPawn(bool team) => resurrector.ResurrectPawn(team);
        public bool ResurrectPiece(bool team) => resurrector.ResurrectPiece(team);
        public Vector2Int? GetResurrectionSquare(bool team) => resurrector.GetResurrectionSquare(team);


        // REMOVE LATER CUZ THIS IS NOT A GOOD WAY OF DOING THIS, JUST FOR TESTS
        // expose current side
        public bool IsWhiteTurn => turnMgr.WhiteTurn;

        public void CapturePiece(Piece captured, Piece winner)
        {
            captureManager.CapturePiece(pieces, captured, winner);
            if (captured is Queen)
            {
                queensCurse.ApplyCurse(winner, 6);  // 6 half-moves = 3 full turns

                if (NetworkManager.Singleton.IsServer)
                {
                    var np = winner.GetComponent<NetworkPiece>();
                    if (np) np.CursedTurns.Value = 6;
                }
            }
        }

    }
}
