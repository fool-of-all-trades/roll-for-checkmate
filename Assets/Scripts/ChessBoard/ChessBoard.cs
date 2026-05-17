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
public class ChessBoard : NetworkBehaviour
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
        TargetSelection
    }

    private BoardInputMode _inputMode = BoardInputMode.Normal;
    private bool IsTargetSelectionActive => _inputMode == BoardInputMode.TargetSelection;

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


    /// <summary>
    /// Detects clicks outside UI and routes to selection or move logic.
    /// </summary>
    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            var cam = Camera.main;
            if (!cam) return;

            Vector3 worldPos = cam.ScreenToWorldPoint(Input.mousePosition);
            Vector3Int cell = boardTilemap.WorldToCell(worldPos);

            int viewRow = cell.y;
            int viewCol = cell.x;

            var (modelRow, modelCol) = ViewToModel(viewRow, viewCol);

            HandleClick(modelRow, modelCol);
            UpdateUI();
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

    #region Helpers

    /// <summary>
    /// Returns the piece at the given square, or null if empty.
    /// </summary>
    public Piece GetPieceAt(int row, int col)
    {
        foreach (var p in pieces)
        {
            if (p == null || !p.gameObject.activeInHierarchy)
                continue;

            var np = p.GetComponent<NetworkPiece>();
            if (np != null && np.IsCaptured.Value)
                continue;

            if (p.Row == row && p.Col == col)
                return p;
        }

        return null;
    }

    // Flip UI helpers
    private const int BoardSize = 8;

    private bool IsWhitePerspective
    {
        get
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return true;

            // Host is white by design in your game
            if (nm.IsHost) return true;

            // On clients, ask PlayerTeams which side *this* local client owns
            if (PlayerTeams.Instance != null && PlayerTeams.Instance.IsSpawned)
            {
                var myTeam = PlayerTeams.GetTeam(nm.LocalClientId);
                return myTeam == TeamSide.White;
            }

            // If unknown momentarily, default to black so we don't mirror host
            return false;
        }
    }


    /** Convert model coords -> view coords depending on local perspective. */
    private (int row, int col) ModelToView(int row, int col)
    {
        if (IsWhitePerspective) return (row, col);
        int max = BoardSize - 1;
        return (max - row, max - col); // 180° rotate for black
    }

    /** Convert view coords (what you clicked) -> model coords for logic. */
    private (int row, int col) ViewToModel(int row, int col)
    {
        if (IsWhitePerspective) return (row, col);
        int max = BoardSize - 1;
        return (max - row, max - col);
    }

    // End of flip helpers


    /// <summary>
    /// Converts board coordinates to world-space position.
    /// </summary>
    public Vector3 GridToWorld(int row, int col)
    {

        var (vr, vc) = ModelToView(row, col);
        Vector3Int cellPos = new Vector3Int(vc, vr, 0);
        return boardTilemap.GetCellCenterWorld(cellPos);

        //Vector3Int cellPos = new Vector3Int(col, row, 0);
        //return boardTilemap.GetCellCenterWorld(cellPos);
    }

    /// <summary>
    /// Removes a captured piece from the active list and hides its GameObject.
    /// </summary>
    private void HideCapturedPiece(Piece piece)
    {
        pieces.Remove(piece);
        //piece.gameObject.SetActive(false);
        // Visuals are handled by NetworkPiece.IsCaptured -> ApplyCapturedState
    }

    /// <summary>
    /// Updates a piece’s visual and logic position to the new board coordinates.
    /// </summary>
    private void MovePiece(Piece p, int row, int col)
    {
        p.SetViewPosition(row, col);
    }

    private bool TryGetLocalTeam(out TeamSide team)
    {
        var nm = NetworkManager.Singleton;
        team = (PlayerTeams.Instance != null && PlayerTeams.Instance.IsSpawned && nm != null)
            ? PlayerTeams.GetTeam(nm.LocalClientId)
            : TeamSide.Black; // safe default

        return team != TeamSide.None;
    }

    private bool IsLocalPlayersTurn(TeamSide team)
    {
        bool teamIsWhite = (team == TeamSide.White);
        return teamIsWhite == TurnSync.IsWhiteTurn;
    }

    private bool CanSelectPieceForMove(Piece piece)
    {
        if (piece == null) return false;

        var np = piece.GetComponent<NetworkPiece>();
        if (np && np.IsCaptured.Value) return false;

        if (!TryGetLocalTeam(out var localTeam)) return false;
        if (!IsLocalPlayersTurn(localTeam)) return false;

        bool localTeamIsWhite = (localTeam == TeamSide.White);
        return piece.Team == localTeamIsWhite;
    }

    #endregion

    #region Input Handling

    /// <summary>
    /// Selects the piece at (row, col) if it belongs to the player whose turn it is.
    /// </summary>
    private void SelectPiece(int row, int col)
    {
        var p = GetPieceAt(row, col);

        if (p == null)
        {
            infoPiece = null;
            return;
        }

        if (CanSelectPieceForMove(p))
        {
            selectedPiece = p;
        }

        infoPiece = p;
    }

    /// <summary>
    /// Attempts to move the currently selected piece to (destRow, destCol).
    /// </summary>
    private void AttemptMove(int destRow, int destCol)
    {
        if (selectedPiece == null) return;
        if (!CanSelectPieceForMove(selectedPiece)) return;

        var nm = NetworkManager.Singleton;
        bool networkingActive = nm != null && nm.IsListening;
        if (networkingActive)
        {
            if (TryGetMoveRelay(out var relay))
            {
                relay.SendMove(selectedPiece, destRow, destCol);
            }
            else if (nm.IsServer)
            {
                // Fallback for host/server if relay wiring is unavailable.
                controller.TryMove(selectedPiece, destRow, destCol);
            }
        }
        else
        {
            // Fallback for non-networked/local play.
            controller.TryMove(selectedPiece, destRow, destCol);
        }
        selectedPiece = null;
        infoPiece = null;
    }

    /// <summary>
    /// Handles a board click: either selects a piece or asks controller to make a move.
    /// </summary>
    private void HandleClick(int row, int col)
    {
        var clickedPiece = GetPieceAt(row, col);

        if (IsTargetSelectionActive)
        {
            if (clickedPiece != null && pendingTargetChoices != null && pendingTargetChoices.Contains(clickedPiece))
                CompleteTargetSelection(clickedPiece);
            else
                CancelTargetSelection();

            return;   // ignore normal selection logic while targeting for DIVINE SMITE
        }

        if (selectedPiece == null)
            SelectPiece(row, col);
        else if (clickedPiece != null && clickedPiece.Team == selectedPiece.Team)
            SelectPiece(row, col);
        else
            AttemptMove(row, col);
    }
    #endregion


    #region UI
    private void OnUltimateButtonClicked()
    {
        Debug.Log("[UI] Ultimate button clicked!");
        if (selectedPiece == null)
        {
            Debug.Log("[UI] selectedPiece is null");
            return;
        }

        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsServer)
        {
            Debug.Log($"[UI] selectedPiece = {selectedPiece.Name} – calling UseUltimateAbility");
            selectedPiece.UseUltimateAbility(controller);
        }
        else
        {
            // Client asks the host
            if (!TryGetMoveRelay(out var relay))
            {
                return;
            }

            relay.SendUltimate(selectedPiece);
        }
    }

    /// <summary>
    /// Updates the info panel and shows or hides the Ultimate button based on selection state.
    /// </summary>
    private void UpdateUI()
    {
        if (infoPiece != null)
        {
            infoNameText.text = infoPiece.Name;
            infoTeamText.text = infoPiece.Team ? "Team: White" : "Team: Black";
            infoLevelText.text = "Lvl: " + infoPiece.Level.ToString();
            infoStunnedText.text = $"Stun: {infoPiece.StunnedTurns} turn{(infoPiece.StunnedTurns == 1 ? "" : "s")}";
            infoCursedText.text = $"Cursed: {infoPiece.CursedTurns} turn{(infoPiece.CursedTurns == 1 ? "" : "s")}";

            infoSpriteImage.sprite = infoPiece.GetComponent<SpriteRenderer>().sprite;
        }

        ultimateButton.gameObject.SetActive(selectedPiece != null && selectedPiece.CanUseUltimate());
    }

    /// <summary>
    /// Updates the info panel, but used in NetworkPiece so that the client sees the changes too.
    /// </summary>
    public void RefreshInfoIf(Piece p)
    {
        if (infoPiece == p)
            UpdateUI();
    }


    // --- Turn label wiring ---
    private void RefreshTurnLabel()
    {
        if (infoTurnText == null) return;

        var nm = NetworkManager.Singleton;
        if (nm == null || TurnSync.Instance == null || PlayerTeams.Instance == null || !PlayerTeams.Instance.IsSpawned)
        {
            infoTurnText.text = "Waiting…";
            return;
        }

        var myTeam = PlayerTeams.GetTeam(nm.LocalClientId);
        if (myTeam == TeamSide.None)
        {
            infoTurnText.text = "Waiting for seat…";
            return;
        }

        bool whiteTurn = TurnSync.Instance.WhiteTurn.Value;
        bool myWhite = (myTeam == TeamSide.White);
        bool myTurn = (myWhite == whiteTurn);

        // Show both “who’s turn” and “can I move”
        string who = whiteTurn ? "White" : "Black";
        infoTurnText.text = myTurn ? $"Your turn ({who})" : $"Opponent’s turn ({who})";
    }

    private void OnWhiteTurnChanged(bool _, bool __) => RefreshTurnLabel();


    #endregion

    #region Event Methods
    // Event methods are called automatically when the given event raises
    public void ApplyMoveVisuals(MoveResult m)
    {
        if (m.FromRow < 0)
        {
            // spawned by Bishop's ultimate
            var p = m.Piece;

            // 1. Register in the board’s lookup grid  (so clicks find it)
            if (!pieces.Contains(p))
                pieces.Add(p);

            // 2. Move the prefab to the correct world position
            p.SetViewPosition(m.ToRow, m.ToCol);
        }
        else
        {
            // normal move
            MovePiece(m.Piece, m.ToRow, m.ToCol);
        }

        // hide the loser if there was a capture
        if (m.Captured != null) HideCapturedPiece(m.Captured);

        UpdateUI();
    }

    public void ShowRoll(int value)
    {
        rollText.text = $"Rolled: {value}";
    }

    void ShowRoads(IReadOnlyList<Vector2Int> white, IReadOnlyList<Vector2Int> black)
    {
        // paint tiles, show highlights for sacred road, sparkly stuff
    }
    #endregion

    //public void BeginTargetSelection(Predicate<Piece> filter, Action<Piece> onChosen)
    //{
    //    targetMode = true;

    //    pendingTargetCallback = p =>
    //    {
    //        // once target isn't null and is enemy we call onChosen which performs DIVINE SMITE
    //        if (filter(p)) onChosen(p);
    //        else Debug.Log("Target selection failed: piece does not match filter.");

    //        targetMode = false;
    //        pendingTargetCallback = null;
    //    };
    //}

    public void BeginTargetSelection(
        IReadOnlyList<Piece> targets,
        Action<Piece> onChosen)
    {
        EnterTargetSelection(targets, onChosen);

        // highlight only targets
        foreach (var p in targets)
        {
            //HighlightSquare(p.Row, p.Col, Color.yellow);
        }
    }

    private void EnterTargetSelection(IReadOnlyList<Piece> targets, Action<Piece> onChosen)
    {
        pendingTargetChoices = targets;
        pendingTargetCallback = onChosen;
        _inputMode = BoardInputMode.TargetSelection;
    }

    private void ExitTargetSelection()
    {
        //ClearHighlights();
        _inputMode = BoardInputMode.Normal;
        pendingTargetChoices = null;
        pendingTargetCallback = null;
    }

    private void CancelTargetSelection()
    {
        CompleteTargetSelection(null);
    }

    private void CompleteTargetSelection(Piece target)
    {
        if (!IsTargetSelectionActive) return;

        var callback = pendingTargetCallback;
        ExitTargetSelection();
        callback?.Invoke(target);
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
