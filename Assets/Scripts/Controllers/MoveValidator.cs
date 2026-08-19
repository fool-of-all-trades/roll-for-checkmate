using System;
using Pieces;

namespace Controller
{
    /// <summary>
    /// Stateless rule engine: path blocking, destination legality,
    /// castling checks, king-safety, en-passant flags.
    /// Does NOT mutate board or piece lists.
    /// </summary>
    public class MoveValidator
    {
        // --------------  DEPENDENCIES  --------------
        readonly TurnManager turnMgr;
        readonly Func<int, int, Piece> pieceAt;
        readonly Checker checker;
        readonly Func<bool, Piece> getKing;
        readonly IGameController controller;

        public MoveValidator(TurnManager tm,
                             Func<int, int, Piece> pieceAt,
                             Checker checker,
                             Func<bool, Piece> getKing,
                             IGameController controller)
        {
            turnMgr = tm;
            this.pieceAt = pieceAt;
            this.checker = checker;
            this.getKing = getKing;
            this.controller = controller;
        }

        /// <summary>
        /// DTO returned to controller when a move is legal.
        /// </summary>
        public readonly struct MoveAnalysis
        {
            public readonly bool IsCastle;
            public readonly Rook CastleRook;     // null if not castle
            public MoveAnalysis(bool castle, Rook rook)
            {
                IsCastle = castle; CastleRook = rook;
            }
        }

        /// <summary>
        /// Checks if the move is legal (shape, castling, king safety) for the given piece.
        /// </summary>
        public bool IsLegalMove(Piece piece, int toRow, int toCol,
                                out MoveAnalysis analysis)
        {
            analysis = default;

            // -1) Piece can't move cuz of Divine Smite
            if (piece.IsStunned()) return false;

            // 0) Null / wrong turn / shape
            if (piece == null ||
                piece.Team != turnMgr.WhiteTurn ||
                !piece.IsValidMove(toRow, toCol))
                return false;

            int fr = piece.Row, fc = piece.Col;

            // 1) Path & destination (per piece type)
            if (!PathClearAndDestinationOk(piece, fr, fc, toRow, toCol))
                return false;

            // 2) Castling extra rules
            bool isCastle = false;
            Rook rook = null;
            if (piece is King)
            {
                isCastle = IsCastleAttempt((King)piece, toRow, toCol, out rook);
                if (isCastle && !CastleRulesOk((King)piece, rook, fr, fc, toCol))
                    return false;
            }

            // 3) King-safety (for ALL moves incl. final castle square)
            if (!SquaresSafeForCurrentSide(piece, toRow, toCol))
                return false;

            analysis = new MoveAnalysis(isCastle, rook);
            return true;
        }

        /// <summary>
        /// Verifies piece‑specific path and target occupancy.
        /// </summary>
        bool PathClearAndDestinationOk(Piece piece, int fr, int fc, int tr, int tc)
        {
            switch (piece)
            {
                case Bishop:
                    return PathUtils.PathClearDiagonal(pieceAt, fr, fc, tr, tc)
                           && DestinationOk(piece, tr, tc);

                case Rook:
                    return PathUtils.PathClearStraight(pieceAt, fr, fc, tr, tc)
                           && DestinationOk(piece, tr, tc);

                case Queen:
                    bool diag = Math.Abs(tr - fr) == Math.Abs(tc - fc);
                    if (!diag && fr != tr && fc != tc) return false;

                    bool clear = diag
                        ? PathUtils.PathClearDiagonal(pieceAt, fr, fc, tr, tc)
                        : PathUtils.PathClearStraight(pieceAt, fr, fc, tr, tc);
                    return clear && DestinationOk(piece, tr, tc);

                case Knight:
                    return DestinationOk(piece, tr, tc);

                case Pawn p:
                    //return ValidatePawnShape(p, fr, fc, tr, tc)
                    //       && PawnPathOk(p, fr, fc, tr, tc);
                    return PawnPathOk(p, fr, fc, tr, tc);

                case King:
                    return DestinationOk(piece, tr, tc);

                default: return false;
            }
        }

        /// <summary>
        /// True if target square is empty or holds an enemy piece.
        /// </summary>
        bool DestinationOk(Piece mover, int r, int c)
        {
            var target = pieceAt(r, c);
            return target == null || target.Team != mover.Team;
        }

        /// <summary>
        /// Verifies pawn path is clear and handles captures or en‑passant eligibility.
        /// </summary>
        bool PawnPathOk(Pawn pawn, int fr, int fc, int tr, int tc)
        {
            int dir = pawn.Team ? 1 : -1;
            int colDiff = tc - fc;
            int rowDiff = tr - fr;

            // Forward move needs empty target (and mid square for double)
            if (colDiff == 0)
            {
                if (pieceAt(tr, tc) != null) return false;
                if (Math.Abs(rowDiff) == 2 && pieceAt(fr + dir, fc) != null) return false;

                // Set en-passant flag when we *commit* (controller does it)
                return true;
            }

            // Diagonal: must capture something OR match en-passant square
            if (DestinationOk(pawn, tr, tc) && pieceAt(tr, tc) != null)
                return true;

            return turnMgr.TryGetValidEnPassantVictim(pawn, tr, tc, pieceAt, out _);
        }

        #region Castling helpers

        /// <summary>
        /// Detects a king’s two‑square move and fetches the corresponding rook.
        /// </summary>
        bool IsCastleAttempt(King king, int tr, int tc, out Rook rook)
        {
            rook = null;

            if (king.HasMoved || king.Row != tr || Math.Abs(tc - king.Col) != 2)
                return false;

            int dir = Math.Sign(tc - king.Col);
            int rookCol = dir < 0 ? 0 : 7;
            rook = pieceAt(king.Row, rookCol) as Rook;
            return rook != null;
        }

        /// <summary>
        /// Ensures rook unmoved, path empty, and no square crossed is under attack.
        /// </summary>
        bool CastleRulesOk(King king, Rook rook, int fr, int fc, int tc)
        {
            // Rook must not have moved
            if (rook.HasMoved) return false;

            // Squares between king and rook empty
            if (!PathUtils.PathClearExceptEndpoints(pieceAt, king.Row, fc, rook.Col)) return false;

            // Squares the king crosses must be safe
            int dir = Math.Sign(tc - fc);
            for (int c = fc; c != tc + dir; c += dir)
                if (!SquaresSafeForCurrentSide(king, king.Row, c)) return false;

            return true;
        }

        /// <summary>
        /// Simulates the move and returns false if it would leave the king in check.
        /// </summary>
        bool SquaresSafeForCurrentSide(Piece mover, int kingDestRow, int kingDestCol)
        {
            return !checker.PredictDanger(mover, getKing(mover.Team),
                                          kingDestRow, kingDestCol,
                                          controller);
        }
        #endregion
    }
}
