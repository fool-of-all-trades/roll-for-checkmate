using System;
using System.Threading.Tasks;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using Unity.Services.Relay.Models;

public sealed class UnityMpsSessionService : IMultiplayerSessionService
{
    private const int PrivateMatchPlayerLimit = 2;

    private ISession _session;
    private bool _hostNetworkStartRequested;

    public static UnityMpsSessionService Instance { get; } = new UnityMpsSessionService();

    public event Action<MultiplayerSessionState, string> StatusChanged;

    public MultiplayerSessionState State { get; private set; } = MultiplayerSessionState.Idle;
    public string StatusMessage { get; private set; } = "Idle.";
    public string LocalPlayerId => AuthenticationService.Instance.IsSignedIn
        ? AuthenticationService.Instance.PlayerId
        : string.Empty;
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

    public async Task LeaveMatchAsync()
    {
        if (_session == null)
        {
            SetState(MultiplayerSessionState.Idle, "No active private match.");
            return;
        }

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
    }

    private async void OnSessionChanged()
    {
        if (_session == null)
            return;

        try
        {
            if (_session.IsHost && _session.PlayerCount < PrivateMatchPlayerLimit)
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
        if (sessionState == SessionState.Deleted || sessionState == SessionState.Disconnected)
        {
            ClearSession();
            SetState(MultiplayerSessionState.Idle, "Private match ended.");
        }
    }

    private void OnSessionRemoved()
    {
        ClearSession();
        SetState(MultiplayerSessionState.Idle, "Private match ended.");
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

        _hostNetworkStartRequested = true;
        SetState(MultiplayerSessionState.Connecting, "Opponent joined. Starting network...");

        try
        {
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
