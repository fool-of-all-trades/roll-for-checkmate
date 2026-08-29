using System;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class OnlinePlayUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField lobbyCodeField;
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private Button createLobbyBtn;
    [SerializeField] private Button joinByCodeBtn;
    [SerializeField] private Button quickPlayBtn;
    [SerializeField] private Button startMatchBtn;
    [SerializeField] private Button leaveLobbyBtn;

    private IMultiplayerSessionService _sessionService;

    void Awake()
    {
        _sessionService = UnityMpsSessionService.Instance;
        _sessionService.StatusChanged += OnSessionStatusChanged;

        createLobbyBtn.onClick.AddListener(async () => await CreateLobby());
        joinByCodeBtn.onClick.AddListener(async () => await JoinByCode());
        quickPlayBtn.onClick.AddListener(async () => await QuickPlay());
        startMatchBtn.onClick.AddListener(async () => await StartMatchAsHost());
        leaveLobbyBtn.onClick.AddListener(async () => await LeaveLobby());
    }

    async Task CreateLobby()
    {
        try
        {
            await _sessionService.CreatePrivateMatchAsync();
        }
        catch (Exception exception)
        {
            statusLabel.text = exception.Message;
        }
    }

    async Task JoinByCode()
    {
        try
        {
            await _sessionService.JoinPrivateMatchAsync(lobbyCodeField.text);
        }
        catch (Exception exception)
        {
            statusLabel.text = exception.Message;
        }
    }

    Task QuickPlay()
    {
        statusLabel.text = "Quick Play is not migrated yet. Use a private match code.";
        return Task.CompletedTask;
    }

    Task StartMatchAsHost()
    {
        statusLabel.text = "Private matches start automatically when the second player joins.";
        return Task.CompletedTask;
    }

    async Task LeaveLobby()
    {
        try
        {
            await _sessionService.LeaveMatchAsync();
        }
        catch (Exception exception)
        {
            statusLabel.text = exception.Message;
        }
    }

    void OnDestroy()
    {
        if (_sessionService != null)
            _sessionService.StatusChanged -= OnSessionStatusChanged;
    }

    private void OnSessionStatusChanged(MultiplayerSessionState state, string message)
    {
        statusLabel.text = message;
    }
}
