using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class NetworkSpawner : MonoBehaviour
{
    [SerializeField] private GameObject moveRelayPrefab;

    private readonly Dictionary<ulong, NetworkObject> _relays = new();
    private bool _wired;

    void OnEnable() => Wire();
    void OnDisable() => Unwire();   // also resets per-run state
    void OnDestroy() => Unwire();

    private void Wire()
    {
        if (_wired) return;
        var nm = NetworkManager.Singleton;
        if (!nm) return;

        nm.OnServerStarted += OnServerStarted;
        nm.OnServerStopped += OnServerStopped;
        nm.OnClientConnectedCallback += SpawnForClient;
        nm.OnClientDisconnectCallback += OnClientDisconnected;

        // If your NGO version has it (most recent do), this helps for client-only runs:
        nm.OnClientStopped += OnClientStopped;

        Application.quitting += OnAppQuit;
        _wired = true;
    }

    private void Unwire()
    {
        if (!_wired) return;
        var nm = NetworkManager.Singleton;
        if (nm)
        {
            nm.OnServerStarted -= OnServerStarted;
            nm.OnServerStopped -= OnServerStopped;
            nm.OnClientConnectedCallback -= SpawnForClient;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
            nm.OnClientStopped -= OnClientStopped; // safe even if absent at runtime
        }
        Application.quitting -= OnAppQuit;
        _wired = false;

        // Also safe to clear any per-run flags here if you add them later.
    }

    private void OnServerStarted()
    {
        Debug.Log("[ConnDbg] ServerStarted");
        // No spawning here; SpawnForClient will be called for host's local client.
    }

    private void OnServerStopped(bool wasHost)
    {
        CleanupAllRelays();
    }

    private void OnClientStopped(bool wasHost)
    {
        // Covers client-only runs, and host mode also passes wasHost=true.
        CleanupAllRelays();
    }

    private void OnAppQuit()
    {
        // Extra safety for Editor/standalone quit path
        CleanupAllRelays();
    }

    private void CleanupAllRelays()
    {
        foreach (var kv in _relays)
        {
            var no = kv.Value;
            if (!no) continue;

            if (no.IsSpawned) no.Despawn(true);
            else Destroy(no.gameObject);
        }
        _relays.Clear();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (_relays.TryGetValue(clientId, out var no))
        {
            if (no)
            {
                if (no.IsSpawned) no.Despawn(true);
                else Destroy(no.gameObject);
            }
            _relays.Remove(clientId);
        }
    }

    private void SpawnForClient(ulong clientId)
    {
        var nm = NetworkManager.Singleton;
        if (!nm || !nm.IsServer) return;

        // Idempotency guard: in case callbacks fire twice
        if (_relays.TryGetValue(clientId, out var existing) && existing && existing.IsSpawned)
        {
            Debug.LogWarning($"[Spawner] Relay already exists for client {clientId}, skipping.");
            return;
        }

        Debug.Log($"[Spawner] Spawning moveRelay for new client {clientId}");

        var go = Instantiate(moveRelayPrefab);
        var no = go.GetComponent<NetworkObject>();
        if (!no)
        {
            Debug.LogError("[Spawner] moveRelayPrefab missing NetworkObject!");
            Destroy(go);
            return;
        }

        no.SpawnWithOwnership(clientId);
        _relays[clientId] = no;

        // Optional: server-side board hookup only (clients should resolve their own local relay)
        var relay = go.GetComponent<MoveRelay>();
        var board = FindObjectOfType<ChessBoard>();
        if (board) board.SetMoveRelay(relay);
    }
}
