using UnityEngine;
using System;

namespace Pieces
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class Queen : Piece
    {
        private void Awake()
        {
            pieceName = "Queen";
            level = 3;
        }

        /// <summary>
        /// Checks if the Queen's move to the target square is valid.
        /// Queen can move diagonally, vertically, or horizontally without jumping over pieces.
        /// </summary>
        public override bool IsValidMove(int newRow, int newCol)
        {
            // Check board boundaries
            if (!board.IsValidPosition(newRow, newCol))
                return false;

            // Cannot move to the same square
            if (newRow == Row && newCol == Col)
                return false;

            int rowDiff = Math.Abs(newRow - Row);
            int colDiff = Math.Abs(newCol - Col);

            // Diagonal movement
            if (rowDiff == colDiff)
            {
                int rowStep = Math.Sign(newRow - Row);
                int colStep = Math.Sign(newCol - Col);
                int currentRow = Row + rowStep;
                int currentCol = Col + colStep;

                // Ensure no pieces block the path
                while (currentRow != newRow && currentCol != newCol)
                {
                    if (board.GetPieceAt(currentRow, currentCol) != null)
                        return false;
                    currentRow += rowStep;
                    currentCol += colStep;
                }

                Piece target = board.GetPieceAt(newRow, newCol);
                return target == null || target.Team != Team;
            }

            // Vertical or horizontal movement
            if (newRow == Row || newCol == Col)
            {
                int rowStep = Math.Sign(newRow - Row);
                int colStep = Math.Sign(newCol - Col);
                int currentRow = Row + rowStep;
                int currentCol = Col + colStep;

                // Ensure no pieces block the path
                while (currentRow != newRow || currentCol != newCol)
                {
                    if (board.GetPieceAt(currentRow, currentCol) != null)
                        return false;
                    currentRow += rowStep;
                    currentCol += colStep;
                }

                Piece target = board.GetPieceAt(newRow, newCol);
                return target == null || target.Team != Team;
            }

            // Move is neither diagonal nor straight
            return false;
        }
    }
}
