using System.Linq;
using Unity.Netcode;
using UnityEngine;

public enum TeamSide { None = 0, White = 1, Black = 2 }

public class PlayerTeams : NetworkBehaviour
{
    public static PlayerTeams Instance;

    // IMPORTANT: 0 is the ServerClientId in NGO, so use ulong.MaxValue as "unset"
    private const ulong Unset = ulong.MaxValue;

    public NetworkVariable<ulong> WhiteClientId = new(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> BlackClientId = new(Unset, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<ulong>.OnValueChangedDelegate _whiteChanged;
    private NetworkVariable<ulong>.OnValueChangedDelegate _blackChanged;

    public static event System.Action TeamsChanged;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject); // persists across scene loads
    }

    public override void OnNetworkSpawn()
    {
        // server seating
        if (IsServer)
        {
            WhiteClientId.Value = NetworkManager.ServerClientId;
            Debug.Log($"[PlayerTeams] White={WhiteClientId.Value}");

            NetworkManager.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        }

        // notify clients when seats change (can also subscribe to it in ChessBoard to show some UI change)
        _whiteChanged = (_, __) => TeamsChanged?.Invoke();
        _blackChanged = (_, __) => TeamsChanged?.Invoke();
        WhiteClientId.OnValueChanged += _whiteChanged;
        BlackClientId.OnValueChanged += _blackChanged;
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            NetworkManager.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        if (_whiteChanged != null) WhiteClientId.OnValueChanged -= _whiteChanged;
        if (_blackChanged != null) BlackClientId.OnValueChanged -= _blackChanged;
    }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    bool IsConnected(ulong id)
    {
        if (id == Unset) return false;
        // ConnectedClientsIds is authoritative on the server
        return NetworkManager.ConnectedClientsIds.Contains(id);
    }

    private void OnClientConnected(ulong clientId)
    {
        if (clientId == NetworkManager.ServerClientId) return; // server already white

        // If Black seat is empty or points to a disconnected/stale id, assign it to the new client
        if (BlackClientId.Value == Unset || !IsConnected(BlackClientId.Value))
        {
            BlackClientId.Value = clientId;
            Debug.Log($"[Server] Assigned BLACK → {clientId}");
        }
        else
        {
            // Optional: kick extra clients (no spectators)
            // NetworkManager.DisconnectClient(clientId);

            // Or allow spectators by doing nothing; TeamSide.None for them
            Debug.Log($"[Server] Extra client {clientId} joined as spectator");
            // I dunno yet, I'll think about it
        }
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (clientId == BlackClientId.Value)
        {
            BlackClientId.Value = Unset;
            Debug.Log("[Server] Black seat freed");
        }
        // If host leaves, the session ends; no host migration here.
    }

    // --- Helpers (read-only on clients too) ---
    public static TeamSide GetTeam(ulong clientId)
    {
        if (!Instance) return TeamSide.None;
        if (Instance.WhiteClientId.Value == clientId) return TeamSide.White;
        if (Instance.BlackClientId.Value == clientId) return TeamSide.Black;
        return TeamSide.None;
    }

    public static bool IsWhite(ulong clientId) => GetTeam(clientId) == TeamSide.White;
    public static bool IsBlack(ulong clientId) => GetTeam(clientId) == TeamSide.Black;

    public static TeamSide MyTeam =>
        Instance ? GetTeam(NetworkManager.Singleton.LocalClientId) : TeamSide.None;
}
