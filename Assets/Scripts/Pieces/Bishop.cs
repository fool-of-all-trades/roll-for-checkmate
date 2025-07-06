using UnityEngine;
using System;
using Abilities;

namespace Pieces
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class Bishop : Piece
    {
        private void Awake()
        {
            pieceName = "Bishop";
            level = 2;

            ultimateAbility = new BishopUltimateAbility(5);
        }

        /// <summary>
        /// Checks if the Bishop's move to the target square is valid.
        /// Bishop moves diagonally without jumping over pieces.
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

            // Must move diagonally
            if (rowDiff != colDiff)
                return false;

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

            // Destination must be empty or contain an enemy piece
            Piece target = board.GetPieceAt(newRow, newCol);
            return target == null || target.Team != Team;
        }
    }
}
