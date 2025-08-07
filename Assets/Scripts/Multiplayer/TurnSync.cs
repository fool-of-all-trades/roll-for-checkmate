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
    public NetworkVariable<bool> WhiteTurn = new NetworkVariable<bool>();

    public static bool IsWhiteTurn => Instance != null && Instance.WhiteTurn.Value;


    void Awake() => Instance = this;      // quick singleton accessor

    public override void OnNetworkSpawn()
    {
        // only the server/host ever drives the turn state
        if (IsServer)
        {
            WhiteTurn.Value = true; // start as White’s turn
        }
    }

    /* Host calls this immediately after toggling its local TurnManager */
    public void CommitTurn(bool isWhiteTurn)
    {
        WhiteTurn.Value = isWhiteTurn;    // replicates to all clients
    }
}
