using Pieces;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using System.Linq;

namespace Abilities
{
    /// <summary>
    /// Rook's ultimate ability: skips the first piece it "sees" in any
    /// rook direction and captures the next enemy piece behind it.
    /// The rook itself does not move.
    /// </summary>
    public class RookUltimateAbility : Ability
    {
        public RookUltimateAbility(int levelRequirement) : base(levelRequirement) { }

        public override void UseAbility(IGameController controller, Piece owner)
        {
            if (usedUltimate)
            {
                Debug.Log("Ultimate already used.");
                return;
            }

            if (owner.StunnedTurns > 0)
            {
                Debug.Log("Rook is stunned and cannot use abilities.");
                return;
            }

            // 1) Gather all valid targets
            List<Piece> targets = FindTargets(controller, owner);
            if (targets.Count == 0)
            {
                Debug.Log("No valid sniper targets.");
                return;
            }

            // 2) Ask the board to start target-selection mode
            ChessBoard board = owner.board;

            board.BeginTargetSelection(
                targets,
                target =>
                {
                    if (target == null)
                    {
                        Debug.Log("Sniper shot canceled.");
                        return;
                    }

                    controller.CapturePiece(target, owner);   // XP + hide prefab
                    Debug.Log($"Rook sniped {target.Name} at ({target.Row},{target.Col})");
                    usedUltimate = true;
                }
            );
        }

        /// <summary>
        /// Returns every second enemy in rook rays.
        /// </summary>
        List<Piece> FindTargets(IGameController ctrl, Piece owner)
        {
            var list = new List<Piece>();
            int r0 = owner.Row, c0 = owner.Col;
            var dirs = new (int dr, int dc)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

            foreach (var (dr, dc) in dirs)
            {
                bool skipped = false;
                int r = r0, c = c0;

                while (true)
                {
                    r += dr; c += dc;
                    if (r < 0 || r > 7 || c < 0 || c > 7) break;

                    Piece p = ctrl.PieceAt(r, c);
                    if (p == null) continue;

                    if (!skipped)
                        skipped = true; // skip the first piece in this ray
                    else
                    {
                        if (p.Team != owner.Team)
                            list.Add(p);          // second piece: enemy target
                        break;                    // stop this ray
                    }
                }
            }
            return list;
        }
    }
}