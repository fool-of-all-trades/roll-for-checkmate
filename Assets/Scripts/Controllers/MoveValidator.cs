using System;
using Pieces;
using UnityEngine;

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
        readonly Func<int, int, Piece> pieceAt;   // board lookup
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

        // --------------  DTO returned to controller  --------------
        public readonly struct MoveAnalysis
        {
            public readonly bool IsCastle;
            public readonly Rook CastleRook;     // null if not castle
            public MoveAnalysis(bool castle, Rook rook)
            {
                IsCastle = castle; CastleRook = rook;
            }
        }

        // --------------  PUBLIC ENTRY POINT  --------------
        public bool IsLegalMove(Piece piece, int toRow, int toCol,
                                out MoveAnalysis analysis)
        {
            analysis = default;

            // 0) Null / wrong turn / shape
            if (piece == null ||
                piece.Team != turnMgr.WhiteTurn ||
                !piece.IsValidMove(toRow, toCol))
                return false;

            int fr = piece.Row, fc = piece.Col;

            // 1) Path & destination (per piece type)
            if (!GeometryAndDestinationOk(piece, fr, fc, toRow, toCol))
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

        // --------------  Piece-specific geometry & path  --------------
        bool GeometryAndDestinationOk(Piece piece, int fr, int fc, int tr, int tc)
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
                    return ValidatePawnShape(p, fr, fc, tr, tc)
                           && PawnPathOk(p, fr, fc, tr, tc);

                case King:
                    return DestinationOk(piece, tr, tc);

                default: return false;
            }
        }

        bool DestinationOk(Piece mover, int r, int c)
        {
            var target = pieceAt(r, c);
            return target == null || target.Team != mover.Team;
        }

        // --------------  Pawn helpers  --------------
        bool ValidatePawnShape(Pawn pawn, int fr, int fc, int tr, int tc)
        {
            int dir = pawn.Team ? 1 : -1;
            int rowDiff = tr - fr;
            int colDiff = tc - fc;

            bool atStart = (pawn.Team && fr == 1) || (!pawn.Team && fr == 6);

            // Forward 1 / 2
            if (colDiff == 0 &&
                (rowDiff == dir || (atStart && rowDiff == 2 * dir)))
                return true;

            // Diagonal capture/en-passant shape
            if (Math.Abs(colDiff) == 1 && rowDiff == dir)
                return true;

            return false;
        }

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
            var target = pieceAt(tr, tc);
            if (target != null && target.Team != pawn.Team) return true;

            var epsq = turnMgr.EnPassantSquare;
            return epsq.HasValue && epsq.Value.row == tr && epsq.Value.col == tc;
        }

        // --------------  Castling helpers  --------------
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

        bool CastleRulesOk(King king, Rook rook, int fr, int fc, int tc)
        {
            // Rook must not have moved
            if (rook.HasMoved) return false;

            // Squares between king & rook empty
            if (!PathUtils.PathClearExceptEndpoints(pieceAt, king.Row, fc, rook.Col)) return false;

            // Squares the king crosses must be safe
            int dir = Math.Sign(tc - fc);
            for (int c = fc; c != tc + dir; c += dir)
                if (!SquaresSafeForCurrentSide(king, king.Row, c)) return false;

            return true;
        }

        // --------------  King-safety  --------------
        bool SquaresSafeForCurrentSide(Piece mover, int kingDestRow, int kingDestCol)
        {
            // checker.PredictDanger already simulates piece movement for us
            return !checker.PredictDanger(mover,
                                          getKing(mover.Team),
                                          kingDestRow, kingDestCol,
                                          controller);
        }
    }
}
