using Pieces;
using System;
using UnityEngine;

public class DuelService : MonoBehaviour, IDuelService
{
    [SerializeField] private int diceSides = 10;

    // Injected deps
    Func<int, int, Piece> pieceAt;       // board lookup
    Func<bool, Piece> getKing;       // if you need
    ICurseService curses;        // optional, for -3
    int boardMin = 0, boardMax = 7;

    public void Init(Func<int, int, Piece> pieceAt,
                     Func<bool, Piece> getKing,
                     ICurseService curses = null)
    {
        this.pieceAt = pieceAt;
        this.getKing = getKing;
        this.curses = curses;
    }

    public DuelResult ResolveDuel(Piece attacker, Piece defender)
    {
        int raw = UnityEngine.Random.Range(1, diceSides + 1);

        int cursePenalty = curses != null ? curses.RollModifier(attacker) : (attacker.CursedTurns > 0 ? -3 : 0);
        int rookBonus = CalcRookAdjBonus(attacker);
        int knightBonus = CalcKnightRearBonus(attacker, defender);

        int effective = raw + cursePenalty + rookBonus + knightBonus;
        Debug.Log("Raw roll: " + raw);
        Debug.Log("Rook bonus: " + rookBonus);
        Debug.Log("Curse penalty: " + cursePenalty);
        Debug.Log("Knight bonus: " + knightBonus);
        Debug.Log("Effective roll: " + effective);
        int threshold = 5 + defender.Level - attacker.Level;

        bool win = effective > threshold;
        return new DuelResult(win, raw, effective);
    }

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
