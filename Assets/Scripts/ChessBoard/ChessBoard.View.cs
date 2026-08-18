using System.Collections.Generic;
using Pieces;
using Unity.Netcode;
using UnityEngine;

public partial class ChessBoard
{
    /// <summary>
    /// Returns the piece at the given square, or null if empty.
    /// </summary>
    public Piece GetPieceAt(int row, int col)
    {
        foreach (var p in pieces)
        {
            if (p == null || !p.gameObject.activeInHierarchy)
                continue;

            var np = p.GetComponent<NetworkPiece>();
            if (np != null && (np.IsCaptured.Value || np.IsAscended.Value))
                continue;

            if (p.Row == row && p.Col == col)
                return p;
        }

        return null;
    }

    // Flip UI helpers
    private const int BoardSize = 8;

    private bool IsWhitePerspective
    {
        get
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return true;

            // Host is white by design in your game
            if (nm.IsHost) return true;

            // On clients, ask PlayerTeams which side *this* local client owns
            if (PlayerTeams.Instance != null && PlayerTeams.Instance.IsSpawned)
            {
                var myTeam = PlayerTeams.GetTeam(nm.LocalClientId);
                return myTeam == TeamSide.White;
            }

            // If unknown momentarily, default to black so we don't mirror host
            return false;
        }
    }

    /** Convert model coords -> view coords depending on local perspective. */
    private (int row, int col) ModelToView(int row, int col)
    {
        if (IsWhitePerspective) return (row, col);
        int max = BoardSize - 1;
        return (max - row, max - col); // 180° rotate for black
    }

    /** Convert view coords (what you clicked) -> model coords for logic. */
    private (int row, int col) ViewToModel(int row, int col)
    {
        if (IsWhitePerspective) return (row, col);
        int max = BoardSize - 1;
        return (max - row, max - col);
    }

    /// <summary>
    /// Converts board coordinates to world-space position.
    /// </summary>
    public Vector3 GridToWorld(int row, int col)
    {

        var (vr, vc) = ModelToView(row, col);
        Vector3Int cellPos = new Vector3Int(vc, vr, 0);
        return boardTilemap.GetCellCenterWorld(cellPos);

        //Vector3Int cellPos = new Vector3Int(col, row, 0);
        //return boardTilemap.GetCellCenterWorld(cellPos);
    }

    /// <summary>
    /// Removes a captured piece from the active list and hides its GameObject.
    /// </summary>
    private void HideCapturedPiece(Piece piece)
    {
        pieces.Remove(piece);
        //piece.gameObject.SetActive(false);
        // Visuals are handled by NetworkPiece's replicated out-of-play state.
    }

    /// <summary>
    /// Updates a piece;s visual and logic position to the new board coordinates.
    /// </summary>
    private void MovePiece(Piece p, int row, int col)
    {
        p.SetViewPosition(row, col);
    }

    // Event methods are called automatically when the given event raises
    public void ApplyMoveVisuals(MoveResult m)
    {
        if (m.FromRow < 0)
        {
            // spawned by Bishop's ultimate
            var p = m.Piece;

            // 1. Register in the board's lookup grid  (so clicks find it)
            if (!pieces.Contains(p))
                pieces.Add(p);

            // 2. Move the prefab to the correct world position
            p.SetViewPosition(m.ToRow, m.ToCol);
        }
        else
        {
            // normal move
            MovePiece(m.Piece, m.ToRow, m.ToCol);
        }

        // hide the loser if there was a capture
        if (m.Captured != null) HideCapturedPiece(m.Captured);

        UpdateUI();
    }

    void ShowRoads(IReadOnlyList<Vector2Int> white, IReadOnlyList<Vector2Int> black)
    {
        // paint tiles, show highlights for sacred road, sparkly stuff
    }
}
