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
    public GameObject infoPanel;
    public TMP_Text infoNameText;
    public TMP_Text infoTeamText;
    public TMP_Text infoLevelText;
    public Image infoSpriteImage;
    public Button ultimateButton;

    private Piece selectedPiece;
    private Piece infoPiece;

    private Action<Piece> pendingTargetCallback;
    private bool targetMode;   // when true, HandleClick calls the pendingTargetCallback

    [Header("References")]
    [SerializeField] private MonoBehaviour controllerRoot; 
    private IGameController controller;

    private MoveRelay _moveRelay;

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

    // --- add at top of class ---
    private bool _wired;
    private bool _spawnedThisRun;

    // --- add (or keep) these safely ---
    private void OnEnable()
    {
        // you can also leave this empty; we'll rely on OnNetworkSpawn/Despawn instead
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
        yield return new WaitUntil(() =>
            NetworkManager.Singleton != null &&
            (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient));

        // wait until PlayerTeams exists & is spawned
        yield return new WaitUntil(() =>
            PlayerTeams.Instance != null && PlayerTeams.Instance.IsSpawned);

        // NEW: wait until we actually have a seat (clients only)
        if (!NetworkManager.Singleton.IsServer)
        {
            yield return new WaitUntil(() => PlayerTeams.MyTeam != TeamSide.None);
        }

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
        yield return StartCoroutine(WaitForBoardClient());
        controller.Initialize(pieces);
        _ready = true;
    }

    private IEnumerator WaitForBoardClient()
    {
        // wait until at least one piece exists
        yield return new WaitUntil(() => FindObjectsOfType<Piece>().Length > 0);

        // wait until the count stabilizes for a few frames
        int lastCount = -1, stableFrames = 0;
        while (stableFrames < 3)
        {
            var current = FindObjectsOfType<Piece>();
            if (current.Length == lastCount) stableFrames++;
            else { stableFrames = 0; lastCount = current.Length; }
            yield return null;
        }

        pieces = new List<Piece>(FindObjectsOfType<Piece>());
    }


    private void OnDestroy()
    {
        if (controller != null)
        {
            // unsubscribe from controller events
            controller.OnMoveAccepted -= ApplyMoveVisuals;
            controller.OnDuelRolled -= ShowRoll;
            controller.OnRoadsChanged -= ShowRoads;
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
            {
                return;
            }

            Vector3 worldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            int col = Mathf.FloorToInt(worldPos.x / tileSize);
            int row = Mathf.FloorToInt(worldPos.y / tileSize);
            HandleClick(row, col);
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
            if (p.Row == row && p.Col == col)
                return p;
        return null;
    }

    /// <summary>
    /// Converts board coordinates to world-space position.
    /// </summary>
    public Vector3 GridToWorld(int row, int col)
    {
        Vector3Int cellPos = new Vector3Int(col, row, 0);
        return boardTilemap.GetCellCenterWorld(cellPos);
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

        var np = p.GetComponent<NetworkPiece>();
        if (np && np.IsCaptured.Value) return;

        bool myTeamIsWhite = NetworkManager.Singleton.IsHost;     // host = white, client = black
        bool isMyTurn = (myTeamIsWhite == TurnSync.IsWhiteTurn);

        if (p != null && p.Team == myTeamIsWhite && isMyTurn)
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

        bool myTeamIsWhite = NetworkManager.Singleton.IsHost;
        bool isMyTurn = (myTeamIsWhite == TurnSync.IsWhiteTurn);
        if (!isMyTurn) return;

        if (NetworkManager.Singleton.IsHost)
        {
            // The controller decides legality, capture, duels, en‑passant, castling, promotion...
            // Host executes directly
            controller.TryMove(selectedPiece, destRow, destCol);
        }
        else
        {
            //client asks politely => host validates and broadcasts
            //but so far the host ignores me hard
            if (_moveRelay == null)
            {
                _moveRelay = FindObjectOfType<MoveRelay>();
                if (_moveRelay == null)
                {
                    Debug.LogError("[ChessBoard] No MoveRelay found!!!");
                }
            }
            _moveRelay.SendMove(selectedPiece, destRow, destCol);
            //GetComponent<MoveRelay>().SendMove(selectedPiece, destRow, destCol);
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

        // targetMode is for when Knight's ultimate is active
        if (targetMode)
        {
            pendingTargetCallback?.Invoke(clickedPiece);
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
        if (selectedPiece != null)
        {
            Debug.Log($"[UI] selectedPiece = {selectedPiece.Name} – calling UseUltimateAbility");
            selectedPiece.UseUltimateAbility(controller);
        }
        else
        {
            Debug.Log("[UI] selectedPiece is null");
        }
    }

    /// <summary>
    /// Updates the info panel and shows or hides the Ultimate button based on selection state.
    /// </summary>
    private void UpdateUI()
    {
        if (infoPiece != null)
        {
            infoPanel.SetActive(true);
            infoNameText.text = infoPiece.Name;
            infoTeamText.text = infoPiece.Team ? "Team: White" : "Team: Black";
            infoLevelText.text = "Level: " + infoPiece.Level.ToString();
            infoSpriteImage.sprite = infoPiece.GetComponent<SpriteRenderer>().sprite;
        }
        else
            infoPanel.SetActive(false);

        ultimateButton.gameObject.SetActive(selectedPiece != null && selectedPiece.CanUseUltimate());
    }
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

    private void ShowRoll(int value)
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
        targetMode = true;

        // highlight only targets
        foreach (var p in targets) 
            //HighlightSquare(p.Row, p.Col, Color.yellow);

        pendingTargetCallback = p =>
        {
            // only accept clicks on listed targets
            if (!targetMode) return;
            if (p != null && targets.Contains(p))
            {
                onChosen?.Invoke(p);
                //ClearHighlights();
                targetMode = false;
                pendingTargetCallback = null;
            }
            else
            {
                // clicked elsewhere -> cancel
                //ClearHighlights();
                targetMode = false;
                pendingTargetCallback = null;
                onChosen?.Invoke(null);          // signal cancel
            }
        };
    }

    public void SetMoveRelay(MoveRelay relay)
    {
        _moveRelay = relay;
    }

}
