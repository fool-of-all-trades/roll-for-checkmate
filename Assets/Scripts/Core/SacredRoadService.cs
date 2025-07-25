using System.Collections.Generic;
using UnityEngine;
using Pieces;

public class SacredRoadService : MonoBehaviour, ISacredRoadService
{
    readonly List<Vector2Int> whiteRoad = new();
    readonly List<Vector2Int> blackRoad = new();
    Piece whiteOwner, blackOwner;
    int whiteUses, blackUses;

    public event System.Action<IReadOnlyList<Vector2Int>, IReadOnlyList<Vector2Int>> OnRoadsChanged;

    public void ActivateRoad(Piece owner, List<Vector2Int> path)
    {
        if (owner.Team)
        {
            whiteRoad.Clear(); whiteRoad.AddRange(path);
            whiteOwner = owner; whiteUses = 1;
        }
        else
        {
            blackRoad.Clear(); blackRoad.AddRange(path);
            blackOwner = owner; blackUses = 1;
        }
        OnRoadsChanged?.Invoke(whiteRoad, blackRoad);
    }

    public void ProcessMove(Piece mover)
    {
        // No roads active?
        if (whiteUses == 0 && blackUses == 0) return;

        // White road check
        if (whiteUses > 0 && mover != whiteOwner && IsOnRoad(whiteRoad, mover))
        {
            ApplyEffect(mover, true);
            ClearWhite();
            return;
        }

        // Black road check
        if (blackUses > 0 && mover != blackOwner && IsOnRoad(blackRoad, mover))
        {
            ApplyEffect(mover, false);
            ClearBlack();
        }
    }

    public (IReadOnlyList<Vector2Int> white, IReadOnlyList<Vector2Int> black) GetRoads()
        => (whiteRoad, blackRoad);

    bool IsOnRoad(List<Vector2Int> road, Piece p)
    {
        for (int i = 0; i < road.Count; i++)
            if (road[i].x == p.Row && road[i].y == p.Col) return true;
        return false;
    }

    void ApplyEffect(Piece p, bool roadTeam)
    {
        if (p.Team != roadTeam)
        {
            p.UpdateLevel(-1);
            Debug.Log($"{p.Name} crossed enemy sacred road, new level {p.Level}");
            if (p.Level <= 0)
            {
                bool oldTeam = p.Team;
                p.ChangeTeam(!oldTeam);
                p.UpdateLevel(1);
                Debug.Log($"{p.Name} switches to {(p.Team ? "White" : "Black")} at level {p.Level}");
            }
        }
    }

    void ClearWhite()
    {
        whiteUses = 0; whiteRoad.Clear(); whiteOwner = null;
        OnRoadsChanged?.Invoke(whiteRoad, blackRoad);
    }

    void ClearBlack()
    {
        blackUses = 0; blackRoad.Clear(); blackOwner = null;
        OnRoadsChanged?.Invoke(whiteRoad, blackRoad);
    }
}
