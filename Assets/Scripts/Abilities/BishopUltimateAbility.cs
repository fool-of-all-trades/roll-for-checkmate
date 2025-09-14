using UnityEngine;
using Pieces;
using Utils;

namespace Abilities
{
    /// <summary>
    /// Bishop's ultimate ability: attempts to resurrect a pawn based on a dice roll.
    /// </summary>
    public class BishopUltimateAbility : Ability
    {
        public BishopUltimateAbility(int levelRequirement) : base(levelRequirement)
        {
        }

        public override void UseAbility(IGameController controller, Piece owner)
        {
            if (usedUltimate)
            {
                Debug.Log("Ultimate already used.");
                return;
            }

            if (owner.StunnedTurns > 0)
            {
                Debug.Log("Bishop is stunned and cannot use abilities.");
                return;
            }

            bool success = false;
            int roll = Dice.Roll(20);

            if (roll <= 3)
            {
                // Low roll: resurrect an opponent pawn
                success = controller.ResurrectPawn(!owner.Team);
                if (success)
                    Debug.Log("Ultimate ability: Wskrzesi³eœ przeciwnika, gratulacje");
                else
                    Debug.Log("Ultimate ability: Coœ posz³o nie tak");
            }
            else if (roll <= 16)
            {
                // Success: resurrect own pawn
                success = controller.ResurrectPawn(owner.Team);
                if (success)
                    Debug.Log("Ultimate ability: Nice, uda³o siê wskrzesiæ");
                else
                    Debug.Log("Ultimate ability: Coœ posz³o nie tak");
            }
            else if (roll <= 19)
            {
                // High roll: bonus resurrection
                success = controller.ResurrectPiece(owner.Team);
                if (success)
                    Debug.Log("Ultimate ability: No way, uda³o siê wskrzesiæ i to jeszcze co");
                else
                    Debug.Log("Ultimate ability: Coœ posz³o nie tak");
            }
            else
            {
                // Nat 20: resurrect a legendary piece (with some crazy ass ability)
                // For now only in plans
                success = controller.ResurrectPiece(owner.Team);
                if (success)
                    Debug.Log("Ultimate ability: No way, uda³o siê wskrzesiæ i to jeszcze co");
                else
                    Debug.Log("Ultimate ability: Coœ posz³o nie tak");
            }

            // Mark the ultimate as used
            if(success)
                usedUltimate = true;
        }
    }
}
