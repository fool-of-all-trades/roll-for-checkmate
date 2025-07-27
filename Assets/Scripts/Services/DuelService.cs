using Pieces;
using System;
using UnityEngine;

public class DuelService : MonoBehaviour, IDuelService
{
    [SerializeField] private int diceSides = 10;

    Func<int, int, Piece> pieceAt;
    ICurseService curses;
    int boardMin = 0, boardMax = 7;

    public void Init(Func<int, int, Piece> pieceAt,
                     ICurseService curses = null)
    {
        this.pieceAt = pieceAt;
        this.curses = curses;
    }

    /// <summary>
    /// Resolves a duel between two pieces with curses and bonuses applied.
    /// </summary>
    /// <returns>bool if the attacker won, the raw roll, the roll with all bonuses applied</returns>
    public DuelResult ResolveDuel(Piece attacker, Piece defender)
    {
        int rawRoll = UnityEngine.Random.Range(1, diceSides + 1);

        int cursePenalty = curses != null ? curses.RollModifier(attacker) : (attacker.CursedTurns > 0 ? -3 : 0);
        int rookBonus = CalcRookAdjBonus(attacker);
        int knightBonus = CalcKnightRearBonus(attacker, defender);

        int effective = rawRoll + cursePenalty + rookBonus + knightBonus;

        Debug.Log("Raw roll: " + rawRoll);
        Debug.Log("Rook bonus: " + rookBonus);
        Debug.Log("Curse penalty: " + cursePenalty);
        Debug.Log("Knight bonus: " + knightBonus);
        Debug.Log("Effective roll: " + effective);

        int threshold = 5 + defender.Level - attacker.Level;

        bool win = effective > threshold;
        return new DuelResult(win, rawRoll, effective);
    }

    /// <summary>
    /// Calculates the bonus for an adjacent Rook piece.
    /// </summary>
    /// <returns>2 if the bonus is applicable, 0 if the bonus is not applicable</returns>
    int CalcRookAdjBonus(Piece attacker)
    {
        var dirs = new (int dr, int dc)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        foreach (var (dr, dc) in dirs)
        {
            int r = attacker.Row + dr, c = attacker.Col + dc;

            if (r < boardMin || r > boardMax || c < boardMin || c > boardMax) continue;
            
            var neighbour = pieceAt(r, c);

            if (neighbour is Rook && neighbour.Team == attacker.Team) {
                {
                    Debug.Log("Rook empowers ally! +2 to roll.");
                    return 2;
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// Calculates the bonus for a Knight attacking from the rear.
    /// </summary>
    /// <returns>1 if the bonus is applicable, 0 if the bonus is not applicable</returns>
    int CalcKnightRearBonus(Piece attacker, Piece defender)
    {
        if (attacker is not Knight || defender == null) return 0;
        
        bool defenderWhite = defender.Team;
        bool rear = defenderWhite
            ? attacker.Row < defender.Row
            : attacker.Row > defender.Row;
        
        return rear ? 1 : 0;
    }
}
