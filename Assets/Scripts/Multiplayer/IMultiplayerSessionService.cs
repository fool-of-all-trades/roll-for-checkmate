using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public enum MultiplayerSessionState
{
    Idle,
    SigningIn,
    Creating,
    WaitingForOpponent,
    Joining,
    Connecting,
    Connected,
    Disconnected,
    Reconnecting,
    Leaving,
    Failed
}

public interface IMultiplayerSessionService
{
    event Action<MultiplayerSessionState, string> StatusChanged;
    event Action<ulong, string> ClientIdentityValidated;

    MultiplayerSessionState State { get; }
    string StatusMessage { get; }
    string LocalPlayerId { get; }
    string CurrentSessionId { get; }
    string HostPlayerId { get; }
    IReadOnlyList<string> CurrentPlayerIds { get; }
    string CurrentJoinCode { get; }
    bool IsHost { get; }
    int PlayerCount { get; }

    Task InitializeAsync();
    Task CreatePrivateMatchAsync();
    Task JoinPrivateMatchAsync(string code);
    Task<bool> TryReconnectAsync();
    Task LeaveMatchAsync();

    bool TryGetValidatedPlayerId(ulong clientId, out string playerId);
}
