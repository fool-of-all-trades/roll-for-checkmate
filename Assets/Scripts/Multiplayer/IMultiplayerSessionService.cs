using System;
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
    Leaving,
    Failed
}

public interface IMultiplayerSessionService
{
    event Action<MultiplayerSessionState, string> StatusChanged;

    MultiplayerSessionState State { get; }
    string StatusMessage { get; }
    string LocalPlayerId { get; }
    string CurrentJoinCode { get; }
    bool IsHost { get; }
    int PlayerCount { get; }

    Task InitializeAsync();
    Task CreatePrivateMatchAsync();
    Task JoinPrivateMatchAsync(string code);
    Task LeaveMatchAsync();
}
