//using Unity.Netcode;
//using System.Collections.Generic;
//using UnityEngine;

///// <summary>
///// Keeps track of which clientId is white (host) / black (client)
///// The host owns this object.
///// </summary>
//public class PlayerTeams : NetworkBehaviour
//{
//    // 0 = white, 1 = black
//    public NetworkVariable<ulong> WhiteClientId = new(
//         0,
//         NetworkVariableReadPermission.Everyone,
//         NetworkVariableWritePermission.Server);


//    //void Awake()
//    //{
//    //    // Subscribe to the connection event *once* per process
//    //    NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
//    //}

//    void Start()
//    {
//        var nm = NetworkManager.Singleton;
//        if (nm != null)
//            nm.OnClientConnectedCallback += OnClientConnected;
//        else
//            Debug.LogError("PlayerTeams couldn't find NetworkManager.Singleton");
//    }

//    void OnDestroy()
//    {
//        if (NetworkManager.Singleton != null)
//            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
//    }

//    public override void OnNetworkSpawn()
//    {
//        if (IsServer)
//        {
//            // Host is always white
//            WhiteClientId.Value = NetworkManager.Singleton.LocalClientId; // = 0
//        }
//    }

//    /* ───────────────────────────  EVENT HANDLERS  ────────────────────────── */

//    // Fired on host whenever any client (including host) finishes connection
//    private void OnClientConnected(ulong clientId)
//    {
//        if (!IsServer) return;      // safety: only the host runs this

//        if (clientId == WhiteClientId.Value)
//            Debug.Log("Host connected as White");
//        else
//            Debug.Log($"Client {clientId} connected as Black");
//    }

//    /* ───────────────────────────  PUBLIC HELPERS  ────────────────────────── */

//    /// <summary>True if the *local* peer is the white player.</summary>
//    public static bool AmIWhite
//    {
//        get
//        {
//            var nm = NetworkManager.Singleton;
//            var teams = FindObjectOfType<PlayerTeams>();
//            if (nm == null || teams == null) return false;        // not in play mode
//            return nm.LocalClientId == teams.WhiteClientId.Value;
//        }
//    }
//}
