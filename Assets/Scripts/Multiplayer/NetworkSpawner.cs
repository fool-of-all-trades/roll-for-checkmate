using UnityEngine;
using Unity.Netcode;

public class NetworkSpawner : MonoBehaviour
{
    [SerializeField] private GameObject moveRelayPrefab;

    private void OnEnable()
    {
        // Fires once for client 0 (the host's internal client) and again for client 1 (your remote)
        NetworkManager.Singleton.OnClientConnectedCallback += SpawnForClient;
    }

    private void OnDisable()
    {
        NetworkManager.Singleton.OnClientConnectedCallback -= SpawnForClient;
    }

    private void SpawnForClient(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        Debug.Log($"[Spawner] Spawning moveRelay for new client {clientId}");

        // 1. Spawn the MoveRelay
        var relayGO = Instantiate(moveRelayPrefab);
        var relayNO = relayGO.GetComponent<NetworkObject>();
        relayNO.Spawn(true);

        var relay = relayGO.GetComponent<MoveRelay>();

        var board = FindObjectOfType<ChessBoard>();
        if (board != null)
        {
            board.SetMoveRelay(relay);
        }
        else
        {
            Debug.LogError("[DynamicSpawner] No ChessBoard found to assign MoveRelay!");
        }
    }
}
