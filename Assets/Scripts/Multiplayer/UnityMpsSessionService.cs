using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using Unity.Services.Relay.Models;

public sealed class UnityMpsSessionService : IMultiplayerSessionService
{
    private const int PrivateMatchPlayerLimit = 2;
    private const int MaximumPlayerIdPayloadBytes = 256;

    private ISession _session;
    private bool _hostNetworkStartRequested;
    private bool _intentionalLeave;
    private bool _unexpectedClientDisconnectPending;
    private bool _reconnectAttemptInProgress;
    private ulong? _connectedLocalClientId;
    private readonly Dictionary<ulong, string> _validatedPlayerIdsByClientId = new();
    private readonly HashSet<string> _expectedNetworkPlayerIds = new(StringComparer.Ordinal);
    private NetworkManager _identityNetworkManager;
    private bool _previousConnectionApprovalEnabled;
    private byte[] _previousConnectionData = Array.Empty<byte>();
    private Action<NetworkManager.ConnectionApprovalRequest, NetworkManager.ConnectionApprovalResponse>
        _previousConnectionApprovalCallback;

    public static UnityMpsSessionService Instance { get; } = new UnityMpsSessionService();

    public event Action<MultiplayerSessionState, string> StatusChanged;
    public event Action<ulong, string> ClientIdentityValidated;

    public MultiplayerSessionState State { get; private set; } = MultiplayerSessionState.Idle;
    public string StatusMessage { get; private set; } = "Idle.";
    public string LocalPlayerId => AuthenticationService.Instance.IsSignedIn
        ? AuthenticationService.Instance.PlayerId
        : string.Empty;
    public string CurrentSessionId => _session?.Id ?? string.Empty;
    public string HostPlayerId => _session?.Host ?? string.Empty;
    public IReadOnlyList<string> CurrentPlayerIds => _session == null
        ? Array.Empty<string>()
        : _session.Players
            .Select(player => player.Id)
            .Where(playerId => !string.IsNullOrEmpty(playerId))
            .ToArray();
    public string CurrentJoinCode => _session?.Code ?? string.Empty;
    public bool IsHost => _session?.IsHost ?? false;
    public int PlayerCount => _session?.PlayerCount ?? 0;

    private UnityMpsSessionService()
    {
    }

    public async Task InitializeAsync()
    {
        if (UgsBootstrap.IsReady && AuthenticationService.Instance.IsSignedIn)
            return;

        SetState(MultiplayerSessionState.SigningIn, "Signing in...");

        try
        {
            await UgsBootstrap.Init();
        }
        catch (Exception exception)
        {
            throw Fail("Authentication failed", exception);
        }
    }

    public async Task CreatePrivateMatchAsync()
    {
        EnsureNoActiveSession();
        await InitializeAsync();
        ConfigureNgoIdentityPayload();
        SetState(MultiplayerSessionState.Creating, "Creating private match...");

        try
        {
            var options = new SessionOptions
            {
                Name = "RollForCheckmate Private Match",
                MaxPlayers = PrivateMatchPlayerLimit,
                IsPrivate = true,
                IsLocked = false
            };

            options.WithNetworkOptions(new NetworkOptions
            {
                RelayProtocol = RelayProtocol.DTLS
            });

            _session = await MultiplayerService.Instance.CreateSessionAsync(options);
            SubscribeToSession(_session);
            SetState(
                MultiplayerSessionState.WaitingForOpponent,
                $"Private match created. Code: {_session.Code}. Waiting for opponent...");

            await TryStartHostNetworkAsync();
        }
        catch (Exception exception)
        {
            ClearSession();
            throw Fail("Create private match failed", exception);
        }
    }

    public async Task JoinPrivateMatchAsync(string code)
    {
        EnsureNoActiveSession();

        code = code?.Trim();
        if (string.IsNullOrEmpty(code))
            throw Fail("Join private match failed", new ArgumentException("Enter a session code."));

        await InitializeAsync();
        ConfigureNgoIdentityPayload();
        SetState(MultiplayerSessionState.Joining, "Joining private match...");

        try
        {
            var options = new JoinSessionOptions();
            options.WithNetworkOptions(new NetworkOptions
            {
                RelayProtocol = RelayProtocol.DTLS
            });

            _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code, options);
            SubscribeToSession(_session);
            SetState(MultiplayerSessionState.Connecting, "Joined private match. Waiting for host network...");
            ApplyNetworkState(_session.Network.State);
        }
        catch (Exception exception)
        {
            ClearSession();
            throw Fail("Join private match failed", exception);
        }
    }

    public async Task<bool> TryReconnectAsync()
    {
        if (_reconnectAttemptInProgress)
            return false;

        _reconnectAttemptInProgress = true;
        _unexpectedClientDisconnectPending = false;

        try
        {
            await InitializeAsync();

            if (_session?.IsHost == true)
            {
                SetState(MultiplayerSessionState.Failed, "Host reconnect is not supported for this match.");
                return false;
            }

            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                throw new InvalidOperationException("NetworkManager is required for session reconnect.");
            if (networkManager.IsListening)
                throw new InvalidOperationException("Cannot reconnect while networking is already active.");

            SetState(MultiplayerSessionState.Reconnecting, "Reconnecting...");

            var joinedSessionIds = await MultiplayerService.Instance.GetJoinedSessionIdsAsync();
            if (joinedSessionIds.Count == 0)
            {
                ClearSession();
                SetState(MultiplayerSessionState.Failed, "Session expired. Reconnect is no longer available.");
                return false;
            }

            if (joinedSessionIds.Count != 1)
            {
                SetState(
                    MultiplayerSessionState.Failed,
                    "Reconnect failed: more than one active session was found.");
                return false;
            }

            var sessionId = joinedSessionIds[0];
            if (_session != null &&
                !string.Equals(_session.Id, sessionId, StringComparison.Ordinal))
            {
                SetState(
                    MultiplayerSessionState.Failed,
                    "Reconnect failed: the retained session does not match the current match.");
                return false;
            }

            ConfigureNgoIdentityPayload();

            var reconnectedSession = await MultiplayerService.Instance.ReconnectToSessionAsync(sessionId);
            if (reconnectedSession.IsHost)
            {
                SetState(MultiplayerSessionState.Failed, "Host reconnect is not supported for this match.");
                return false;
            }

            if (!reconnectedSession.Players.Any(player =>
                    string.Equals(player.Id, LocalPlayerId, StringComparison.Ordinal)))
            {
                SetState(MultiplayerSessionState.Failed, "Reconnect failed: player is no longer a session member.");
                return false;
            }

            ReplaceSession(reconnectedSession);
            SetState(MultiplayerSessionState.Connecting, "Rejoined session. Connecting...");
            ApplyNetworkState(reconnectedSession.Network.State);
            return reconnectedSession.Network.State == NetworkState.Started;
        }
        catch (Exception exception)
        {
            Fail("Reconnect failed", exception);
            return false;
        }
        finally
        {
            _reconnectAttemptInProgress = false;
        }
    }

    public async Task LeaveMatchAsync()
    {
        if (_session == null)
        {
            SetState(MultiplayerSessionState.Idle, "No active private match.");
            return;
        }

        _intentionalLeave = true;
        _unexpectedClientDisconnectPending = false;
        SetState(MultiplayerSessionState.Leaving, "Leaving private match...");
        var sessionToLeave = _session;

        try
        {
            if (sessionToLeave.IsHost)
                await sessionToLeave.AsHost().DeleteAsync();
            else
                await sessionToLeave.LeaveAsync();

            ClearSession();
            SetState(MultiplayerSessionState.Idle, "Left private match.");
        }
        catch (Exception exception)
        {
            throw Fail("Leave private match failed", exception);
        }
        finally
        {
            _intentionalLeave = false;
        }
    }

    public bool TryGetValidatedPlayerId(ulong clientId, out string playerId)
    {
        return _validatedPlayerIdsByClientId.TryGetValue(clientId, out playerId);
    }

    private async void OnSessionChanged()
    {
        if (_session == null)
            return;

        try
        {
            if (_session.IsHost &&
                _session.PlayerCount < PrivateMatchPlayerLimit &&
                _session.AsHost().Network.State == NetworkState.Stopped)
            {
                SetState(
                    MultiplayerSessionState.WaitingForOpponent,
                    $"Private match created. Code: {_session.Code}. Waiting for opponent...");
            }

            await TryStartHostNetworkAsync();
        }
        catch (Exception exception)
        {
            Fail("Host network start failed", exception);
        }
    }

    private void OnPlayerJoined(string playerId)
    {
        OnSessionChanged();
    }

    private void OnSessionStateChanged(SessionState sessionState)
    {
        if (sessionState == SessionState.Deleted)
        {
            ClearSession();
            SetState(MultiplayerSessionState.Idle, "Private match ended.");
        }
        else if (sessionState == SessionState.Disconnected && !_intentionalLeave)
        {
            SetState(MultiplayerSessionState.Disconnected, "Session disconnected. Reconnect is available.");
        }
    }

    private void OnSessionRemoved()
    {
        ClearSession();
        SetState(
            _intentionalLeave ? MultiplayerSessionState.Idle : MultiplayerSessionState.Failed,
            _intentionalLeave
                ? "Private match ended."
                : "Session ended or reconnect membership expired.");
    }

    private void OnNetworkStateChanged(NetworkState networkState)
    {
        ApplyNetworkState(networkState);
    }

    private void OnNetworkStartFailed(SessionError error)
    {
        _hostNetworkStartRequested = false;
        SetState(MultiplayerSessionState.Failed, $"Network start failed: {error}.");
    }

    private async Task TryStartHostNetworkAsync()
    {
        if (_session == null || !_session.IsHost || _session.PlayerCount < PrivateMatchPlayerLimit)
            return;

        var hostSession = _session.AsHost();
        if (_hostNetworkStartRequested || hostSession.Network.State != NetworkState.Stopped)
            return;

        CaptureExpectedNetworkPlayerIds();
        _hostNetworkStartRequested = true;
        SetState(MultiplayerSessionState.Connecting, "Opponent joined. Starting network...");

        try
        {
            if (!hostSession.IsLocked)
            {
                hostSession.IsLocked = true;
                await hostSession.SavePropertiesAsync();
            }

            await hostSession.Network.StartRelayNetworkAsync(RelayNetworkOptions.Default);
        }
        catch
        {
            _hostNetworkStartRequested = false;
            throw;
        }
    }

    private void ApplyNetworkState(NetworkState networkState)
    {
        switch (networkState)
        {
            case NetworkState.Starting:
                SetState(MultiplayerSessionState.Connecting, "Connecting...");
                break;
            case NetworkState.Started:
                if (_session?.IsHost == false && _identityNetworkManager != null)
                    _connectedLocalClientId = _identityNetworkManager.LocalClientId;
                SetState(MultiplayerSessionState.Connected, "Connected!");
                break;
            case NetworkState.Stopping:
                SetState(MultiplayerSessionState.Leaving, "Disconnecting...");
                break;
        }
    }

    private void SubscribeToSession(ISession session)
    {
        session.Changed += OnSessionChanged;
        session.PlayerJoined += OnPlayerJoined;
        session.StateChanged += OnSessionStateChanged;
        session.Deleted += OnSessionRemoved;
        session.RemovedFromSession += OnSessionRemoved;
        session.Network.StateChanged += OnNetworkStateChanged;
        session.Network.StartFailed += OnNetworkStartFailed;
    }

    private void UnsubscribeFromSession(ISession session)
    {
        session.Changed -= OnSessionChanged;
        session.PlayerJoined -= OnPlayerJoined;
        session.StateChanged -= OnSessionStateChanged;
        session.Deleted -= OnSessionRemoved;
        session.RemovedFromSession -= OnSessionRemoved;
        session.Network.StateChanged -= OnNetworkStateChanged;
        session.Network.StartFailed -= OnNetworkStartFailed;
    }

    private void ClearSession()
    {
        if (_session != null)
            UnsubscribeFromSession(_session);

        _session = null;
        _hostNetworkStartRequested = false;
        _unexpectedClientDisconnectPending = false;
        _connectedLocalClientId = null;
        _validatedPlayerIdsByClientId.Clear();
        _expectedNetworkPlayerIds.Clear();
        UnregisterNgoIdentityCallbacks();
    }

    private void ReplaceSession(ISession session)
    {
        if (_session != null)
            UnsubscribeFromSession(_session);

        _session = session;
        _hostNetworkStartRequested = false;
        SubscribeToSession(session);
    }

    private void ConfigureNgoIdentityPayload()
    {
        var networkManager = NetworkManager.Singleton;
        if (networkManager == null)
            throw new InvalidOperationException("NetworkManager is required for authenticated multiplayer.");
        if (networkManager.IsListening)
            throw new InvalidOperationException("Cannot configure player identity after networking has started.");

        var localPlayerId = LocalPlayerId;
        if (string.IsNullOrEmpty(localPlayerId))
            throw new InvalidOperationException("Authenticated PlayerId is unavailable.");

        _validatedPlayerIdsByClientId.Clear();
        _expectedNetworkPlayerIds.Clear();

        if (_identityNetworkManager != networkManager)
        {
            UnregisterNgoIdentityCallbacks();
            _identityNetworkManager = networkManager;
            _previousConnectionApprovalEnabled = networkManager.NetworkConfig.ConnectionApproval;
            _previousConnectionData = networkManager.NetworkConfig.ConnectionData == null
                ? Array.Empty<byte>()
                : (byte[])networkManager.NetworkConfig.ConnectionData.Clone();
            _previousConnectionApprovalCallback = networkManager.ConnectionApprovalCallback;
            _identityNetworkManager.OnClientDisconnectCallback += OnNgoClientDisconnected;
            _identityNetworkManager.OnClientStopped += OnNgoClientStopped;
            UnityEngine.Application.quitting += OnApplicationQuitting;
        }

        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(localPlayerId);
        networkManager.ConnectionApprovalCallback = OnNgoConnectionApproval;
    }

    private void CaptureExpectedNetworkPlayerIds()
    {
        _expectedNetworkPlayerIds.Clear();

        if (_session == null || !_session.IsHost)
            throw new InvalidOperationException("Only the session host can establish network participants.");

        foreach (var player in _session.Players)
        {
            if (!string.IsNullOrEmpty(player.Id))
                _expectedNetworkPlayerIds.Add(player.Id);
        }

        if (_expectedNetworkPlayerIds.Count != PrivateMatchPlayerLimit ||
            string.IsNullOrEmpty(_session.Host) ||
            !_expectedNetworkPlayerIds.Contains(_session.Host) ||
            !string.Equals(LocalPlayerId, _session.Host, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The private match participant list is not ready for network startup.");
        }

        _validatedPlayerIdsByClientId[NetworkManager.ServerClientId] = LocalPlayerId;
    }

    private void OnNgoConnectionApproval(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = false;
        response.CreatePlayerObject = false;
        response.Pending = false;

        if (_session == null || !_session.IsHost || _expectedNetworkPlayerIds.Count != PrivateMatchPlayerLimit)
        {
            DenyConnection(response, "Session identity validation is unavailable.");
            return;
        }

        if (request.Payload == null || request.Payload.Length == 0 ||
            request.Payload.Length > MaximumPlayerIdPayloadBytes)
        {
            DenyConnection(response, "Authenticated PlayerId is missing or invalid.");
            return;
        }

        var claimedPlayerId = Encoding.UTF8.GetString(request.Payload);
        bool isCurrentSessionPlayer = _session.Players.Any(player =>
            string.Equals(player.Id, claimedPlayerId, StringComparison.Ordinal));

        if (!_expectedNetworkPlayerIds.Contains(claimedPlayerId) || !isCurrentSessionPlayer)
        {
            DenyConnection(response, "Player is not an expected active session member.");
            return;
        }

        bool isHostConnection = request.ClientNetworkId == NetworkManager.ServerClientId;
        if (isHostConnection != string.Equals(claimedPlayerId, _session.Host, StringComparison.Ordinal))
        {
            DenyConnection(response, "Player identity does not match the expected session seat.");
            return;
        }

        var expectedRemotePlayerId = _expectedNetworkPlayerIds.Single(playerId =>
            !string.Equals(playerId, _session.Host, StringComparison.Ordinal));
        if (!isHostConnection && !string.Equals(claimedPlayerId, expectedRemotePlayerId, StringComparison.Ordinal))
        {
            DenyConnection(response, "Player identity does not match the expected session seat.");
            return;
        }

        if (_validatedPlayerIdsByClientId.TryGetValue(request.ClientNetworkId, out var existingPlayerId) &&
            !string.Equals(existingPlayerId, claimedPlayerId, StringComparison.Ordinal))
        {
            DenyConnection(response, "Network client identity changed during approval.");
            return;
        }

        bool playerAlreadyBoundElsewhere = _validatedPlayerIdsByClientId.Any(binding =>
            binding.Key != request.ClientNetworkId &&
            string.Equals(binding.Value, claimedPlayerId, StringComparison.Ordinal));
        if (playerAlreadyBoundElsewhere)
        {
            DenyConnection(response, "Player identity is already connected.");
            return;
        }

        _validatedPlayerIdsByClientId[request.ClientNetworkId] = claimedPlayerId;
        response.Approved = true;
        response.Reason = string.Empty;
        ClientIdentityValidated?.Invoke(request.ClientNetworkId, claimedPlayerId);

        if (!isHostConnection && State == MultiplayerSessionState.Disconnected)
            SetState(MultiplayerSessionState.Connected, "Opponent reconnected.");
    }

    private void OnNgoClientDisconnected(ulong clientId)
    {
        _validatedPlayerIdsByClientId.Remove(clientId);

        if (_intentionalLeave || _session == null)
            return;

        if (_session.IsHost)
        {
            if (clientId != NetworkManager.ServerClientId)
                SetState(MultiplayerSessionState.Disconnected, "Opponent connection lost.");
            return;
        }

        if (_connectedLocalClientId.HasValue && clientId != _connectedLocalClientId.Value)
            return;

        _unexpectedClientDisconnectPending = true;
        _connectedLocalClientId = null;
        SetState(MultiplayerSessionState.Disconnected, "Connection lost. Reconnecting...");
    }

    private async void OnNgoClientStopped(bool isHost)
    {
        if (isHost || !_unexpectedClientDisconnectPending || _intentionalLeave || _reconnectAttemptInProgress)
            return;

        await TryReconnectAsync();
    }

    private void OnApplicationQuitting()
    {
        _intentionalLeave = true;
        _unexpectedClientDisconnectPending = false;
    }

    private void UnregisterNgoIdentityCallbacks()
    {
        if (_identityNetworkManager == null)
            return;

        _identityNetworkManager.OnClientDisconnectCallback -= OnNgoClientDisconnected;
        _identityNetworkManager.OnClientStopped -= OnNgoClientStopped;
        UnityEngine.Application.quitting -= OnApplicationQuitting;
        if (_identityNetworkManager.ConnectionApprovalCallback == OnNgoConnectionApproval)
        {
            _identityNetworkManager.NetworkConfig.ConnectionApproval = _previousConnectionApprovalEnabled;
            _identityNetworkManager.NetworkConfig.ConnectionData = _previousConnectionData;
            _identityNetworkManager.ConnectionApprovalCallback = _previousConnectionApprovalCallback;
        }

        _identityNetworkManager = null;
        _previousConnectionApprovalEnabled = false;
        _previousConnectionData = Array.Empty<byte>();
        _previousConnectionApprovalCallback = null;
    }

    private static void DenyConnection(NetworkManager.ConnectionApprovalResponse response, string reason)
    {
        response.Approved = false;
        response.CreatePlayerObject = false;
        response.Pending = false;
        response.Reason = reason;
    }

    private void EnsureNoActiveSession()
    {
        if (_session != null)
            throw new InvalidOperationException("Leave the current private match before starting another one.");
    }

    private InvalidOperationException Fail(string operation, Exception exception)
    {
        var message = GetFriendlyError(operation, exception);
        SetState(MultiplayerSessionState.Failed, message);
        return new InvalidOperationException(message, exception);
    }

    private static string GetFriendlyError(string operation, Exception exception)
    {
        var detail = exception.Message;
        if (!string.IsNullOrEmpty(detail))
        {
            var lowerDetail = detail.ToLowerInvariant();
            if (lowerDetail.Contains("full"))
                return "That private match is full.";
            if (lowerDetail.Contains("locked"))
                return "That private match is locked.";
        }

        if (exception is SessionException sessionException)
        {
            switch (sessionException.Error)
            {
                case SessionError.NotAuthorized:
                    return "Authentication failed. Please try again.";
                case SessionError.SessionNotFound:
                case SessionError.InvalidSessionIdentifier:
                    return "Private match code is invalid or no longer available.";
                case SessionError.SessionDeleted:
                    return "That private match has ended.";
                case SessionError.Forbidden:
                    return "That private match is locked or unavailable.";
                case SessionError.NetworkManagerNotInitialized:
                case SessionError.NetworkManagerStartFailed:
                case SessionError.NetworkSetupFailed:
                case SessionError.TransportComponentMissing:
                case SessionError.TransportInvalid:
                    return "Unable to start the multiplayer connection.";
            }
        }

        return string.IsNullOrEmpty(detail) ? operation + "." : $"{operation}: {detail}";
    }

    private void SetState(MultiplayerSessionState state, string message)
    {
        State = state;
        StatusMessage = message;
        StatusChanged?.Invoke(state, message);
    }
}
