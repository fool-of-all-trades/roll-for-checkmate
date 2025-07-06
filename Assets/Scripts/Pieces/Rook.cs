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
            // Poza plansz¹?
            if (!board.IsValidPosition(newRow, newCol))
                return false;

            // Ruch musi byæ w tym samym wierszu lub kolumnie
            if (newRow != Row && newCol != Col)
                return false;

            // Nie przesuwamy siê o zero
            if (newRow == Row && newCol == Col)
                return false;

            // Krok ruchu: -1, 0 lub 1
            int rowStep = Math.Sign(newRow - Row);
            int colStep = Math.Sign(newCol - Col);

            int currentRow = Row + rowStep;
            int currentCol = Col + colStep;

            // Sprawdzamy œcie¿kê, czy nie blokuje nas ¿adna figura
            while (currentRow != newRow || currentCol != newCol)
            {
                if (board.GetPieceAt(currentRow, currentCol) != null)
                    return false;

                currentRow += rowStep;
                currentCol += colStep;
            }

            // Cel: pole jest puste lub stoi na nim figura przeciwnika
            Piece target = board.GetPieceAt(newRow, newCol);
            return target == null || target.Team != Team;
        }
    }
}
