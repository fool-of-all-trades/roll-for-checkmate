using System;
using Unity.Netcode;
using UnityEngine;
using Pieces;

public struct EnPassantNetworkState : INetworkSerializable, IEquatable<EnPassantNetworkState>
{
    public EnPassantNetworkState(int row, int col, ulong pawnNetworkObjectId)
    {
        Active = true;
        Row = row;
        Col = col;
        PawnNetworkObjectId = pawnNetworkObjectId;
    }

    public bool Active;
    public int Row;
    public int Col;
    public ulong PawnNetworkObjectId;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Active);
        serializer.SerializeValue(ref Row);
        serializer.SerializeValue(ref Col);
        serializer.SerializeValue(ref PawnNetworkObjectId);
    }

    public bool Equals(EnPassantNetworkState other)
    {
        return Active == other.Active &&
               Row == other.Row &&
               Col == other.Col &&
               PawnNetworkObjectId == other.PawnNetworkObjectId;
    }
}

/// <summary>
/// Single source of truth for whose turn it is.
/// Host (server) writes; every client reads.
/// Place this in a scene as a NetworkObject with "Spawn With Scene" checked,
/// or spawn it once like PlayerTeams.
/// </summary>
[DefaultExecutionOrder(-90)]
public class TurnSync : NetworkBehaviour
{
    public static TurnSync Instance { get; private set; }

    // Explicit perms: Everyone can read; only Server can write.
    [HideInInspector]
    public NetworkVariable<bool> WhiteTurn =
        new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [HideInInspector]
    public NetworkVariable<EnPassantNetworkState> EnPassant =
        new(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public static bool IsWhiteTurn => Instance && Instance.WhiteTurn.Value;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject); // optional; helps across scene loads
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            WhiteTurn.Value = true; // Initialize once on server
            EnPassant.Value = default;
        }
    }

    /// <summary>Server-only: call after a successful move to toggle/commit.</summary>
    public void CommitTurn(bool isWhiteTurn)
    {
        if (IsServer) WhiteTurn.Value = isWhiteTurn;    // replicates to all clients
    }

    public void CommitEnPassant(EnPassantNetworkState state)
    {
        if (IsServer) EnPassant.Value = state;
    }

    public bool TryResolveEnPassantPawn(out Pawn pawn)
    {
        pawn = null;

        var state = EnPassant.Value;
        var networkManager = NetworkManager.Singleton;
        if (!state.Active || networkManager == null) return false;

        if (!networkManager.SpawnManager.SpawnedObjects.TryGetValue(
                state.PawnNetworkObjectId,
                out var networkObject) ||
            !networkObject.IsSpawned)
            return false;

        var resolvedPawn = networkObject.GetComponent<Pawn>();
        if (resolvedPawn == null || !resolvedPawn.gameObject.activeInHierarchy)
            return false;

        var networkPiece = resolvedPawn.GetComponent<NetworkPiece>();
        if (networkPiece != null &&
            (networkPiece.IsCaptured.Value || networkPiece.IsAscended.Value))
            return false;

        pawn = resolvedPawn;
        return true;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
