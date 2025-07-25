using UnityEngine;
using System;
using Controller;
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
        /// Checks if the King's move to the target square is valid.
        /// King moves one square in any direction, and can castle under specific conditions.
        /// </summary>
        public override bool IsValidMove(int newRow, int newCol)
        {
            // Out of the board?
            if (newRow < 0 || newRow > 7 || newCol < 0 || newCol > 7) return false;

            // 1-step any direction
            if (Mathf.Abs(newRow - Row) <= 1 && Mathf.Abs(newCol - Col) <= 1)
                return true;

            // Castling pattern: same row, 2 squares horizontally.
            if (!HasMoved && newRow == Row && Mathf.Abs(newCol - Col) == 2)
                return true; // We only say "geometry is fine"; safety is controller’s job.

            return false;
        }
    }
}
