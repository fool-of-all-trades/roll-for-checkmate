using System;
using Pieces;
using UnityEngine;

namespace Controller
{
    /// <summary>
    /// Pure rule helper. Predicts if the king will be in danger after a hypothetical move.
    /// NO visual or board-manipulating code here.
    /// </summary>
    public class Checker
    {
        private Piece king;
        private Func<int, int, Piece> _pieceAt;   // delegate to query the board

        /// <summary>
        /// Construct with a lookup delegate. Usually pass controller.PieceAt.
        /// </summary>
        public Checker(Func<int, int, Piece> pieceAt)
        {
            _pieceAt = pieceAt;
        }

        #region Range checks (use _pieceAt instead of board.GetPieceAt)
        private bool RangeOfBishopOrQueen()
        {
            Vector2Int[] directions = {
                new Vector2Int(-1, -1), new Vector2Int(-1,  1),
                new Vector2Int( 1, -1), new Vector2Int( 1,  1)
            };

            foreach (var d in directions)
            {
                for (int i = 1; i < 8; i++)
                {
                    int r = king.Row + d.x * i;
                    int c = king.Col + d.y * i;
                    var piece = _pieceAt(r, c);
                    if (piece != null)
                    {
                        if (piece.Team != king.Team && (piece is Bishop || piece is Queen))
                            return true;
                        break;
                    }
                }
            }
            return false;
        }

        private bool RangeOfRookOrQueen()
        {
            Vector2Int[] directions = {
                new Vector2Int(-1, 0), new Vector2Int( 1, 0),
                new Vector2Int( 0,-1), new Vector2Int( 0, 1)
            };

            foreach (var d in directions)
            {
                for (int i = 1; i < 8; i++)
                {
                    int r = king.Row + d.x * i;
                    int c = king.Col + d.y * i;
                    var piece = _pieceAt(r, c);
                    if (piece != null)
                    {
                        if (piece.Team != king.Team && (piece is Rook || piece is Queen))
                            return true;
                        break;
                    }
                }
            }
            return false;
        }

        private bool RangeOfKnight()
        {
            Vector2Int[] offsets = {
                new Vector2Int(-2, -1), new Vector2Int(-2,  1),
                new Vector2Int(-1, -2), new Vector2Int(-1,  2),
                new Vector2Int( 1, -2), new Vector2Int( 1,  2),
                new Vector2Int( 2, -1), new Vector2Int( 2,  1)
            };

            foreach (var d in offsets)
            {
                int r = king.Row + d.x;
                int c = king.Col + d.y;
                var piece = _pieceAt(r, c);
                if (piece != null && piece.Team != king.Team && piece is Knight)
                    return true;
            }
            return false;
        }

        private bool RangeOfKing()
        {
            Vector2Int[] offsets = {
                new Vector2Int(-1, -1), new Vector2Int(-1,  0), new Vector2Int(-1,  1),
                new Vector2Int( 0, -1),                      new Vector2Int( 0,  1),
                new Vector2Int( 1, -1), new Vector2Int( 1,  0), new Vector2Int( 1,  1)
            };

            foreach (var d in offsets)
            {
                int r = king.Row + d.x;
                int c = king.Col + d.y;
                var piece = _pieceAt(r, c);
                if (piece != null && piece.Team != king.Team && piece is King)
                    return true;
            }
            return false;
        }

        private bool RangeOfPawn()
        {
            Vector2Int[] directions = king.Team
                ? new[] { new Vector2Int(1, -1), new Vector2Int(1, 1) }   // white king: pawns move "up"
                : new[] { new Vector2Int(-1, -1), new Vector2Int(-1, 1) }; // black king: pawns move "down"

            foreach (var d in directions)
            {
                int r = king.Row + d.x;
                int c = king.Col + d.y;
                var piece = _pieceAt(r, c);
                if (piece != null && piece.Team != king.Team && piece is Pawn)
                    return true;
            }
            return false;
        }

        private bool IsKingInDanger()
        {
            return RangeOfRookOrQueen()
                || RangeOfBishopOrQueen()
                || RangeOfKing()
                || RangeOfKnight()
                || RangeOfPawn();
        }
        #endregion

        /// <summary>
        /// Simulate moving 'piece' to (destRow, destCol) and return whether king would be in check.
        /// No visual calls, no ChessBoard.
        /// </summary>
        public bool PredictDanger(
            Piece piece,
            Piece kingPiece,
            int destRow,
            int destCol,
            IGameController controller)
        {
            king = kingPiece;

            // Snapshot
            int originalRow = piece.Row;
            int originalCol = piece.Col;

            Piece captured = controller.PieceAt(destRow, destCol);
            if (captured != null && captured.Level > piece.Level)
                captured = null; // your original logic

            // Build a temporary board lookup that reflects the hypothetical move
            Piece TempLookup(int r, int c)
            {
                // Piece has moved: its original square is now empty
                if (r == originalRow && c == originalCol)
                    return null;

                // Destination square now holds 'piece' unless capture is disallowed above
                if (r == destRow && c == destCol)
                    return piece;

                // If something was captured, pretend it's gone
                if (captured != null && r == destRow && c == destCol)
                    return piece;

                // Everything else is the real board
                return controller.PieceAt(r, c);
            }

            // Swap delegates
            var oldLookup = _pieceAt;
            _pieceAt = TempLookup;

            // Temporarily move the piece (so piece.Row/Col reflect the new spot for threat checks)
            piece.SetBoardCoords(destRow, destCol);   // or SetGridPosition if that's your method name

            bool result = IsKingInDanger();

            // Revert
            piece.SetBoardCoords(originalRow, originalCol);
            _pieceAt = oldLookup;

            return result;
        }
    }
}
