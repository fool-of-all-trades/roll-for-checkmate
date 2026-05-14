using Pieces;
using Controller;

//King (level 5):
//Base Ability: one time, no leveling up required
//
//Queen (level 3, capture gives +3 level):
//Base Ability: one time, no leveling up required, works automatically after death
//
//Bishop (level 2, capture gives +2 level):
//Base Ability: passive, triggered when conditions are met, no cooldown
//Ultimate: once on level 5
//
//Knight (level 2, capture gives +2 level):
//Base Ability: passive, triggered when conditions are met, no cooldown
//Ultimate: once on level 5
//
//Rook (level 2, capture gives +2 level):
//Base Ability: passive, triggered when conditions are met, no cooldown
//Ultimate: once on level 5
//
//Piece (level 1, capture gives +1 level): nothing for now

namespace Abilities
{
    /// <summary>
    /// Base class for all piece abilities.
    /// </summary>
    public abstract class Ability
    {
        protected bool usedUltimate;
        protected int levelRequirement;

        protected Ability(int levelRequirement)
        {
            this.levelRequirement = levelRequirement;
            this.usedUltimate = false;
        }

        /// <summary>
        /// Determines if the ultimate ability can be used by a piece of the given level.
        /// </summary>
        /// <param name="pieceLevel">Current level of the piece.</param>
        /// <returns>True if ultimate not used and piece level >= requirement.</returns>
        public bool CanUseUltimate(int pieceLevel)
        {
            return !usedUltimate && pieceLevel >= levelRequirement;
        }

        public bool HasUsedUltimate => usedUltimate;

        public void SetUltimateUsed(bool used)
        {
            usedUltimate = used;
        }

        /// <summary>
        /// Executes the ability logic.
        /// </summary>
        /// <param name="controller">Reference to the GameController managing the game.</param>
        /// <param name="owner">The piece invoking the ability.</param>
        public abstract void UseAbility(IGameController controller, Piece owner);
    }
}
