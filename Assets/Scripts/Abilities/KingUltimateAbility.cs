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

            bool pushedAny = false;

            foreach (var d in dirs)
            {
                int enemyRow = kr + d[0];
                int enemyCol = kc + d[1];

                if (enemyRow < 0 || enemyRow >= 8 || enemyCol < 0 || enemyCol >= 8)
                    continue;

                Piece target = controller.PieceAt(enemyRow, enemyCol);
                if (target != null && target.Team != owner.Team)
                {
                    var targetNetworkPiece = target.GetComponent<NetworkPiece>();
                    if (targetNetworkPiece != null && targetNetworkPiece.IsCaptured.Value)
                        continue;

                    foreach (int distance in new[] { 2, 3 })
                    {
                        int pushRow = enemyRow + d[0] * distance;
                        int pushCol = enemyCol + d[1] * distance;

                        if (!IsClearPushPath(controller, enemyRow, enemyCol, d[0], d[1], distance))
                            continue;

                        if (controller.TryRelocate(target, pushRow, pushCol))
                        {
                            Debug.Log($"King pushed {target.Name} to ({pushRow},{pushCol}).");
                            pushedAny = true;
                            break;
                        }
                    }
                }
            }

            if (pushedAny)
            {
                usedUltimate = true;
            }
            else
            {
                Debug.Log("King shockwave found no adjacent enemies with a valid push destination.");
            }
        }

        private bool IsClearPushPath(IGameController controller, int startRow, int startCol, int dr, int dc, int distance)
        {
            for (int step = 1; step <= distance; step++)
            {
                int row = startRow + dr * step;
                int col = startCol + dc * step;

                if (row < 0 || row >= 8 || col < 0 || col >= 8)
                    return false;

                if (controller.PieceAt(row, col) != null)
                    return false;
            }

            return true;
        }
    }
}
