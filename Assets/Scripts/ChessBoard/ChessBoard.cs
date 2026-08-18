using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using Pieces;
using TMPro;
using System;
using UnityEngine.Tilemaps;
using UnityEngine.EventSystems;
using System.Linq;
using Unity.Netcode;
using System.Collections;


/// <summary>
/// Renders the board, spawns pieces, handles input, and updates UI.
/// </summary>
public partial class ChessBoard : NetworkBehaviour
{
    [Header("Grid Settings")]
    public int tileSize = 1;
    [SerializeField] private Tilemap boardTilemap;

    [Header("Piece Prefabs")]
    public GameObject pawnPrefab;
    public GameObject rookPrefab;
    public GameObject knightPrefab;
    public GameObject bishopPrefab;
    public GameObject queenPrefab;
    public GameObject kingPrefab;

    [HideInInspector]
    public List<Piece> pieces = new List<Piece>();

    [Header("UI Elements")]
    // public Image diceImage;
    public TMP_Text rollText;
    public TMP_Text infoNameText;
    public TMP_Text infoTeamText;
    public TMP_Text infoLevelText;
    public Image infoSpriteImage;
    public Button ultimateButton;
    public TMP_Text infoTurnText;
    public TMP_Text infoStunnedText;
    public TMP_Text infoCursedText;

    private Piece selectedPiece;
    private Piece infoPiece;

    private Action<Piece> pendingTargetCallback;
    private IReadOnlyList<Piece> pendingTargetChoices;

    private enum BoardInputMode
    {
        Normal,
        TargetSelection,
        AscensionMove
    }

    private BoardInputMode _inputMode = BoardInputMode.Normal;

    [Header("References")]
    [SerializeField] private MonoBehaviour controllerRoot; 
    private IGameController controller;

    private MoveRelay _moveRelay;
    private bool _moveRelayLookupWarningLogged;
    private readonly ChessBoardStartupCoordinator _startupCoordinator = new ChessBoardStartupCoordinator();

    #region Unity Lifecycle

    private void Awake()
    {
        // subscribe to controller events
        controller = (IGameController)controllerRoot; 

        controller.OnMoveAccepted += ApplyMoveVisuals;
        controller.OnDuelRolled += ShowRoll;
        controller.OnRoadsChanged += ShowRoads;

        //if (_moveRelay == null)
        //    Debug.LogError("ChessBoard: _moveRelay is null!");
    }

    private bool TryGetMoveRelay(out MoveRelay relay)
    {
        if (_moveRelay != null)
        {
            relay = _moveRelay;
            return true;
        }

        _moveRelay = FindObjectOfType<MoveRelay>();
        relay = _moveRelay;

        if (relay != null)
        {
            _moveRelayLookupWarningLogged = false;
            return true;
        }

        if (!_moveRelayLookupWarningLogged)
        {
            Debug.LogError("[ChessBoard] No MoveRelay found.");
            _moveRelayLookupWarningLogged = true;
        }

        return false;
    }

    private bool _wired;
    private bool _spawnedThisRun;

    private void OnEnable()
    {
        // we'll rely on OnNetworkSpawn/Despawn instead
    }
    private void OnDisable() { }

    // --- NEW: network lifecycle handlers ---
    public override void OnNetworkSpawn()
    {
        // Server/Host: (re)spawn board every time networking starts
        if (IsServer && !_spawnedThisRun)
        {
            // (Optional) if anything somehow survived, nuke it
            CleanupLocalPieces();

            AddPieces();
            controller.Initialize(pieces);
            _spawnedThisRun = true;
            _ready = true;
        }
    }

    public override void OnNetworkDespawn()
    {
        // Reset per-session flags so next StartHost works
        _spawnedThisRun = false;
        _ready = false;

        // local list points to destroyed objects after shutdown; clear it
        pieces.Clear();
        ExitAscensionMoveMode();
        selectedPiece = null;
        infoPiece = null;
        ExitTargetSelection();
    }

    // Helper to be extra safe if you want to hard-reset visuals on new sessions
    private void CleanupLocalPieces()
    {
        var existing = FindObjectsOfType<Piece>();
        foreach (var p in existing)
        {
            if (p && p.TryGetComponent<NetworkObject>(out var no))
            {
                if (no.IsSpawned) no.Despawn(true);
                else Destroy(no.gameObject);
            }
            else if (p) // non-network piece (shouldn’t happen, but safe)
            {
                Destroy(p.gameObject);
            }
        }
        pieces.Clear();
    }

    private bool _ready;

    private IEnumerator Start()
    {
        // UI wiring stays
        ultimateButton.onClick.AddListener(OnUltimateButtonClicked);
        ultimateButton.gameObject.SetActive(false);

        // wait until networking actually started
        yield return StartCoroutine(_startupCoordinator.WaitForNetworkStarted());

        // wait until PlayerTeams exists & is spawned
        yield return StartCoroutine(_startupCoordinator.WaitForPlayerTeamsReady());

        // NEW: wait until we actually have a seat (clients only)
        if (!NetworkManager.Singleton.IsServer)
        {
            yield return StartCoroutine(_startupCoordinator.WaitForClientSeat());
        }

        // after waiting for networking + PlayerTeams (and optional seat for clients)
        yield return StartCoroutine(_startupCoordinator.WaitForTurnSync());

        // subscribe & set initial label
        TurnSync.Instance.WhiteTurn.OnValueChanged += OnWhiteTurnChanged;
        RefreshTurnLabel();

        // small sync frame
        yield return null;

        var myTeam = NetworkManager.Singleton.IsServer ? TeamSide.White : PlayerTeams.MyTeam;
        Debug.Log($"I am {myTeam}");

        if (NetworkManager.Singleton.IsServer)
        {
            //AddPieces();
            //controller.Initialize(pieces);
            //_ready = true;
            yield break;
        }

        // client path
        yield return StartCoroutine(_startupCoordinator.WaitForClientBoardPieces(clientPieces => pieces = clientPieces));
        controller.Initialize(pieces);
        _ready = true;
    }


    private void OnDestroy()
    {
        if (controller != null)
        {
            // unsubscribe from controller events
            controller.OnMoveAccepted -= ApplyMoveVisuals;
            controller.OnDuelRolled -= ShowRoll;
            controller.OnRoadsChanged -= ShowRoads;
            if (TurnSync.Instance != null)
                TurnSync.Instance.WhiteTurn.OnValueChanged -= OnWhiteTurnChanged;

        }
    }

    #endregion

    #region Board Initialization

    /// <summary>
    /// Places all pieces on their standard starting squares.
    /// </summary>
    private void AddPieces()
    {
        // Rooks
        SpawnPiece(rookPrefab, 0, 0, true);
        SpawnPiece(rookPrefab, 0, 7, true);
        SpawnPiece(rookPrefab, 7, 0, false);
        SpawnPiece(rookPrefab, 7, 7, false);

        // Knights
        SpawnPiece(knightPrefab, 0, 1, true);
        SpawnPiece(knightPrefab, 0, 6, true);
        SpawnPiece(knightPrefab, 7, 1, false);
        SpawnPiece(knightPrefab, 7, 6, false);

        // Bishops
        SpawnPiece(bishopPrefab, 0, 2, true);
        SpawnPiece(bishopPrefab, 0, 5, true);
        SpawnPiece(bishopPrefab, 7, 2, false);
        SpawnPiece(bishopPrefab, 7, 5, false);

        // Queens
        SpawnPiece(queenPrefab, 0, 4, true);
        SpawnPiece(queenPrefab, 7, 4, false);

        // Kings
        SpawnPiece(kingPrefab, 0, 3, true);
        SpawnPiece(kingPrefab, 7, 3, false);

        // Pawns
        for (int c = 0; c < 8; c++)
        {
            SpawnPiece(pawnPrefab, 1, c, true);
            SpawnPiece(pawnPrefab, 6, c, false);
        }
    }

    /// <summary>
    /// Spawns a piece prefab at the given grid position and adds it to the pieces list.
    /// </summary>
    private void SpawnPiece(GameObject prefab, int row, int col, bool team)
    {
        // Only the host will create & spawn networked objects
        if (!NetworkManager.Singleton.IsServer) return;

        Vector3 worldPos = GridToWorld(row, col);

        var go = Instantiate(prefab, worldPos, Quaternion.identity, boardTilemap.transform);

        // Local init (row/col, sprite, etc.)
        var piece = go.GetComponent<Piece>();
        piece.Init(this, row, col, team);

        // Network init
        var netPiece = go.GetComponent<NetworkPiece>();
        //netPiece.InitNetwork(team);
        //netPiece.CommitGridPos(row, col);

        //var netObj = go.GetComponent<NetworkObject>();
        //netObj.Spawn(true); // host-owned; replicates to all clients

        var netObj = go.GetComponent<NetworkObject>();
        netObj.Spawn(true);                   // now IsSpawned == true
        netPiece.InitNetwork(team);           // safe to write Team.Value here
        netPiece.CommitGridPos(row, col);     // safe to write Row/Col.Value here

        if (!pieces.Contains(piece))
            pieces.Add(piece);
    }
    #endregion

    private void OnWhiteTurnChanged(bool _, bool __)
    {
        ExitAscensionMoveMode();
        RefreshTurnLabel();
    }

    public void SetMoveRelay(MoveRelay relay)
    {
        _moveRelay = relay;
        if (relay != null)
        {
            _moveRelayLookupWarningLogged = false;
        }
    }

}
