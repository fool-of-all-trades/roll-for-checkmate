using UnityEngine;
using System;
using Abilities;

namespace Pieces
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class Knight : Piece
    {
        private void Awake()
        {
            pieceName = "Knight";
            level = 2;
            // there ain't one yet
            //ultimateAbility = new KnightUltimateAbility(5);
        }

        /// <summary>
        /// Checks if the Knight's move is valid.
        /// Knight moves in an L-shape: two squares in one direction and one square perpendicular.
        /// </summary>
        public override bool IsValidMove(int newRow, int newCol)
        {
            // Out of the board?
            if (newRow < 0 || newRow > 7 || newCol < 0 || newCol > 7) return false;

            int rowDiff = newRow - Row;
            int colDiff = newCol - Col;

            // Knight's L-shaped movement: (2,1) or (1,2)
            return (Math.Abs(rowDiff) == 2 && Math.Abs(colDiff) == 1) ||
                           (Math.Abs(rowDiff) == 1 && Math.Abs(colDiff) == 2);
        }
    }
}
