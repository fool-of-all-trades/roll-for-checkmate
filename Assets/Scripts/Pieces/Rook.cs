using UnityEngine;
using System;
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
        /// Sprawdza poprawnoœæ ruchu wie¿y (tylko ruch pionowo lub poziomo, bez przeskakiwania innych figur).
        /// </summary>
        public override bool IsValidMove(int newRow, int newCol)
        {
            // Out of the board?
            if (newRow < 0 || newRow > 7 || newCol < 0 || newCol > 7) return false;

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
