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
using Unity.Netcode;


/// <summary>
/// Renders the board, spawns pieces, handles input, and updates UI.
/// </summary>
public class ChessBoard : MonoBehaviour
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

    #region Unity Lifecycle

    private void Awake()
    {
        // subscribe to controller events
        controller = (IGameController)controllerRoot; 

        controller.OnMoveAccepted += ApplyMoveVisuals;
        controller.OnDuelRolled += ShowRoll;
        controller.OnRoadsChanged += ShowRoads;
    }

    private void Start()
    {
        ultimateButton.onClick.AddListener(OnUltimateButtonClicked);
        ultimateButton.gameObject.SetActive(false);

        if (NetworkManager.Singleton.IsHost)
        {
            AddPieces();
            controller.Initialize(pieces);   // white = host
        }
        else
        {
            StartCoroutine(WaitForBoard());
        }
    }

    private IEnumerator WaitForBoard()
    {
        // Wait until the server has spawned everything
        while (pieces.Count < 32) yield return null;
        controller.Initialize(pieces);       // black = client
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
        netPiece.InitNetwork(team);

        var netObj = go.GetComponent<NetworkObject>();
        netObj.Spawn(true); // host-owned; replicates to all clients

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
        piece.gameObject.SetActive(false);
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

        bool iAmWhite = NetworkManager.Singleton.LocalClientId == 0;
        if (p != null && p.Team == iAmWhite)
            selectedPiece = p;

        infoPiece = p;
    }

    /// <summary>
    /// Attempts to move the currently selected piece to (destRow, destCol).
    /// </summary>
    private void AttemptMove(int destRow, int destCol)
    {
        if (selectedPiece == null) return;

        // The controller decides legality, capture, duels, en‑passant, castling, promotion...
        controller.TryMove(selectedPiece, destRow, destCol);

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
    private void ApplyMoveVisuals(MoveResult m)
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

}
