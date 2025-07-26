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
        /// Checks if the Queen's move to the target square is valid geometrically.
        /// Doesn't check for safety, that's the controller's job.
        /// </summary>
        public override bool IsValidMove(int newRow, int newCol)
        {
            // Out of the board?
            if (newRow < 0 || newRow > 7 || newCol < 0 || newCol > 7) 
                return false;

            // Cannot move to the same square
            if (newRow == Row && newCol == Col)
                return false;

            int rowDiff = Math.Abs(newRow - Row);
            int colDiff = Math.Abs(newCol - Col);

            bool diagonal = rowDiff == colDiff;
            bool straight = (newRow == Row) || (newCol == Col);

            // Queen can move diagonally or straight
            return diagonal || straight;
        }
    }
}
