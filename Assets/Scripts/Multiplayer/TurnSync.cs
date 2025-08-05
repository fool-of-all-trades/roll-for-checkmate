using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Single source of truth for whose turn it is.
/// Host (server) writes; every client reads.
/// </summary>
[DefaultExecutionOrder(-90)]          // spawn early, before board/UI scripts use it
public class TurnSync : NetworkBehaviour
{
    public static TurnSync Instance { get; private set; }

    /// <summary>true = white to move, false = black to move</summary>
    public NetworkVariable<bool> WhiteTurn =
        new NetworkVariable<bool>(true,
                                  NetworkVariableReadPermission.Everyone,
                                  NetworkVariableWritePermission.Server);

    void Awake() => Instance = this;      // quick singleton accessor

    /* Host calls this immediately after toggling its local TurnManager */
    public void CommitTurn(bool whiteToMove)
    {
        WhiteTurn.Value = whiteToMove;    // replicates to all clients
    }

    public static bool IsWhiteTurn => Instance != null && Instance.WhiteTurn.Value;
}
