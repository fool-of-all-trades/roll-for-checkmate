using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class OnlinePlayUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField lobbyCodeField;   // for join by code
    [SerializeField] private TMP_Text statusLabel;
    [SerializeField] private Button createLobbyBtn;
    [SerializeField] private Button joinByCodeBtn;
    [SerializeField] private Button quickPlayBtn;
    [SerializeField] private Button startMatchBtn; // Host only: allocate Relay + start host
    [SerializeField] private Button leaveLobbyBtn;

    void Awake()
    {
        createLobbyBtn.onClick.AddListener(async () => await CreateLobby());
        joinByCodeBtn.onClick.AddListener(async () => await JoinByCode());
        quickPlayBtn.onClick.AddListener(async () => await QuickPlay());
        startMatchBtn.onClick.AddListener(async () => await StartMatchAsHost());
        leaveLobbyBtn.onClick.AddListener(async () => await LeaveLobby());

        if (LobbyManager.Instance) LobbyManager.Instance.OnLobbyChanged += OnLobbyChanged;
    }

    async Task CreateLobby()
    {
        try
        {
            await LobbyManager.Instance.CreateLobbyAsync("Chess Lobby", 2);
            statusLabel.text = $"Lobby created. Code: {LobbyManager.Instance.CurrentLobby.LobbyCode}";
        }
        catch (System.Exception e) { statusLabel.text = $"Create failed: {e.Message}"; }
    }

    async Task JoinByCode()
    {
        try
        {
            var code = lobbyCodeField.text.Trim();
            await LobbyManager.Instance.JoinLobbyByCodeAsync(code);
            statusLabel.text = "Joined lobby. Waiting for host…";
            TryAutoConnectIfReady();
        }
        catch (System.Exception e) { statusLabel.text = $"Join failed: {e.Message}"; }
    }

    async Task QuickPlay()
    {
        try
        {
            await LobbyManager.Instance.QuickJoinAsync();
            statusLabel.text = "Matched. Waiting for host…";
            TryAutoConnectIfReady();
        }
        catch (System.Exception e) { statusLabel.text = $"Quick play failed: {e.Message}"; }
    }

    async Task StartMatchAsHost()
    {
        // Host allocates Relay, sets joinCode into Lobby, then starts Host
        try
        {
            string joinCode = await RelayManager.StartHostWithRelayAsync(2);
            await LobbyManager.Instance.SetRelayJoinCodeAsync(joinCode);
            statusLabel.text = $"Hosting… Join code: {joinCode}";
        }
        catch (System.Exception e) { statusLabel.text = $"Host failed: {e.Message}"; }
    }

    async Task LeaveLobby()
    {
        await LobbyManager.Instance.LeaveAsync();
        statusLabel.text = "Left lobby.";
        if (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient)
        {
            NetworkManager.Singleton.Shutdown();
        }
    }

    void OnLobbyChanged(Unity.Services.Lobbies.Models.Lobby lob)
    {
        if (lob == null) return;
        TryAutoConnectIfReady();
    }

    async void TryAutoConnectIfReady()
    {
        var code = LobbyManager.Instance.GetRelayJoinCode();
        if (!string.IsNullOrEmpty(code) && !NetworkManager.Singleton.IsServer && !NetworkManager.Singleton.IsClient)
        {
            statusLabel.text = "Connecting to host…";
            try
            {
                await RelayManager.JoinClientWithRelayAsync(code);
                statusLabel.text = "Connected!";
            }
            catch (System.Exception e) { statusLabel.text = $"Connect failed: {e.Message}"; }
        }
    }
}
