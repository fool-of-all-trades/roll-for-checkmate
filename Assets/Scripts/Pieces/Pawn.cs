using UnityEngine;
using System;

namespace Pieces
{
    [RequireComponent(typeof(SpriteRenderer))]
    public class Pawn : Piece
    {
        private void Awake()
        {
            pieceName = "Pawn";
        }

        /// <summary>
        /// Checks if the pawn's move to the target square is valid geometrically.
        /// Doesn't check for safety, that's the controller's job.
        /// </summary>
        public override bool IsValidMove(int newRow, int newCol)
        {
            // Out of the board?
            if (newRow < 0 || newRow > 7 || newCol < 0 || newCol > 7) 
                return false;

            int direction = Team ? 1 : -1;
            int rowDiff = newRow - Row;
            int colDiff = newCol - Col;

            // Forward moves (1 or 2 depending on start row) -> just geometry
            bool atStart = (Team && Row == 1) || (!Team && Row == 6);
            if (colDiff == 0 && (rowDiff == direction || (atStart && rowDiff == 2 * direction)))
                return true;

            // Diagonal capture/en-passant shape: 1 row forward, 1 col sideways
            // Only  possible if done just after the opponent's pawn moved two squares forward
            if (Math.Abs(colDiff) == 1 && rowDiff == direction)
                return true;

            return false;
        }
    }
}
