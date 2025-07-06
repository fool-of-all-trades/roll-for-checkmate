using UnityEngine;

namespace Utils
{
    /// <summary>
    /// Provides a simple dice roll utility.
    /// </summary>
    public static class Dice
    {
        /// <summary>
        /// Rolls a dice with the specified number of sides (1 through sides).
        /// </summary>
        /// <param name="sides">Number of faces on the dice.</param>
        /// <returns>Random integer between 1 and sides, inclusive.</returns>
        public static int Roll(int sides)
        {
            // UnityEngine.Random.Range(min, max) returns [min, max) for ints, so we use sides + 1.
            return Random.Range(1, sides + 1);
        }
    }
}
