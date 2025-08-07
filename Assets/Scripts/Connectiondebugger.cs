using UnityEngine;
using Unity.Netcode;

public class ConnectionDebugger : MonoBehaviour
{
    void Start()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null) return;

        nm.OnServerStarted += () =>
            Debug.Log("[ConnDbg] ServerStarted");

        nm.OnClientConnectedCallback += id =>
                Debug.Log($"[{(nm.IsServer ? "Host" : "Client")}] OnClientConnectedCallback → client {id}  (IsServer={nm.IsServer}, IsClient={nm.IsClient}, IsHost={nm.IsHost})");

        nm.OnClientDisconnectCallback += id =>
            Debug.Log($"[{(nm.IsServer ? "Host" : "Client")}] OnClientDisconnectCallback → client {id}");
    }
}
