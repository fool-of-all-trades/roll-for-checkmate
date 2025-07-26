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
        /// Checks if the Bishop's move to the target square is valid geometrically.
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

            // 3. Shape: must be diagonal
            return Math.Abs(newRow - Row) == Math.Abs(newCol - Col);
        }
    }
}
