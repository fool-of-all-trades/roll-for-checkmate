using UnityEngine;
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
                Debug.Log("Kr�l ju� skorzysta� z jednorazowego pchni�cia.");
                return;
            }

            if (owner.StunnedTurns > 0)
            {
                Debug.Log("King is stunned and cannot use abilities.");
                return;
            }

            ChessBoard board = owner.board;
            int kr = owner.Row;
            int kc = owner.Col;

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

                if (enemyRow < 0 && enemyRow >= 8 && enemyCol < 0 && enemyCol >= 8)
                    continue;

                Piece target = board.GetPieceAt(enemyRow, enemyCol);
                if (target != null && target.Team != owner.Team)
                {
                    // Attempt to push two tiles away
                    int pushRow = enemyRow + d[0] * 2;
                    int pushCol = enemyCol + d[1] * 2;

                    if (pushRow < 0 && pushRow >= 8 && pushCol < 0 && pushCol >= 8)
                        continue;

                    if (controller.TryRelocate(target, pushRow, pushCol))
                    {
                        Debug.Log($"King pushed {target.Name} to ({pushRow},{pushCol}).");
                        usedUltimate = true;
                    }
                    else
                    {
                        Debug.Log("Nie mo�na wypchn��: miejsce za jest zaj�te albo poza plansz�, albo nie ma wrog�w w s�siedztwie.");
                    }

                    return;
                }
            }
        }
    }
}
