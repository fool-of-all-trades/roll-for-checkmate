using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Lobbies;
using Unity.Services.Multiplayer;
using Unity.Services.Relay.Models;

public sealed class UnityMpsSessionService : IMultiplayerSessionService
{
    private enum SessionOperation
    {
        None,
        CreatePrivate,
        JoinPrivate,
        Quick,
        Reconnect
    }

    private const int MatchPlayerLimit = 2;
    private const int MaximumPlayerIdPayloadBytes = 256;
    private const int QuickJoinTimeoutSeconds = 5;
    private const string PrivateMatchName = "RollForCheckmate Private Match";
    private const string QuickMatchName = "RollForCheckmate Quick Match";
    private const string MatchmakingStatePropertyKey = "matchmakingState";
    private const string QuickMatchWaitingValue = "rfc.chess.quick.v1.waiting.2p";
    private const string QuickMatchStartedValue = "rfc.chess.quick.v1.started.2p";
    private const string DiagnosticBuildMarker = "Patch1B-diagnostic";

    private ISession _session;
    private string _originalHostPlayerId = string.Empty;
    private bool _hostNetworkStartRequested;
    private bool _intentionalLeave;
    private bool _unexpectedClientDisconnectPending;
    private int _reconnectAttemptInProgress;
    private bool _reconnectUnavailable;
    private int _sessionOperationOwner;
    private long _sessionLifecycleGeneration;
    private bool _quickMatchOperationInProgress;
    private bool _quickMatchCancellationRequested;
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
        TraceSessionOperation("CreatePrivate", "enter");
        if (RejectExistingSessionLifecycle(SessionOperation.CreatePrivate))
            return;
        if (!TryAcquireSessionOperation(SessionOperation.CreatePrivate))
            return;

        try
        {
            if (RejectExistingSessionLifecycle(SessionOperation.CreatePrivate))
                return;

            EnsureNoActiveSession();
            await InitializeAsync();
            ConfigureNgoIdentityPayload();
            SetState(MultiplayerSessionState.Creating, "Creating private match...");

            try
            {
                var options = new SessionOptions
                {
                    Name = PrivateMatchName,
                    MaxPlayers = MatchPlayerLimit,
                    IsPrivate = true,
                    IsLocked = false
                };

                options.WithNetworkOptions(new NetworkOptions
                {
                    RelayProtocol = RelayProtocol.DTLS
                });

                TraceSessionOperation("CreatePrivate", "before-mps");
                _session = await MultiplayerService.Instance.CreateSessionAsync(options);
                TraceSessionOperation("CreatePrivate", "after-mps");
                CaptureOriginalHost(_session);
                SubscribeToSession(_session);
                SetState(
                    MultiplayerSessionState.WaitingForOpponent,
                    $"Private match created. Code: {_session.Code}. Waiting for opponent...");

                await TryStartMatchIfReadyAsync();
                Volatile.Write(ref _reconnectUnavailable, false);
                TraceSessionOperation("CreatePrivate", "exit");
            }
            catch (Exception exception)
            {
                TraceSessionOperation("CreatePrivate", "exit");
                ClearSession();
                throw Fail("Create private match failed", exception);
            }
        }
        finally
        {
            ReleaseSessionOperation(SessionOperation.CreatePrivate);
        }
    }

    public async Task JoinPrivateMatchAsync(string code)
    {
        TraceSessionOperation("JoinPrivate", "enter");
        if (RejectExistingSessionLifecycle(SessionOperation.JoinPrivate))
            return;
        if (!TryAcquireSessionOperation(SessionOperation.JoinPrivate))
            return;

        try
        {
            if (RejectExistingSessionLifecycle(SessionOperation.JoinPrivate))
                return;

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

                TraceSessionOperation("JoinPrivate", "before-mps");
                _session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code, options);
                TraceSessionOperation("JoinPrivate", "after-mps");
                CaptureOriginalHost(_session);
                SubscribeToSession(_session);
                SetState(MultiplayerSessionState.Connecting, "Joined private match. Waiting for host network...");
                ApplyNetworkState(_session.Network.State);
                Volatile.Write(ref _reconnectUnavailable, false);
                TraceSessionOperation("JoinPrivate", "exit");
            }
            catch (Exception exception)
            {
                TraceSessionOperation("JoinPrivate", "exit");
                ClearSession();
                throw Fail("Join private match failed", exception);
            }
        }
        finally
        {
            ReleaseSessionOperation(SessionOperation.JoinPrivate);
        }
    }

    public async Task FindOrCreateQuickMatchAsync()
    {
        TraceSessionOperation("Quick", "enter");
        if (RejectExistingSessionLifecycle(SessionOperation.Quick))
            return;
        if (!TryAcquireSessionOperation(SessionOperation.Quick))
            return;

        try
        {
            if (RejectExistingSessionLifecycle(SessionOperation.Quick))
                return;

            EnsureNoActiveSession();
            _quickMatchOperationInProgress = true;
            _quickMatchCancellationRequested = false;

            try
            {
                await InitializeAsync();
                if (_quickMatchCancellationRequested)
                    return;

                ConfigureNgoIdentityPayload();
                SetState(MultiplayerSessionState.Searching, "Searching for opponent...");

                var sessionOptions = new SessionOptions
                {
                    Name = QuickMatchName,
                    MaxPlayers = MatchPlayerLimit,
                    IsPrivate = false,
                    IsLocked = false,
                    SessionProperties = new Dictionary<string, SessionProperty>
                    {
                        [MatchmakingStatePropertyKey] = new SessionProperty(
                            QuickMatchWaitingValue,
                            VisibilityPropertyOptions.Public,
                            PropertyIndex.String1)
                    }
                };

                sessionOptions.WithNetworkOptions(new NetworkOptions
                {
                    RelayProtocol = RelayProtocol.DTLS
                });

                var quickJoinOptions = new QuickJoinOptions
                {
                    CreateSession = true,
                    Timeout = TimeSpan.FromSeconds(QuickJoinTimeoutSeconds),
                    Filters = new List<FilterOption>
                    {
                        new FilterOption(
                            FilterField.StringIndex1,
                            QuickMatchWaitingValue,
                            FilterOperation.Equal),
                        new FilterOption(
                            FilterField.AvailableSlots,
                            "0",
                            FilterOperation.Greater),
                        new FilterOption(
                            FilterField.IsLocked,
                            "false",
                            FilterOperation.Equal)
                    }
                };

                TraceSessionOperation("Quick", "before-mps");
                var matchmadeSession = await MultiplayerService.Instance.MatchmakeSessionAsync(
                    quickJoinOptions,
                    sessionOptions);
                TraceSessionOperation("Quick", "after-mps");
                if (_quickMatchCancellationRequested)
                {
                    await CleanupCancelledQuickMatchAsync(matchmadeSession);
                    return;
                }

                _session = matchmadeSession;
                CaptureOriginalHost(_session);
                SubscribeToSession(_session);

                if (_session.IsHost)
                {
                    SetState(MultiplayerSessionState.WaitingForOpponent, "Waiting for opponent...");
                }
                else
                {
                    SetState(MultiplayerSessionState.Connecting, "Opponent found. Connecting...");
                }

                ApplyNetworkState(_session.Network.State);
                await TryStartMatchIfReadyAsync();
                Volatile.Write(ref _reconnectUnavailable, false);
            }
            catch (Exception exception)
            {
                if (_quickMatchCancellationRequested)
                    return;

                ClearSession();
                throw Fail("Quick Match failed", exception);
            }
            finally
            {
                bool wasCancelled = _quickMatchCancellationRequested;
                _quickMatchOperationInProgress = false;
                _quickMatchCancellationRequested = false;

                if (wasCancelled)
                {
                    ClearSession();
                    _intentionalLeave = false;
                    SetState(MultiplayerSessionState.Idle, "Quick Match cancelled.");
                }

                TraceSessionOperation("Quick", "exit");
            }
        }
        finally
        {
            ReleaseSessionOperation(SessionOperation.Quick);
        }
    }

    public async Task<bool> TryReconnectAsync()
    {
        TraceSessionOperation("Reconnect", "enter");
        if (!TryAcquireSessionOperation(SessionOperation.Reconnect))
            return false;

        try
        {
            long lifecycleGeneration = Interlocked.Read(ref _sessionLifecycleGeneration);
            bool reconnectSucceeded = await TryReconnectOwnedAsync(lifecycleGeneration);
            if (IsReconnectStale(lifecycleGeneration) &&
                (_session != null || _intentionalLeave))
            {
                TraceSessionOperation("Reconnect", "reconnect-stale-before-release");
                await CleanupStaleReconnectAsync(_session);
                return false;
            }

            return reconnectSucceeded;
        }
        finally
        {
            ReleaseSessionOperation(SessionOperation.Reconnect);
        }
    }

    private async Task<bool> TryReconnectOwnedAsync(long lifecycleGeneration)
    {
        if (Volatile.Read(ref _reconnectUnavailable))
        {
            TraceSessionOperation("Reconnect", "reconnect-unavailable-local");
            SetState(MultiplayerSessionState.Failed, "Reconnect is not available.");
            return false;
        }

        if (Interlocked.CompareExchange(ref _reconnectAttemptInProgress, 1, 0) != 0)
        {
            TraceSessionOperation("Reconnect", "gate-rejected");
            return false;
        }

        TraceSessionOperation("Reconnect", "gate-acquired");

        _unexpectedClientDisconnectPending = false;

        try
        {
            if (Volatile.Read(ref _reconnectUnavailable))
            {
                TraceSessionOperation("Reconnect", "reconnect-unavailable-local");
                SetState(MultiplayerSessionState.Failed, "Reconnect is not available.");
                return false;
            }

            var networkManager = NetworkManager.Singleton;
            if (networkManager == null)
                throw new InvalidOperationException("NetworkManager is required for session reconnect.");
            if (networkManager.IsListening)
            {
                TraceSessionOperation("Reconnect", "already-connected");
                SetState(MultiplayerSessionState.Connected, "Already connected.");
                return false;
            }

            await InitializeAsync();

            bool reconnectIsStale = IsReconnectStale(lifecycleGeneration);
            if (!reconnectIsStale && _session?.IsHost == true)
            {
                SetState(MultiplayerSessionState.Failed, "Host reconnect is not supported for this match.");
                return false;
            }

            if (!reconnectIsStale)
                SetState(MultiplayerSessionState.Reconnecting, "Reconnecting...");

            TraceSessionOperation("Reconnect", "before-mps");
            var joinedMemberships = await MultiplayerService.Instance.GetJoinedSessionIdsAsync();
            TraceSessionOperation("Reconnect", "after-mps");
            if (IsReconnectStale(lifecycleGeneration))
                TraceSessionOperation("Reconnect", "reconnect-stale-after-discovery");
            var joinedSessionIds = (joinedMemberships ?? new List<string>())
                .Where(sessionId => !string.IsNullOrWhiteSpace(sessionId))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            UnityEngine.Debug.Log($"[Reconnect] joined membership count = {joinedSessionIds.Count}");
            if (joinedSessionIds.Count == 0)
            {
                TraceSessionOperation("Reconnect", "zero-memberships");
                Volatile.Write(ref _reconnectUnavailable, true);
                if (IsReconnectStale(lifecycleGeneration))
                {
                    await CleanupStaleReconnectAsync(null);
                    return false;
                }

                ClearSession();
                SetState(MultiplayerSessionState.Failed, "Session expired. Reconnect is no longer available.");
                return false;
            }

            UnityEngine.Debug.Log("[Reconnect] selecting session");
            var sessionId = await SelectReconnectSessionIdAsync(joinedSessionIds);
            if (IsReconnectStale(lifecycleGeneration))
                TraceSessionOperation("Reconnect", "reconnect-stale-after-selection");
            if (string.IsNullOrEmpty(sessionId))
            {
                if (IsReconnectStale(lifecycleGeneration))
                    await CleanupStaleReconnectAsync(null);
                return false;
            }

            UnityEngine.Debug.Log($"[Reconnect] selected session {sessionId}");
            UnityEngine.Debug.Log("[Reconnect] configuring NGO identity");
            ConfigureNgoIdentityPayload();

            UnityEngine.Debug.Log("[Reconnect] calling ReconnectToSessionAsync");
            TraceSessionOperation("Reconnect", "before-mps");
            var reconnectedSession = await MultiplayerService.Instance.ReconnectToSessionAsync(sessionId);
            TraceSessionOperation("Reconnect", "after-mps");
            UnityEngine.Debug.Log("[Reconnect] session returned");
            if (IsReconnectStale(lifecycleGeneration))
            {
                TraceSessionOperation("Reconnect", "reconnect-stale-after-mps");
                await CleanupStaleReconnectAsync(reconnectedSession);
                return false;
            }

            if (reconnectedSession == null)
                throw new InvalidOperationException("MPS reconnect returned no session.");

            if (reconnectedSession.IsHost)
            {
                SetState(MultiplayerSessionState.Failed, "Host reconnect is not supported for this match.");
                return false;
            }

            if (!reconnectedSession.Players.Any(player =>
                    string.Equals(player?.Id, LocalPlayerId, StringComparison.Ordinal)))
            {
                Volatile.Write(ref _reconnectUnavailable, true);
                SetState(MultiplayerSessionState.Failed, "Reconnect failed: player is no longer a session member.");
                return false;
            }

            if (IsReconnectStale(lifecycleGeneration))
            {
                TraceSessionOperation("Reconnect", "reconnect-stale-before-adoption");
                await CleanupStaleReconnectAsync(reconnectedSession);
                return false;
            }

            UnityEngine.Debug.Log("[Reconnect] replacing session");
            ReplaceSession(reconnectedSession);
            SetState(MultiplayerSessionState.Connecting, "Rejoined session. Connecting...");
            ApplyNetworkState(reconnectedSession.Network.State);
            return reconnectedSession.Network.State == NetworkState.Started;
        }
        catch (SessionException exception) when (exception.Error == SessionError.SessionNotFound)
        {
            if (IsReconnectStale(lifecycleGeneration))
            {
                await CleanupStaleReconnectAsync(null);
                return false;
            }

            Volatile.Write(ref _reconnectUnavailable, true);
            ClearSession();
            SetState(MultiplayerSessionState.Failed, "Match ended. The host left.");
            return false;
        }
        catch (Exception exception)
        {
            if (IsReconnectStale(lifecycleGeneration))
            {
                await CleanupStaleReconnectAsync(null);
                return false;
            }

            UnityEngine.Debug.LogException(exception);
            Fail("Reconnect failed", exception);
            return false;
        }
        finally
        {
            Interlocked.Exchange(ref _reconnectAttemptInProgress, 0);
            TraceSessionOperation("Reconnect", "exit");
        }
    }

    private async Task<string> SelectReconnectSessionIdAsync(IReadOnlyList<string> joinedSessionIds)
    {
        if (_session != null)
        {
            var currentSessionId = _session.Id;
            UnityEngine.Debug.Log($"[Reconnect] checking current session {currentSessionId}");
            if (joinedSessionIds.Any(sessionId =>
                    string.Equals(sessionId, currentSessionId, StringComparison.Ordinal)))
            {
                return currentSessionId;
            }

            Volatile.Write(ref _reconnectUnavailable, true);
            ClearSession();
            SetState(
                MultiplayerSessionState.Failed,
                "Session expired. The current match membership is no longer available.");
            return string.Empty;
        }

        if (joinedSessionIds.Count == 1)
            return joinedSessionIds[0];

        var candidates = new List<string>();
        foreach (var sessionId in joinedSessionIds)
        {
            try
            {
                UnityEngine.Debug.Log($"[Reconnect] inspecting session {sessionId}");
                var lobby = await LobbyService.Instance.GetLobbyAsync(sessionId);
                if (IsResumableRollForCheckmateLobby(lobby, sessionId))
                    candidates.Add(sessionId);
            }
            catch (LobbyServiceException exception)
                when (exception.Reason == LobbyExceptionReason.LobbyNotFound)
            {
                // GetJoinedSessionIdsAsync can briefly include a lobby that has just expired or been deleted.
            }
        }

        if (candidates.Count == 1)
            return candidates[0];

        SetState(
            MultiplayerSessionState.Failed,
            candidates.Count == 0
                ? "Reconnect failed: no resumable RollForCheckmate match was found."
                : "Reconnect failed: more than one resumable RollForCheckmate match was found.");
        if (candidates.Count == 0)
            Volatile.Write(ref _reconnectUnavailable, true);
        return string.Empty;
    }

    private bool IsResumableRollForCheckmateLobby(
        Unity.Services.Lobbies.Models.Lobby lobby,
        string expectedSessionId)
    {
        if (lobby == null ||
            !string.Equals(lobby.Id, expectedSessionId, StringComparison.Ordinal) ||
            lobby.MaxPlayers != MatchPlayerLimit ||
            !lobby.IsLocked ||
            string.IsNullOrEmpty(lobby.HostId) ||
            lobby.Players == null ||
            string.Equals(lobby.HostId, LocalPlayerId, StringComparison.Ordinal))
        {
            return false;
        }

        var playerIds = lobby.Players
            .Select(player => player?.Id)
            .Where(playerId => !string.IsNullOrEmpty(playerId))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (playerIds.Length != MatchPlayerLimit ||
            !playerIds.Contains(LocalPlayerId, StringComparer.Ordinal) ||
            !playerIds.Contains(lobby.HostId, StringComparer.Ordinal))
        {
            return false;
        }

        if (string.Equals(lobby.Name, PrivateMatchName, StringComparison.Ordinal))
            return lobby.IsPrivate;

        return string.Equals(lobby.Name, QuickMatchName, StringComparison.Ordinal) &&
               !lobby.IsPrivate &&
               lobby.Data != null &&
               lobby.Data.TryGetValue(MatchmakingStatePropertyKey, out var matchmakingState) &&
               matchmakingState != null &&
               string.Equals(matchmakingState.Value, QuickMatchStartedValue, StringComparison.Ordinal);
    }

    public async Task LeaveMatchAsync()
    {
        TraceSessionOperation("Leave", "enter");
        Interlocked.Increment(ref _sessionLifecycleGeneration);

        if ((SessionOperation)Volatile.Read(ref _sessionOperationOwner) == SessionOperation.Reconnect)
        {
            _intentionalLeave = true;
            _unexpectedClientDisconnectPending = false;
            Volatile.Write(ref _reconnectUnavailable, true);
            SetState(MultiplayerSessionState.Leaving, "Leaving match...");
            TraceSessionOperation("Leave", "leave-invalidated-reconnect");
            SetState(MultiplayerSessionState.Idle, "Left match.");
            TraceSessionOperation("Leave", "exit");
            return;
        }

        if (_session == null)
        {
            if (_quickMatchOperationInProgress)
            {
                _quickMatchCancellationRequested = true;
                _intentionalLeave = true;
                _unexpectedClientDisconnectPending = false;
                SetState(MultiplayerSessionState.Leaving, "Cancelling Quick Match...");
                SetState(MultiplayerSessionState.Idle, "Quick Match cancelled.");
                TraceSessionOperation("Leave", "exit");
                return;
            }

            SetState(MultiplayerSessionState.Idle, "No active match.");
            TraceSessionOperation("Leave", "exit");
            return;
        }

        if (_quickMatchOperationInProgress)
            _quickMatchCancellationRequested = true;

        _intentionalLeave = true;
        _unexpectedClientDisconnectPending = false;
        SetState(MultiplayerSessionState.Leaving, "Leaving match...");
        var sessionToLeave = _session;

        try
        {
            TraceSessionOperation("Leave", "before-mps");
            if (sessionToLeave.IsHost)
                await sessionToLeave.AsHost().DeleteAsync();
            else
                await sessionToLeave.LeaveAsync();
            TraceSessionOperation("Leave", "after-mps");

            Volatile.Write(ref _reconnectUnavailable, true);
            ClearSession();
            SetState(MultiplayerSessionState.Idle, "Left match.");
        }
        catch (Exception exception)
        {
            throw Fail("Leave private match failed", exception);
        }
        finally
        {
            _intentionalLeave = false;
            TraceSessionOperation("Leave", "exit");
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
            if (_session.IsHost && !IsOriginalHost())
            {
                SetState(MultiplayerSessionState.Failed, "Original host left. Match ended.");
                return;
            }

            if (CanShowWaitingForOpponent())
            {
                SetState(
                    MultiplayerSessionState.WaitingForOpponent,
                    GetWaitingForOpponentMessage());
            }

            await TryStartMatchIfReadyAsync();
        }
        catch (Exception exception)
        {
            Fail("Host network start failed", exception);
        }
    }

    private void OnSessionStateChanged(SessionState sessionState)
    {
        if (sessionState == SessionState.Deleted)
        {
            Volatile.Write(ref _reconnectUnavailable, true);
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
        Volatile.Write(ref _reconnectUnavailable, true);
        ClearSession();
        SetState(
            _intentionalLeave ? MultiplayerSessionState.Idle : MultiplayerSessionState.Failed,
            _intentionalLeave
                ? "Private match ended."
                : "Session ended or reconnect membership expired.");
    }

    private static async Task CleanupCancelledQuickMatchAsync(ISession session)
    {
        if (session == null)
            return;

        try
        {
            if (session.IsHost)
                await session.AsHost().DeleteAsync();
            else
                await session.LeaveAsync();
        }
        catch (SessionException exception) when (
            exception.Error == SessionError.SessionNotFound ||
            exception.Error == SessionError.SessionDeleted ||
            exception.Error == SessionError.NotInLobby)
        {
            // The cancelled result was already removed while the Quick Match request was completing.
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogException(exception);
        }
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

    private async Task TryStartMatchIfReadyAsync()
    {
        if (_session == null ||
            !_session.IsHost ||
            !IsOriginalHost() ||
            _session.PlayerCount < MatchPlayerLimit)
            return;

        var hostSession = _session.AsHost();
        if (_hostNetworkStartRequested || hostSession.Network.State != NetworkState.Stopped)
            return;

        CaptureExpectedNetworkPlayerIds();
        _hostNetworkStartRequested = true;
        SetState(MultiplayerSessionState.Connecting, "Opponent joined. Starting network...");

        try
        {
            if (IsQuickMatchSession(hostSession))
            {
                hostSession.SetProperty(
                    MatchmakingStatePropertyKey,
                    new SessionProperty(
                        QuickMatchStartedValue,
                        VisibilityPropertyOptions.Public,
                        PropertyIndex.String1));
            }

            if (!hostSession.IsLocked)
                hostSession.IsLocked = true;

            await hostSession.SavePropertiesAsync();

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
        session.StateChanged += OnSessionStateChanged;
        session.Deleted += OnSessionRemoved;
        session.RemovedFromSession += OnSessionRemoved;
        session.Network.StateChanged += OnNetworkStateChanged;
        session.Network.StartFailed += OnNetworkStartFailed;
    }

    private void UnsubscribeFromSession(ISession session)
    {
        session.Changed -= OnSessionChanged;
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
        _originalHostPlayerId = string.Empty;
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
        CaptureOriginalHost(session);
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
        if (_session == null || !_session.IsHost)
            throw new InvalidOperationException("Only the session host can establish network participants.");

        var currentPlayerIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var player in _session.Players)
        {
            if (!string.IsNullOrEmpty(player.Id))
                currentPlayerIds.Add(player.Id);
        }

        if (currentPlayerIds.Count != MatchPlayerLimit ||
            string.IsNullOrEmpty(_session.Host) ||
            !currentPlayerIds.Contains(_session.Host) ||
            !string.Equals(LocalPlayerId, _session.Host, StringComparison.Ordinal) ||
            !string.Equals(_originalHostPlayerId, _session.Host, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The match participant list is not ready for network startup.");
        }

        if (_expectedNetworkPlayerIds.Count == 0)
        {
            foreach (var playerId in currentPlayerIds)
                _expectedNetworkPlayerIds.Add(playerId);
        }
        else if (!_expectedNetworkPlayerIds.SetEquals(currentPlayerIds))
        {
            throw new InvalidOperationException("Match participants changed after seat identities were frozen.");
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

        if (_session == null || !_session.IsHost || _expectedNetworkPlayerIds.Count != MatchPlayerLimit)
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
        if (isHost ||
            !_unexpectedClientDisconnectPending ||
            _intentionalLeave ||
            Volatile.Read(ref _reconnectAttemptInProgress) != 0)
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
            throw new InvalidOperationException("Leave the current match before starting another one.");
        if (_quickMatchOperationInProgress)
            throw new InvalidOperationException("Wait for the current Quick Match search to finish cancelling.");
    }

    private string GetWaitingForOpponentMessage()
    {
        return IsQuickMatchSession(_session)
            ? "Waiting for opponent..."
            : $"Private match created. Code: {_session.Code}. Waiting for opponent...";
    }

    private bool CanShowWaitingForOpponent()
    {
        if (_session == null ||
            !_session.IsHost ||
            !IsOriginalHost() ||
            _session.PlayerCount >= MatchPlayerLimit ||
            _expectedNetworkPlayerIds.Count != 0)
        {
            return false;
        }

        var hostSession = _session.AsHost();
        if (hostSession.IsLocked || hostSession.Network.State != NetworkState.Stopped)
            return false;

        return !IsQuickMatchSession(hostSession) || IsQuickMatchWaitingSession(hostSession);
    }

    private void CaptureOriginalHost(ISession session)
    {
        if (string.IsNullOrEmpty(_originalHostPlayerId))
            _originalHostPlayerId = session?.Host ?? string.Empty;
    }

    private bool IsOriginalHost()
    {
        return !string.IsNullOrEmpty(_originalHostPlayerId) &&
               string.Equals(LocalPlayerId, _originalHostPlayerId, StringComparison.Ordinal);
    }

    private static bool IsQuickMatchSession(ISession session)
    {
        if (session == null ||
            !session.Properties.TryGetValue(MatchmakingStatePropertyKey, out var property))
        {
            return false;
        }

        return string.Equals(property.Value, QuickMatchWaitingValue, StringComparison.Ordinal) ||
               string.Equals(property.Value, QuickMatchStartedValue, StringComparison.Ordinal);
    }

    private static bool IsQuickMatchWaitingSession(ISession session)
    {
        return session != null &&
               session.Properties.TryGetValue(MatchmakingStatePropertyKey, out var property) &&
               string.Equals(property.Value, QuickMatchWaitingValue, StringComparison.Ordinal);
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

    private bool TryAcquireSessionOperation(SessionOperation operation)
    {
        bool acquired = Interlocked.CompareExchange(
            ref _sessionOperationOwner,
            (int)operation,
            (int)SessionOperation.None) == (int)SessionOperation.None;
        TraceSessionOperation(operation.ToString(), acquired ? "operation-acquired" : "operation-rejected");
        return acquired;
    }

    private bool IsReconnectStale(long lifecycleGeneration)
    {
        return Interlocked.Read(ref _sessionLifecycleGeneration) != lifecycleGeneration;
    }

    private async Task CleanupStaleReconnectAsync(ISession staleSession)
    {
        TraceSessionOperation("Reconnect", "stale-reconnect-cleanup-start");

        try
        {
            if (staleSession != null)
                await staleSession.LeaveAsync();
        }
        catch (SessionException exception) when (
            exception.Error == SessionError.SessionNotFound ||
            exception.Error == SessionError.SessionDeleted ||
            exception.Error == SessionError.NotInLobby ||
            exception.Error == SessionError.InvalidOperation)
        {
            // The host/session may disappear while an intentionally abandoned reconnect is being cleaned up.
        }
        catch (Exception exception)
        {
            UnityEngine.Debug.LogException(exception);
        }
        finally
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager != null &&
                networkManager.IsListening &&
                networkManager.IsClient &&
                !networkManager.IsServer)
            {
                networkManager.Shutdown();
            }

            Volatile.Write(ref _reconnectUnavailable, true);
            ClearSession();
            _intentionalLeave = false;
            _unexpectedClientDisconnectPending = false;
            SetState(MultiplayerSessionState.Idle, "Left match.");
            TraceSessionOperation("Reconnect", "stale-reconnect-cleanup-complete");
        }
    }

    private bool RejectExistingSessionLifecycle(SessionOperation operation)
    {
        if (_session == null)
            return false;

        TraceSessionOperation(operation.ToString(), "existing-session-rejected");
        return true;
    }

    private void ReleaseSessionOperation(SessionOperation operation)
    {
        bool released = Interlocked.CompareExchange(
            ref _sessionOperationOwner,
            (int)SessionOperation.None,
            (int)operation) == (int)operation;
        TraceSessionOperation(operation.ToString(), released ? "operation-released" : "operation-release-rejected");
    }

    private void TraceSessionOperation(string operation, string phase)
    {
        var networkManager = NetworkManager.Singleton;
        var sessionId = _session?.Id;
        if (string.IsNullOrEmpty(sessionId))
            sessionId = "none";

        UnityEngine.Debug.Log(
            $"[SessionOp] build={DiagnosticBuildMarker} " +
            $"service={RuntimeHelpers.GetHashCode(this)} " +
            $"operation={operation} " +
            $"phase={phase} " +
            $"state={State} " +
            $"session={sessionId} " +
            $"isListening={networkManager?.IsListening ?? false} " +
            $"isClient={networkManager?.IsClient ?? false} " +
            $"isServer={networkManager?.IsServer ?? false} " +
            $"operationOwner={(SessionOperation)Volatile.Read(ref _sessionOperationOwner)} " +
            $"lifecycleGeneration={Interlocked.Read(ref _sessionLifecycleGeneration)} " +
            $"reconnectGate={Volatile.Read(ref _reconnectAttemptInProgress)} " +
            $"quickInProgress={_quickMatchOperationInProgress} " +
            $"thread={Thread.CurrentThread.ManagedThreadId} " +
            $"dataPath={UnityEngine.Application.dataPath}");
    }
}
