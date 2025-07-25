using Pieces;
using UnityEngine;
using Random = UnityEngine.Random;

public class DuelService : MonoBehaviour, IDuelService
{
    [SerializeField] private int sides = 10;

    public bool ResolveDuel(Piece attacker, Piece defender, out int roll)
    {
        roll = Random.Range(1, sides + 1);
        int threshold = 5 + defender.Level - attacker.Level;
        return roll > threshold;
    }
}
