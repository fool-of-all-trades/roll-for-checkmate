using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class NetcodeUI : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button startHostBtn;
    [SerializeField] private Button startClientBtn;
    [SerializeField] private Button stopHostBtn;
    [SerializeField] private Button stopClientBtn;

    [Header("Optional")]
    [SerializeField] private TMP_InputField addressField;
    [SerializeField] private ushort port = 7777;

    private NetworkManager nm;

    private bool subscribed;

    // cached delegates so we can safely unsubscribe
    private Action onServerStartedH;

    #if NGO_1_5_OR_NEWER
        private Action onServerStoppedH;
    #endif

    private Action<ulong> onClientConnectedH;
    private Action<ulong> onClientDisconnectedH;

    private bool lastIsServer, lastIsClient;

    void Awake()
    {
        nm = NetworkManager.Singleton;

        startHostBtn.onClick.AddListener(StartHost);
        startClientBtn.onClick.AddListener(StartClient);
        stopHostBtn.onClick.AddListener(StopHost);
        stopClientBtn.onClick.AddListener(StopClient);

        // cache handlers
        onServerStartedH = () => SafeRefreshUI();
        onClientConnectedH = _ => SafeRefreshUI();
        onClientDisconnectedH = _ => SafeRefreshUI();

        #if NGO_1_5_OR_NEWER
            onServerStoppedH = () => SafeRefreshUI();
        #endif

        //SafeRefreshUI(true);
    }

    void OnEnable() { TrySubscribe(); }
    void OnDisable() { Unsubscribe(); }
    void OnDestroy() { Unsubscribe(); }

    void Update()
    {
        if (nm == null) return;
        if (nm.IsServer != lastIsServer || nm.IsClient != lastIsClient)
            SafeRefreshUI();
    }

    private void ConfigureTransportAddress()
    {
        if (addressField == null || string.IsNullOrWhiteSpace(addressField.text)) return;
        var utp = nm.NetworkConfig.NetworkTransport as UnityTransport;
        if (utp != null) utp.SetConnectionData(addressField.text.Trim(), port);
    }

    public void StartHost()
    {
        if (nm.IsServer || nm.IsClient) return;
        ConfigureTransportAddress();
        nm.StartHost();
        SafeRefreshUI();
    }

    public void StartClient()
    {
        if (nm.IsServer || nm.IsClient) return;
        ConfigureTransportAddress();
        nm.StartClient();
        SafeRefreshUI();
    }

    public void StopHost()
    {
        if (!nm.IsHost) return;
        nm.Shutdown();
        SafeRefreshUI(true);
    }

    public void StopClient()
    {
        if (!nm.IsClient || nm.IsServer) return;
        nm.Shutdown();
        SafeRefreshUI(true);
    }

    // Only call RefreshUI if this component and its buttons are still alive
    private void TrySubscribe()
    {
        if (nm == null) nm = NetworkManager.Singleton;
        if (nm == null || subscribed) return;

        nm.OnServerStarted += onServerStartedH;
        nm.OnClientConnectedCallback += onClientConnectedH;
        nm.OnClientDisconnectCallback += onClientDisconnectedH;
#if NGO_1_5_OR_NEWER
        nm.OnServerStopped            += onServerStoppedH;
#endif
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (nm == null || !subscribed) return;
        nm.OnServerStarted -= onServerStartedH;
        nm.OnClientConnectedCallback -= onClientConnectedH;
        nm.OnClientDisconnectCallback -= onClientDisconnectedH;
#if NGO_1_5_OR_NEWER
        nm.OnServerStopped            -= onServerStoppedH;
#endif
        subscribed = false;
    }

    private void SafeRefreshUI(bool force = false)
    {
        if (this == null) return;
        if (nm == null) nm = NetworkManager.Singleton;
        if (nm == null) return; // NetworkManager not ready yet

        if (!startHostBtn || !startClientBtn || !stopHostBtn || !stopClientBtn) return;
        RefreshUI(force);
    }

    private void RefreshUI(bool force = false)
    {
        bool isHost = nm.IsHost;
        bool isServer = nm.IsServer;
        bool isClient = nm.IsClient && !nm.IsServer;

        if (!force && lastIsServer == nm.IsServer && lastIsClient == nm.IsClient)
            return;

        startHostBtn.interactable = !nm.IsServer && !nm.IsClient;
        startClientBtn.interactable = !nm.IsServer && !nm.IsClient;
        stopHostBtn.interactable = isHost;
        stopClientBtn.interactable = isClient;

        if (addressField) addressField.interactable = !nm.IsServer && !nm.IsClient;

        lastIsServer = nm.IsServer;
        lastIsClient = nm.IsClient;
    }
}
