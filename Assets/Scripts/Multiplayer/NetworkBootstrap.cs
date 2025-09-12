using Unity.Netcode;
using UnityEngine;

public class NetworkBootstrap : MonoBehaviour
{
    [SerializeField] private PlayerTeams playerTeamsPrefab;

    public void TryEnsureCoreNetworkSingletonsSpawned()
    {
        if (!NetworkManager.Singleton || !NetworkManager.Singleton.IsServer) return;

        if (PlayerTeams.Instance)
        {
            var no = PlayerTeams.Instance.GetComponent<NetworkObject>();
            if (!no.IsSpawned)
            {
                no.Spawn(true);         // reuse existing object instead of instantiating a new one
            }
            return;
        }

        // no instance at all → instantiate & spawn
        var go = Instantiate(playerTeamsPrefab);
        var spawned = go.GetComponent<NetworkObject>();
        spawned.Spawn(true);
    }


    // In Start(), subscribe & also handle already-started server:
    //void Start()
    //{
    //    var nm = NetworkManager.Singleton;
    //    if (nm == null) return;

    //    nm.OnServerStarted += TryEnsureCoreNetworkSingletonsSpawned;
    //    if (nm.IsServer) TryEnsureCoreNetworkSingletonsSpawned();
    //}

    private bool _wired;
    void OnEnable()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || _wired) return;
        nm.OnServerStarted += TryEnsureCoreNetworkSingletonsSpawned;
        _wired = true;
    }
    void OnDisable()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !_wired) return;
        nm.OnServerStarted -= TryEnsureCoreNetworkSingletonsSpawned;
        _wired = false;
    }

}
