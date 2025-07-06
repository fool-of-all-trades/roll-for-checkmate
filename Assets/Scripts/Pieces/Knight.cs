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
            //ultimateAbility = new KnightUltimateAbility(5);
        }

        /// <summary>
        /// Checks if the Knight's move is valid.
        /// Knight moves in an L-shape: two squares in one direction and one square perpendicular.
        /// </summary>
        public override bool IsValidMove(int newRow, int newCol)
        {
            // Check board boundaries
            if (!board.IsValidPosition(newRow, newCol))
                return false;

            int rowDiff = newRow - Row;
            int colDiff = newCol - Col;

            // Knight's L-shaped movement: (2,1) or (1,2)
            bool isLMove = (Math.Abs(rowDiff) == 2 && Math.Abs(colDiff) == 1) ||
                           (Math.Abs(rowDiff) == 1 && Math.Abs(colDiff) == 2);
            if (!isLMove)
                return false;

            // Check if destination is occupied by own piece
            Piece target = board.GetPieceAt(newRow, newCol);
            if (target != null && target.Team == Team)
                return false;

            return true;
        }
    }
}
