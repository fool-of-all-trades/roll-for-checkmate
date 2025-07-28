using System.Collections.Generic;
using Pieces;
using UnityEngine;

public class CurseService : MonoBehaviour, ICurseService
{
    private readonly List<Piece> cursedList = new();

    /// <summary>
    /// Applies a curse to the specified piece for a given number of turns.
    /// Adds the piece to the cursed list if not already present.
    /// </summary>
    public void ApplyCurse(Piece piece, int turns)
    {
        if (piece == null) 
            return;

        piece.SetCursedTurns(turns);

        if (!cursedList.Contains(piece))
            cursedList.Add(piece);

        Debug.Log($"{piece.Name} is cursed for {turns / 2} turns!");
    }

    /// <summary>
    /// Ticks the curse service, decrementing the curse turns for each cursed piece.
    /// </summary>
    public void TickTurn()
    {
        for (int i = cursedList.Count - 1; i >= 0; i--)
        {
            var p = cursedList[i];
            if (p.CursedTurns <= 0)
            {
                cursedList.RemoveAt(i);
            }
            else
            {
                p.DecreaseCursedTurns();
                if (p.CursedTurns <= 0) cursedList.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Returns a roll modifier based on whether the piece is cursed.
    /// </summary>
    public int RollModifier(Piece piece) => IsCursed(piece) ? -3 : 0;

    /// <summary>
    /// Determines if a piece is currently cursed.
    /// </summary>
    public bool IsCursed(Piece piece) => piece != null && piece.CursedTurns > 0;

}
