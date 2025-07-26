using Pieces;
using UnityEngine;

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

            int ownerRow = owner.Row;
            int ownerCol = owner.Col;

            // Directions: up, down, right, left
            var dirs = new (int dr, int dc)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

            Piece target = null;

            foreach (var (dr, dc) in dirs)
            {
                bool skippedFirst = false;
                int r = ownerRow;
                int c = ownerCol;

                while (true)
                {
                    r += dr;
                    c += dc;

                    if (r < 0 || r > 7 || c < 0 || c > 7)      // board bounds
                        break;

                    Piece p = controller.PieceAt(r, c);
                    if (p == null) continue;

                    if (!skippedFirst)
                    {
                        // Skip the first piece in that direction
                        skippedFirst = true;
                    }
                    else
                    {
                        // This is the second piece; capture if enemy
                        if (p.Team != owner.Team)
                            target = p;
                        break;          // stop scanning this ray
                    }
                }

                // so for now the first found is the one that gets shot
                // but we could also add that all possible targets gets hilighted
                // and the player can choose which one to shoot
                if (target != null) break;   // found a victim -> stop other dirs
            }

            if (target != null)
            {
                controller.CapturePiece(target, owner);   // XP for the rook + hide prefab of the victim
                Debug.Log($"Rook sniped {target.Name} at ({target.Row},{target.Col})");
                usedUltimate = true;
            }
            else
            {
                Debug.Log("No enemy behind a screen – ultimate not consumed.");
            }
        }
    }
}
