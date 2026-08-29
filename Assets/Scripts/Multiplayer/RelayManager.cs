using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport.Relay;
using UnityEngine;

public static class RelayManager
{
    public static async Task<string> StartHostWithRelayAsync(int maxPlayers = 2, string region = null)
    {
        var networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.IsServer || networkManager.IsClient || networkManager.IsListening)
        {
            Debug.LogWarning("[Relay] Cannot start host; NetworkManager is missing or already running.");
            throw new System.InvalidOperationException("NetworkManager is missing or already running.");
        }

        await UgsBootstrap.Init();

        Allocation alloc = await RelayService.Instance.CreateAllocationAsync(maxPlayers, region);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

        var utp = networkManager.GetComponent<UnityTransport>();
        var relayData = AllocationUtils.ToRelayServerData(alloc, "dtls");
        utp.SetRelayServerData(relayData);

        bool started = networkManager.StartHost();
        if (!started)
        {
            Debug.LogWarning("[Relay] StartHost returned false.");
            throw new System.InvalidOperationException("Failed to start host via Relay.");
        }

        Debug.Log($"[Relay] Host started. JoinCode={joinCode}");
        return joinCode;
    }

    public static async Task JoinClientWithRelayAsync(string joinCode)
    {
        var networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.IsServer || networkManager.IsClient || networkManager.IsListening)
        {
            Debug.LogWarning("[Relay] Cannot start client; NetworkManager is missing or already running.");
            throw new System.InvalidOperationException("NetworkManager is missing or already running.");
        }

        await UgsBootstrap.Init();

        JoinAllocation join = await RelayService.Instance.JoinAllocationAsync(joinCode);

        var utp = networkManager.GetComponent<UnityTransport>();
        var relayData = AllocationUtils.ToRelayServerData(join, "dtls");
        utp.SetRelayServerData(relayData);

        bool started = networkManager.StartClient();
        if (!started)
        {
            Debug.LogWarning("[Relay] StartClient returned false.");
            throw new System.InvalidOperationException("Failed to start client via Relay.");
        }

        Debug.Log("[Relay] Client started via Relay.");
    }
}
