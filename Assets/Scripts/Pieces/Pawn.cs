using UnityEngine;
using System;
using System.Collections.Generic;

namespace Pieces
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class Pawn : Piece
    {
        private void Awake()
        {
            pieceName = "Pawn";
        }

        /// <summary>
        /// Checks if the pawn can move to the specified position
        /// </summary>
        public override bool IsValidMove(int newRow, int newCol)
        {
            // Out of the board?
            if (!board.IsValidPosition(newRow, newCol))
                return false;

            int direction = Team ? 1 : -1;
            int rowDiff = newRow - Row;
            int colDiff = newCol - Col;

            // Move forward
            if (colDiff == 0)
            {
                // 1 hop
                if (rowDiff == direction && board.GetPieceAt(newRow, newCol) == null)
                    return true;

                // 2 hops
                bool atStart = (Team && Row == 1) || (!Team && Row == 6);
                if (atStart && rowDiff == 2 * direction)
                {
                    int midRow = Row + direction;
                    if (board.GetPieceAt(midRow, Col) == null &&
                        board.GetPieceAt(newRow, newCol) == null)
                    {
                        return true;
                    }
                }
            }

            // Diagonal capture
            if (Math.Abs(colDiff) == 1 && rowDiff == direction)
            {
                Piece target = board.GetPieceAt(newRow, newCol);
                if (target != null && target.Team != Team)
                    return true;

                // En Passant
                Vector2Int ep = board.EnPassantTile;
                if (ep.x == newRow && ep.y == newCol)
                    return true;
            }

            return false;
        }
    }
}
