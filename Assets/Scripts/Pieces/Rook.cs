using UnityEngine;
using Abilities;

namespace Pieces
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class Rook : Piece
    {
        private void Awake()
        {
            pieceName = "Rook";
            level = 2;

            ultimateAbility = new RookUltimateAbility(5);
        }

        /// <summary>
        /// Checks if the Rook's move to the target square is valid geometrically.
        /// Doesn't check for safety, that’s the controller’s job.
        /// </summary>
        public override bool IsValidMove(int newRow, int newCol)
        {
            // Out of the board?
            if (newRow < 0 || newRow > 7 || newCol < 0 || newCol > 7) 
                return false;

            // The same column or row or no move
            if (newRow != Row && newCol != Col)
                return false;

            // Can't move by 0
            if (newRow == Row && newCol == Col)
                return false;

            return true;
        }
    }
}
