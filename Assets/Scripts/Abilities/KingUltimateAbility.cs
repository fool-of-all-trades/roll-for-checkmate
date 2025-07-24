using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Controller;
using Pieces;

namespace Abilities
{
    /// <summary>
    /// King's ultimate ability: pushes an adjacent enemy two tiles away if possible.
    /// </summary>
    public class KingUltimateAbility : Ability
    {
        public KingUltimateAbility() : base(0)
        {
        }

        public override void UseAbility(IGameController controller, Piece owner)
        {
            if (usedUltimate)
            {
                Debug.Log("Król ju¿ skorzysta³ z jednorazowego pchniêcia.");
                return;
            }

            ChessBoard board = owner.board;
            int kr = owner.Row;
            int kc = owner.Col;
            bool pushed = false;

            // Directions to search for enemies
            int[][] dirs = new int[][]
            {
                new int[] {1, 0}, new int[] {-1, 0},
                new int[] {0, 1}, new int[] {0, -1},
                new int[] {1, 1}, new int[] {1, -1},
                new int[] {-1, 1}, new int[] {-1, -1}
            };

            foreach (var d in dirs)
            {
                int enemyRow = kr + d[0];
                int enemyCol = kc + d[1];

                if (!board.IsValidPosition(enemyRow, enemyCol))
                    continue;

                Piece target = board.GetPieceAt(enemyRow, enemyCol);
                if (target != null && target.Team != owner.Team)
                {
                    // Attempt to push two tiles away
                    int pushRow = enemyRow + d[0] * 2;
                    int pushCol = enemyCol + d[1] * 2;

                    if (board.IsValidPosition(pushRow, pushCol) && board.GetPieceAt(pushRow, pushCol) == null)
                    {
                        target.SetGridPosition(pushRow, pushCol);
                        Debug.Log($"Król wypycha {target.Name} na ({pushRow}, {pushCol})");
                    }
                    else
                    {
                        Debug.Log("Nie mo¿na wypchn¹æ: miejsce za jest zajête albo poza plansz¹.");
                    }

                    usedUltimate = true;
                    pushed = true;
                    break;
                }
            }

            if (!pushed)
            {
                Debug.Log("Brak wrogów w s¹siedztwie, umiejêtnoœæ nie zosta³a u¿yta.");
            }
        }
    }
}
