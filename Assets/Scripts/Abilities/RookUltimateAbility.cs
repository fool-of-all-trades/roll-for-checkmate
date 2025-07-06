using UnityEngine;
using Controller;
using Pieces;

namespace Abilities
{
    /// <summary>
    /// Rook's ultimate ability: skips the first piece encountered in a direction and captures the second.
    /// </summary>
    public class RookUltimateAbility : Ability
    {
        public RookUltimateAbility(int levelRequirement) : base(levelRequirement)
        {
        }

        public override void UseAbility(GameController controller, Piece owner)
        {
            ChessBoard board = owner.board;
            int ownerRow = owner.Row;
            int ownerCol = owner.Col;
            Piece target = null;
            int targetRow = -1, targetCol = -1;

            // Directions: up, down, right, left
            int[][] dirs = new int[][]
            {
                new int[] {1, 0}, new int[] {-1, 0},
                new int[] {0, 1}, new int[] {0, -1}
            };

            foreach (var d in dirs)
            {
                int pieceCount = 0;
                int pieceRow = ownerRow;
                int pieceCol = ownerCol;

                while (true)
                {
                    pieceRow += d[0];
                    pieceCol += d[1];

                    if (!board.IsValidPosition(pieceRow, pieceCol))
                        break;

                    Piece p = board.GetPieceAt(pieceRow, pieceCol);
                    if (p != null)
                    {
                        if (pieceCount == 0)
                        {
                            // First piece: skip this one
                            pieceCount++;
                        }
                        else
                        {
                            // Second piece: potential target
                            if (p.Team != owner.Team)
                            {
                                target = p;
                                targetRow = pieceRow;
                                targetCol = pieceCol;
                            }
                            break;
                        }
                    }
                }

                if (target != null)
                    break;
            }

            if (target != null)
            {
                // Remove and capture the target
                board.RemovePiece(target);
                controller.CapturePiece(target, owner);

                // Teleport code commented out for future use:
                // owner.SetGridPosition(targetRow, targetCol);

                Debug.Log($"Wie¿a zestrzeli³a {target.Name} na ({targetRow}, {targetCol})");
                usedUltimate = true;
            }
            else
            {
                Debug.Log("Nie ma w co strzelaæ, ultimate nie zosta³ u¿yty.");
            }
        }
    }
}
