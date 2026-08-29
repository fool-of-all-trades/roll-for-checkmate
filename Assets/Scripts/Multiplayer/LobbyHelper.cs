using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using UnityEngine;
using Unity.Services.Authentication;
using static UnityEditor.PhysicsVisualizationSettings;
using LobbyQueryFilter = Unity.Services.Lobbies.Models.QueryFilter;

public class LobbyManager : MonoBehaviour
{
    public static LobbyManager Instance { get; private set; }

    public Lobby CurrentLobby { get; private set; }
    public event Action<Lobby> OnLobbyChanged;

    [SerializeField] private float heartbeatSecs = 15f;
    [SerializeField] private float pollSecs = 2.5f;

    Coroutine _hb, _poll;

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public async Task<Lobby> CreateLobbyAsync(string lobbyName = "Chess Lobby", int maxPlayers = 2, string region = null)
    {
        await UgsBootstrap.Init();

        var options = new CreateLobbyOptions
        {
            IsPrivate = false,
            Data = new Dictionary<string, DataObject>
            {
                // Relay code is blank until host allocates Relay
                { "joinCode", new DataObject(DataObject.VisibilityOptions.Member, "") },
                { "mode",     new DataObject(DataObject.VisibilityOptions.Public,  "chess") },
                { "region",   new DataObject(DataObject.VisibilityOptions.Public,  region ?? "") },
                { "started",  new DataObject(DataObject.VisibilityOptions.Member, "0") }
            }
        };

        CurrentLobby = await LobbyService.Instance.CreateLobbyAsync(lobbyName, maxPlayers, options);
        Debug.Log($"[Lobby] Created {CurrentLobby.Id} code={CurrentLobby.LobbyCode}");
        StartHeartbeat();
        StartPolling();
        OnLobbyChanged?.Invoke(CurrentLobby);
        return CurrentLobby;
    }

    public async Task<Lobby> JoinLobbyByCodeAsync(string code)
    {
        await UgsBootstrap.Init();
        CurrentLobby = await LobbyService.Instance.JoinLobbyByCodeAsync(code);
        Debug.Log($"[Lobby] Joined {CurrentLobby.Id}");
        StartPolling();
        OnLobbyChanged?.Invoke(CurrentLobby);
        return CurrentLobby;
    }

    public async Task<Lobby> QuickJoinAsync()
    {
        await UgsBootstrap.Init();
        var q = new QuickJoinLobbyOptions
        {
            Filter = new List<LobbyQueryFilter>
            {
                new LobbyQueryFilter(field: LobbyQueryFilter.FieldOptions.AvailableSlots,
                                op: LobbyQueryFilter.OpOptions.GT,
                                value: "0"),
                new LobbyQueryFilter(field: LobbyQueryFilter.FieldOptions.S1,
                                op: LobbyQueryFilter.OpOptions.EQ,
                                value: "chess") // matches Data["mode"]
            }
        };
        CurrentLobby = await LobbyService.Instance.QuickJoinLobbyAsync(q);
        Debug.Log($"[Lobby] Quick joined {CurrentLobby.Id}");
        StartPolling();
        OnLobbyChanged?.Invoke(CurrentLobby);
        return CurrentLobby;
    }

    public async Task SetRelayJoinCodeAsync(string joinCode)
    {
        if (CurrentLobby == null) return;
        var up = new UpdateLobbyOptions
        {
            Data = new Dictionary<string, DataObject>
            {
                { "joinCode", new DataObject(DataObject.VisibilityOptions.Member, joinCode) },
                { "started",  new DataObject(DataObject.VisibilityOptions.Member, "1") }
            }
        };
        CurrentLobby = await LobbyService.Instance.UpdateLobbyAsync(CurrentLobby.Id, up);
        OnLobbyChanged?.Invoke(CurrentLobby);
    }

    public string GetRelayJoinCode()
    {
        if (CurrentLobby == null) return null;
        if (CurrentLobby.Data != null && CurrentLobby.Data.TryGetValue("joinCode", out var d))
            return d.Value;
        return null;
    }

    public async Task LeaveAsync()
    {
        if (CurrentLobby == null) return;
        try
        {
            await LobbyService.Instance.RemovePlayerAsync(CurrentLobby.Id, AuthenticationService.Instance.PlayerId);
        }
        catch { /* ignored */ }
        CurrentLobby = null;
        StopAll();
        OnLobbyChanged?.Invoke(null);
    }

    void StartHeartbeat()
    {
        if (_hb != null) StopCoroutine(_hb);
        _hb = StartCoroutine(HeartbeatCo());
    }

    void StartPolling()
    {
        if (_poll != null) StopCoroutine(_poll);
        _poll = StartCoroutine(PollCo());
    }

    void StopAll()
    {
        if (_hb != null) StopCoroutine(_hb); _hb = null;
        if (_poll != null) StopCoroutine(_poll); _poll = null;
    }

    IEnumerator HeartbeatCo()
    {
        while (CurrentLobby != null)
        {
            LobbyService.Instance.SendHeartbeatPingAsync(CurrentLobby.Id);
            yield return new WaitForSecondsRealtime(heartbeatSecs);
        }
    }

    IEnumerator PollCo()
    {
        while (CurrentLobby != null)
        {
            var t = LobbyService.Instance.GetLobbyAsync(CurrentLobby.Id);
            while (!t.IsCompleted) yield return null;
            if (!t.IsFaulted) { CurrentLobby = t.Result; OnLobbyChanged?.Invoke(CurrentLobby); }

            // If host set started=1 and joinCode is present, clients can auto-connect
            yield return new WaitForSecondsRealtime(pollSecs);
        }
    }
}
