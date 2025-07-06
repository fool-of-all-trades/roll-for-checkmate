using Pieces;
using Controller;

//King (level 3, zbicie daje zwyciêstwo):
//Bazowa umiejêtnoœæ: Jednorazowa ale bez potrzeby levelowania
//
//Queen (level 3, zbicie daje +3 level):
//Bazowa umiejêtnoœæ: jednorazowa, nie wymaga levelowania, automatycznie dzia³a po œmierci
//
//Bishop (level 2, zbicie daje +2 level):
//Bazowa umiejêtnoœæ: passive, triggered when conditions are met, no cooldown
//Ultimate: raz na levelu 5
//
//Knight (level 2, zbicie daje +2 level):
//Bazowa umiejêtnoœæ: passive, triggered when conditions are met, no cooldown
//Ultimate: raz na levelu 5
//
//Rook (level 2, zbicie daje +2 level):
//Bazowa umiejêtnoœæ: passive, triggered when conditions are met, no cooldown
//Ultimate: raz na levelu 5
//
//Piece (level 1, zbicie daje +1 level): nothing for now

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

        /// <summary>
        /// Executes the ability logic.
        /// </summary>
        /// <param name="controller">Reference to the GameController managing the game.</param>
        /// <param name="owner">The piece invoking the ability.</param>
        public abstract void UseAbility(GameController controller, Piece owner);
    }
}
