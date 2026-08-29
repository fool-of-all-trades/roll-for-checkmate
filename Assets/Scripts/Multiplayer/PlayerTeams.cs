using System.Linq;
using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public enum TeamSide { None = 0, White = 1, Black = 2 }

public class PlayerTeams : NetworkBehaviour
{
    public static PlayerTeams Instance;

    // IMPORTANT: 0 is the ServerClientId in NGO, so use ulong.MaxValue as "unset"
    private const ulong Unset = ulong.MaxValue;

    public NetworkVariable<FixedString128Bytes> WhitePlayerId = new(
        new FixedString128Bytes(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    public NetworkVariable<FixedString128Bytes> BlackPlayerId = new(
        new FixedString128Bytes(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<ulong> WhiteClientId = new(Unset, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<ulong> BlackClientId = new(Unset, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private NetworkVariable<FixedString128Bytes>.OnValueChangedDelegate _whitePlayerChanged;
    private NetworkVariable<FixedString128Bytes>.OnValueChangedDelegate _blackPlayerChanged;
    private NetworkVariable<ulong>.OnValueChangedDelegate _whiteChanged;
    private NetworkVariable<ulong>.OnValueChangedDelegate _blackChanged;
    private bool _serverCallbacksRegistered;
    private bool _identityCallbacksRegistered;
    private IMultiplayerSessionService _sessionService;
    private string _seatSessionId = string.Empty;

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
            _sessionService = UnityMpsSessionService.Instance;
            PrepareSeatStateForCurrentSession();
            RegisterIdentityCallbacks();
            RegisterServerCallbacks();

            if (!TrySetSessionSeatPlayerIds())
                WhiteClientId.Value = NetworkManager.ServerClientId;

            BindKnownValidatedClients();
            Debug.Log($"[PlayerTeams] White player={WhitePlayerId.Value}, client={WhiteClientId.Value}");
        }

        // notify clients when seats change (can also subscribe to it in ChessBoard to show some UI change)
        _whitePlayerChanged = (_, __) => TeamsChanged?.Invoke();
        _blackPlayerChanged = (_, __) => TeamsChanged?.Invoke();
        _whiteChanged = (_, __) => TeamsChanged?.Invoke();
        _blackChanged = (_, __) => TeamsChanged?.Invoke();
        WhitePlayerId.OnValueChanged += _whitePlayerChanged;
        BlackPlayerId.OnValueChanged += _blackPlayerChanged;
        WhiteClientId.OnValueChanged += _whiteChanged;
        BlackClientId.OnValueChanged += _blackChanged;
    }

    public override void OnNetworkDespawn()
    {
        UnregisterIdentityCallbacks();
        UnregisterServerCallbacks();

        if (_whitePlayerChanged != null) WhitePlayerId.OnValueChanged -= _whitePlayerChanged;
        if (_blackPlayerChanged != null) BlackPlayerId.OnValueChanged -= _blackPlayerChanged;
        if (_whiteChanged != null) WhiteClientId.OnValueChanged -= _whiteChanged;
        if (_blackChanged != null) BlackClientId.OnValueChanged -= _blackChanged;
    }
    void OnDestroy()
    {
        UnregisterIdentityCallbacks();
        UnregisterServerCallbacks();
        if (Instance == this) Instance = null;
    }

    private void RegisterIdentityCallbacks()
    {
        if (_identityCallbacksRegistered || _sessionService == null) return;

        _sessionService.ClientIdentityValidated += OnClientIdentityValidated;
        _identityCallbacksRegistered = true;
    }

    private void UnregisterIdentityCallbacks()
    {
        if (!_identityCallbacksRegistered || _sessionService == null) return;

        _sessionService.ClientIdentityValidated -= OnClientIdentityValidated;
        _identityCallbacksRegistered = false;
    }

    private void RegisterServerCallbacks()
    {
        if (_serverCallbacksRegistered) return;

        var nm = NetworkManager;
        if (nm == null) return;

        nm.OnClientConnectedCallback += OnClientConnected;
        nm.OnClientDisconnectCallback += OnClientDisconnected;
        _serverCallbacksRegistered = true;
    }

    private void UnregisterServerCallbacks()
    {
        if (!_serverCallbacksRegistered) return;

        var nm = NetworkManager != null ? NetworkManager : NetworkManager.Singleton;
        if (nm != null)
        {
            nm.OnClientConnectedCallback -= OnClientConnected;
            nm.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        _serverCallbacksRegistered = false;
    }

    bool IsConnected(ulong id)
    {
        if (id == Unset) return false;
        // ConnectedClientsIds is authoritative on the server
        return NetworkManager.ConnectedClientsIds.Contains(id);
    }

    private void OnClientConnected(ulong clientId)
    {
        if (TryBindValidatedClient(clientId)) return;

        if (clientId == NetworkManager.ServerClientId)
        {
            if (!HasPersistentSeatPlayerIds)
                WhiteClientId.Value = clientId;
            return;
        }

        if (HasPersistentSeatPlayerIds)
        {
            Debug.LogWarning($"[Server] Client {clientId} connected without a validated seat identity.");
            return;
        }

        // Compatibility fallback for the old non-Sessions development path.
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
        var nm = NetworkManager;
        if (!IsServer ||
            !IsSpawned ||
            nm == null ||
            nm.ShutdownInProgress ||
            !nm.IsListening)
            return;

        if (clientId == BlackClientId.Value)
        {
            BlackClientId.Value = Unset;
            Debug.Log("[Server] Black client unbound; persistent Black player seat retained");
        }
        if (clientId == WhiteClientId.Value)
        {
            WhiteClientId.Value = Unset;
            Debug.Log("[Server] White client unbound; host shutdown will end the match");
        }
        // If host leaves, the session ends; no host migration here.
    }

    private void OnClientIdentityValidated(ulong clientId, string playerId)
    {
        if (!IsServer || !IsSpawned) return;

        TrySetSessionSeatPlayerIds();
        BindClientToPlayer(clientId, playerId);
    }

    private void PrepareSeatStateForCurrentSession()
    {
        var currentSessionId = _sessionService?.CurrentSessionId ?? string.Empty;
        if (string.Equals(_seatSessionId, currentSessionId, StringComparison.Ordinal))
            return;

        _seatSessionId = currentSessionId;
        WhitePlayerId.Value = new FixedString128Bytes();
        BlackPlayerId.Value = new FixedString128Bytes();
        WhiteClientId.Value = Unset;
        BlackClientId.Value = Unset;
    }

    private bool TrySetSessionSeatPlayerIds()
    {
        if (!IsServer || _sessionService == null) return false;
        if (HasPersistentSeatPlayerIds) return true;

        var whitePlayerId = _sessionService.HostPlayerId;
        if (string.IsNullOrEmpty(whitePlayerId)) return false;

        var blackPlayerId = _sessionService.CurrentPlayerIds.FirstOrDefault(playerId =>
            !string.Equals(playerId, whitePlayerId, StringComparison.Ordinal));
        if (string.IsNullOrEmpty(blackPlayerId)) return false;

        return SetSeatPlayerIds(whitePlayerId, blackPlayerId);
    }

    private bool SetSeatPlayerIds(string whitePlayerId, string blackPlayerId)
    {
        if (!IsServer || string.IsNullOrEmpty(whitePlayerId) || string.IsNullOrEmpty(blackPlayerId))
            return false;

        if (HasPersistentSeatPlayerIds)
        {
            return string.Equals(WhitePlayerId.Value.ToString(), whitePlayerId, StringComparison.Ordinal) &&
                   string.Equals(BlackPlayerId.Value.ToString(), blackPlayerId, StringComparison.Ordinal);
        }

        WhitePlayerId.Value = new FixedString128Bytes(whitePlayerId);
        BlackPlayerId.Value = new FixedString128Bytes(blackPlayerId);
        Debug.Log($"[PlayerTeams] Seats assigned: White={whitePlayerId}, Black={blackPlayerId}");
        return true;
    }

    private void BindKnownValidatedClients()
    {
        var networkManager = NetworkManager;
        if (networkManager == null || _sessionService == null) return;

        foreach (var clientId in networkManager.ConnectedClientsIds)
            TryBindValidatedClient(clientId);
    }

    private bool TryBindValidatedClient(ulong clientId)
    {
        if (_sessionService == null ||
            !_sessionService.TryGetValidatedPlayerId(clientId, out var playerId))
            return false;

        return BindClientToPlayer(clientId, playerId);
    }

    private bool BindClientToPlayer(ulong clientId, string playerId)
    {
        if (!IsServer || !HasPersistentSeatPlayerIds || string.IsNullOrEmpty(playerId))
            return false;

        if (string.Equals(WhitePlayerId.Value.ToString(), playerId, StringComparison.Ordinal))
        {
            if (WhiteClientId.Value != Unset && WhiteClientId.Value != clientId)
                return false;

            WhiteClientId.Value = clientId;
            Debug.Log($"[PlayerTeams] Bound White player to client {clientId}");
            return true;
        }

        if (string.Equals(BlackPlayerId.Value.ToString(), playerId, StringComparison.Ordinal))
        {
            if (BlackClientId.Value != Unset && BlackClientId.Value != clientId)
                return false;

            BlackClientId.Value = clientId;
            Debug.Log($"[PlayerTeams] Bound Black player to client {clientId}");
            return true;
        }

        Debug.LogWarning($"[PlayerTeams] Validated player {playerId} does not own a match seat.");
        return false;
    }

    private bool HasPersistentSeatPlayerIds =>
        WhitePlayerId.Value.Length > 0 && BlackPlayerId.Value.Length > 0;

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

    public static string GetPlayerIdForTeam(TeamSide team)
    {
        if (!Instance) return string.Empty;
        if (team == TeamSide.White) return Instance.WhitePlayerId.Value.ToString();
        if (team == TeamSide.Black) return Instance.BlackPlayerId.Value.ToString();
        return string.Empty;
    }

    public static ulong? GetClientIdForTeam(TeamSide team)
    {
        if (!Instance) return null;

        ulong clientId = team == TeamSide.White
            ? Instance.WhiteClientId.Value
            : team == TeamSide.Black
                ? Instance.BlackClientId.Value
                : Unset;
        return clientId == Unset ? null : clientId;
    }

    public static TeamSide MyTeam =>
        Instance ? GetTeam(NetworkManager.Singleton.LocalClientId) : TeamSide.None;
}
