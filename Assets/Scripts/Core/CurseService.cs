using System.Collections.Generic;
using Pieces;
using UnityEngine;

public class CurseService : MonoBehaviour, ICurseService
{
    private readonly List<Piece> cursed = new();

    public void ApplyCurse(Piece piece, int turns)
    {
        if (piece == null) 
            return;

        piece.SetCursedTurns(turns);

        if (!cursed.Contains(piece)) 
            cursed.Add(piece);

        Debug.Log($"{piece.Name} is cursed for {turns / 2} turns!");
    }

    public void TickTurn()
    {
        for (int i = cursed.Count - 1; i >= 0; i--)
        {
            var p = cursed[i];
            if (p.CursedTurns <= 0)
            {
                cursed.RemoveAt(i);
            }
            else
            {
                p.DecreaseCursedTurns();
                if (p.CursedTurns <= 0) cursed.RemoveAt(i);
            }
        }
    }

    public int RollModifier(Piece piece) => IsCursed(piece) ? -3 : 0;

    public bool IsCursed(Piece piece) => piece != null && piece.CursedTurns > 0;
}
