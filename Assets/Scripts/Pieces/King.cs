using UnityEngine;
using Abilities;

namespace Pieces
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class King : Piece
    {
        private void Awake()
        {
            pieceName = "King";
            level = 3;
            ultimateAbility = new KingUltimateAbility();
        }

        /// <summary>
        /// Checks if the King's move to the target square is valid geometrically.
        /// Doesn't check for safety, that's the controller's job.
        /// </summary>
        public override bool IsValidMove(int newRow, int newCol)
        {
            // Out of the board?
            if (newRow < 0 || newRow > 7 || newCol < 0 || newCol > 7) 
                return false;

            // 1-step any direction
            if (Mathf.Abs(newRow - Row) <= 1 && Mathf.Abs(newCol - Col) <= 1)
                return true;

            // Castling pattern: same row, 2 squares horizontally.
            if (!HasMoved && newRow == Row && Mathf.Abs(newCol - Col) == 2)
                return true;

            return false;
        }
    }
}
