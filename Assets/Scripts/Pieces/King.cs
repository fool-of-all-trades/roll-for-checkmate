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
            // Check board boundaries
            if (!board.IsValidPosition(newRow, newCol))
                return false;

            int rowDiff = newRow - Row;
            int colDiff = newCol - Col;

            // Cannot stay in place
            if (rowDiff == 0 && colDiff == 0)
                return false;

            // Standard one-square move in any direction
            if (Math.Abs(rowDiff) <= 1 && Math.Abs(colDiff) <= 1)
            {
                Piece target = board.GetPieceAt(newRow, newCol);
                // Do not capture own piece
                if (target != null && target.Team == Team)
                    return false;
                return true;
            }

            // Castling: two squares horizontally, King must not have moved
            if (!HasMoved && newRow == Row && Math.Abs(colDiff) == 2)
            {
                int direction = Math.Sign(colDiff); // +1 for king-side, -1 for queen-side
                int rookCol = direction > 0 ? 7 : 0;
                Piece rook = board.GetPieceAt(Row, rookCol);

                // Rook exists, is a Rook, same team, and hasn't moved
                if (!(rook is Rook) || rook.Team != Team || ((Rook)rook).HasMoved)
                    return false;

                // No pieces between King and Rook
                for (int c = Col + direction; c != rookCol; c += direction)
                {
                    if (board.GetPieceAt(Row, c) != null)
                        return false;
                }

                // King cannot pass through or end in check
                // We check each square from current to destination + one beyond
                for (int c = Col; c != newCol + direction; c += direction)
                {
                    if (GameController.Instance.CheckCheck(this, Row, c))
                        return false;
                }

                return true;
            }

            // Any other move is invalid
            return false;
        }
    }
}
