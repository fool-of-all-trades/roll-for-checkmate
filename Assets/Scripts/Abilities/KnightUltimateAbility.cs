using Pieces;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Abilities
{
    /// <summary>
    /// Knight's ultimate ability: Divine Smite
    /// One enemy piece can be striked, and it's level turns to 1,
    /// and it can't move for one turn
    /// </summary>
    public class KnightUltimateAbility : Ability
    {
        public KnightUltimateAbility(int levelRequirement) : base(levelRequirement) { }

        public override void UseAbility(IGameController controller, Piece owner)
        {
            if (usedUltimate)
            {
                Debug.Log("Ultimate already used.");
                return;
            }

            int ownerRow = owner.Row;
            int ownerCol = owner.Col;

            Piece target = null;
        }
    }
}
