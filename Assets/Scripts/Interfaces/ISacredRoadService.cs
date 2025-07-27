using System.Collections.Generic;
using Pieces;
using UnityEngine;

public interface ISacredRoadService
{
    void ActivateRoad(Piece owner, List<Vector2Int> path);   // 1 use
    void ProcessMove(Piece mover);                           // call after each legal move
    (IReadOnlyList<Vector2Int> white, IReadOnlyList<Vector2Int> black) GetRoads();

    /// <summary>
    /// Fired whenever the roads change (activation or clearing).
    /// The args are the current white and black road square lists.
    /// Subscribed in the view layer to update tile highlights.
    /// </summary>
    event System.Action<IReadOnlyList<Vector2Int>, IReadOnlyList<Vector2Int>> OnRoadsChanged;
}
