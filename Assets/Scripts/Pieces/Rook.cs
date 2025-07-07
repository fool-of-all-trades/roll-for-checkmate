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
            if (!board.IsValidPosition(newRow, newCol))
                return false;

            // The same column or row or no move
            if (newRow != Row && newCol != Col)
                return false;

            // Can't move by 0
            if (newRow == Row && newCol == Col)
                return false;

            int rowStep = Math.Sign(newRow - Row);
            int colStep = Math.Sign(newCol - Col);

            int currentRow = Row + rowStep;
            int currentCol = Col + colStep;

            // See if nothing blocks the move, no interception in the middle
            while (currentRow != newRow || currentCol != newCol)
            {
                if (board.GetPieceAt(currentRow, currentCol) != null)
                    return false;

                currentRow += rowStep;
                currentCol += colStep;
            }

            // No piece at the target square or a piece of the opposite team -> can move
            Piece target = board.GetPieceAt(newRow, newCol);
            return target == null || target.Team != Team;
        }
    }
}
