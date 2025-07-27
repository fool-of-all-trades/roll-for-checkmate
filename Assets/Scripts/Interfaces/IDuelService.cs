using Pieces;
using System;
public interface IDuelService
{

    public void Init(Func<int, int, Piece> pieceAt,
                     ICurseService curses = null);
    DuelResult ResolveDuel(Piece attacker, Piece defender);
}

/// <summary>
/// Represents the result of a duel between two pieces.
/// </summary>
public readonly struct DuelResult
{
    public readonly bool AttackerWon;
    public readonly int RawRoll;
    public readonly int EffectiveRoll;
    public DuelResult(bool won, int raw, int eff)
    {
        AttackerWon = won; RawRoll = raw; EffectiveRoll = eff;
    }
}