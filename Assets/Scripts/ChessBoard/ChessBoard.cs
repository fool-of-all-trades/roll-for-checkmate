using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using Pieces;
using Utils;
using TMPro;
using UnityEngine.Tilemaps;
using UnityEngine.EventSystems;

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

    [Header("References")]
    [SerializeField] private MonoBehaviour controllerRoot; 
    private IGameController controller;

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
        AddPieces();
        controller.Initialize(pieces);
        ultimateButton.onClick.AddListener(OnUltimateButtonClicked);
        ultimateButton.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (controller != null)
        {
            controller.OnMoveAccepted -= ApplyMoveVisuals;
            controller.OnDuelRolled -= ShowRoll;
        }
    }


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

    public Piece GetPieceAt(int row, int col)
    {
        foreach (var p in pieces)
            if (p.Row == row && p.Col == col)
                return p;
        return null;
    }


    public Vector3 GridToWorld(int row, int col)
    {
        Vector3Int cellPos = new Vector3Int(col, row, 0);
        return boardTilemap.GetCellCenterWorld(cellPos);
    }

    public void HideCapturedPiece(Piece piece)
    {
        pieces.Remove(piece);
        piece.gameObject.SetActive(false);
    }

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

    private void SpawnPiece(GameObject prefab, int row, int col, bool team)
    {
        Vector3 worldPos = GridToWorld(row, col);
        // make the tilemap your parent so the board’s transform/origin “carries along” the piece:
        var go = Instantiate(prefab, worldPos, Quaternion.identity, boardTilemap.transform);

        var piece = go.GetComponent<Piece>();
        piece.Init(this, row, col, team);
        pieces.Add(piece);
    }

    // Called automatically whenever GameControllerMono accepts a legal move
    private void ApplyMoveVisuals(MoveResult m)
    {
        // move the winner’s prefab
        MovePiece(m.Piece, m.ToRow, m.ToCol);

        // hide the loser if there was a capture
        if (m.Captured != null) HideCapturedPiece(m.Captured);

        UpdateUI();
    }

    private void SelectPiece(int row, int col)
    {
        var p = GetPieceAt(row, col);
        // You may keep the side-to-move filter, or let controller reject out‑of‑turn clicks.
        if (p != null && p.Team == controller.IsWhiteTurn)
            selectedPiece = p;

        infoPiece = p;        // Still update the info panel
    }

    private void AttemptMove(int destRow, int destCol)
    {
        if (selectedPiece == null) return;

        // Let the controller decide legality, capture, duels, en‑passant, castling, promotion.
        controller.TryMove(selectedPiece, destRow, destCol);

        selectedPiece = null;
        infoPiece = null;
    }

    private void HandleClick(int row, int col)
    {
        var clickedPiece = GetPieceAt(row, col);

        if (selectedPiece == null)
            SelectPiece(row, col);
        else if (clickedPiece != null && clickedPiece.Team == selectedPiece.Team)
            SelectPiece(row, col);
        else
            AttemptMove(row, col);   // ← pass only coordinates now
    }

    private void MovePiece(Piece p, int row, int col)
    {
        p.SetGridPosition(row, col);
    }

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

    private void ShowRoll(int value)
    {
        rollText.text = $"Rolled: {value}";
    }

    void ShowRoads(IReadOnlyList<Vector2Int> white, IReadOnlyList<Vector2Int> black)
    {
        
        // paint tiles, show highlights for sacred road
    }

}
