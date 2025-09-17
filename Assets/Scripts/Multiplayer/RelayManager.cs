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
        await UgsBootstrap.Init();

        Allocation alloc = await RelayService.Instance.CreateAllocationAsync(maxPlayers, region);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(alloc.AllocationId);

        var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
        var relayData = new RelayServerData(alloc, "dtls");
        utp.SetRelayServerData(relayData);

        NetworkManager.Singleton.StartHost();
        Debug.Log($"[Relay] Host started. JoinCode={joinCode}");
        return joinCode;
    }

    public static async Task JoinClientWithRelayAsync(string joinCode)
    {
        await UgsBootstrap.Init();

        JoinAllocation join = await RelayService.Instance.JoinAllocationAsync(joinCode);

        var utp = NetworkManager.Singleton.GetComponent<UnityTransport>();
        var relayData = new RelayServerData(join, "dtls");
        utp.SetRelayServerData(relayData);

        NetworkManager.Singleton.StartClient();
        Debug.Log("[Relay] Client started via Relay.");
    }
}
