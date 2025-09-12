using Unity.Netcode;
using UnityEngine;

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
            // Initialize once on server
            WhiteTurn.Value = true; // White starts
        }
    }

    /// <summary>Server-only: call after a successful move to toggle/commit.</summary>
    public void CommitTurn(bool isWhiteTurn)
    {
        if (!IsServer)
        {
            // Silent no-op in builds; warn in editor to catch mistakes
#if UNITY_EDITOR
            Debug.LogWarning("TurnSync.CommitTurn called on a client; ignored.");
#endif
            return;
        }
        WhiteTurn.Value = isWhiteTurn;    // replicates to all clients
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
