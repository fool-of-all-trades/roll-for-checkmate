using UnityEngine;
using Unity.Netcode;

public class ConnectionDebugger : MonoBehaviour
{
    private bool _wired;

    private void OnEnable() { Wire(); }
    private void OnDisable() { Unwire(); }
    private void OnDestroy() { Unwire(); }

    private void Wire()
    {
        if (_wired) return;
        var nm = NetworkManager.Singleton;
        if (!nm) return;

        nm.OnServerStarted += OnServerStarted;
        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;
        _wired = true;
    }

    private void Unwire()
    {
        if (!_wired) return;
        var nm = NetworkManager.Singleton;
        if (!nm) { _wired = false; return; }

        nm.OnServerStarted -= OnServerStarted;
        nm.OnClientConnectedCallback -= OnClientConnected;
        nm.OnClientDisconnectCallback -= OnClientDisconnected;
        _wired = false;
    }

    private void OnServerStarted()
        => Debug.Log("[ConnDbg] ServerStarted");

    private void OnClientConnected(ulong id)
        => Debug.Log($"[{(NetworkManager.Singleton.IsServer ? "Host" : "Client")}] OnClientConnectedCallback → client {id}  (IsServer={NetworkManager.Singleton.IsServer}, IsClient={NetworkManager.Singleton.IsClient}, IsHost={NetworkManager.Singleton.IsHost})");

    private void OnClientDisconnected(ulong id)
        => Debug.Log($"[{(NetworkManager.Singleton.IsServer ? "Host" : "Client")}] OnClientDisconnectCallback → client {id}");

}
