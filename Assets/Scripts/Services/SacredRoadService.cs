using System.Collections.Generic;
using UnityEngine;
using Pieces;
using Unity.Netcode;

/// <summary>
/// Manages the "sacred road" which is Bishop's base ability.
/// A bishop can lay a single-use path of squares after it moves.
/// A road is consumed after one trigger, and a new bishop move replaces the previous road for that team.
/// 
/// Effects:
/// - Enemy stepping on the road: level -1; if level <= 0, the piece switches team and goes to level 1.
/// - Ally stepping on the road: no effect.
/// 
/// This service is logic-only: it raises <see cref="OnRoadsChanged"/> so the view can highlight tiles.
/// </summary>
public class SacredRoadService : MonoBehaviour, ISacredRoadService
{
    readonly List<Vector2Int> whiteRoad = new();
    readonly List<Vector2Int> blackRoad = new();

    Piece whiteOwner, blackOwner;
    int whiteUses, blackUses; // Single-use counters: 0 = inactive, 1 = armed

    public event System.Action<IReadOnlyList<Vector2Int>, IReadOnlyList<Vector2Int>> OnRoadsChanged;

    /// <summary>
    /// Activates (or replaces) the road for the owner's team with the given path.
    /// Called right after a bishop finishes a legal move.
    /// </summary>
    /// <param name="owner">The Bishop that created the road.</param>
    /// <param name="path">Squares that form the road, as (row, col). Excludes the bishop's start/end squares.</param>
    public void ActivateRoad(Piece owner, List<Vector2Int> path)
    {
        if (owner.Team)
        {
            whiteRoad.Clear(); 
            whiteRoad.AddRange(path);
            whiteOwner = owner; 
            whiteUses = 1;
        }
        else
        {
            blackRoad.Clear(); 
            blackRoad.AddRange(path);
            blackOwner = owner; 
            blackUses = 1;
        }

        // Notify the view to refresh highlights
        OnRoadsChanged?.Invoke(whiteRoad, blackRoad);
    }

    /// <summary>
    /// Called once after every legal move. Checks if the piece ended on any active road square.
    /// If so, applies the effect and clears the road that was triggered.
    /// </summary>
    public void ProcessMove(Piece possibleTrespasser)
    {
        // No roads active?
        if (whiteUses == 0 && blackUses == 0) return;

        // White road check
        if (whiteUses > 0 && possibleTrespasser != whiteOwner && IsOnRoad(whiteRoad, possibleTrespasser))
        {
            ApplyEffect(possibleTrespasser, roadTeam: true);
            ClearWhite();
            return;
        }

        // Black road check
        if (blackUses > 0 && possibleTrespasser != blackOwner && IsOnRoad(blackRoad, possibleTrespasser))
        {
            ApplyEffect(possibleTrespasser, roadTeam: false);
            ClearBlack();
        }
    }

    /// <summary>
    /// Returns the current road squares for both teams as read-only lists.
    /// </summary>
    /// <returns>A tuple (white, black) of read-only road square lists.</returns>
    public (IReadOnlyList<Vector2Int> white, IReadOnlyList<Vector2Int> black) GetRoads()
        => (whiteRoad, blackRoad);

    /// <summary>
    /// Tests whether piece currently stands on any square of a road.
    /// </summary>
    bool IsOnRoad(List<Vector2Int> road, Piece p)
    {
        for (int i = 0; i < road.Count; i++)
            if (road[i].x == p.Row && road[i].y == p.Col) return true;
        return false;
    }

    /// <summary>
    /// Applies the enemy-road penalty if the trespasser belongs to the opposite team.
    /// </summary>
    void ApplyEffect(Piece trespasser, bool roadTeam)
    {
        // Only the host/server should apply gameplay effects
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        // we only punish the enemies
        if (trespasser.Team != roadTeam)
        {
            trespasser.UpdateLevel(-1);
            Debug.Log($"{trespasser.Name} crossed enemy sacred road, new level {trespasser.Level}");

            // push replicated level
            var np = trespasser.GetComponent<NetworkPiece>();
            if (np) np.Level.Value = trespasser.Level;

            // if the trespasser has no level left, switch teams and reset level to 1
            if (trespasser.Level <= 0)
            {
                bool oldTeam = trespasser.Team;
                trespasser.ChangeTeam(!oldTeam);
                trespasser.UpdateLevel(1);

                // push replicated team + level
                if (np)
                {
                    np.Team.Value = trespasser.Team;   // bool: true=White, false=Black
                    np.Level.Value = trespasser.Level;  // ensure clients see 1
                }

                Debug.Log($"{trespasser.Name} switches to {(trespasser.Team ? "White" : "Black")} at level {trespasser.Level}");
            }
        }
    }

    /// <summary>Clears white road and notifies listeners.</summary>
    void ClearWhite()
    {
        whiteUses = 0; whiteRoad.Clear(); whiteOwner = null;
        OnRoadsChanged?.Invoke(whiteRoad, blackRoad);
    }

    /// <summary>Clears black road and notifies listeners.</summary>
    void ClearBlack()
    {
        blackUses = 0; blackRoad.Clear(); blackOwner = null;
        OnRoadsChanged?.Invoke(whiteRoad, blackRoad);
    }
}
