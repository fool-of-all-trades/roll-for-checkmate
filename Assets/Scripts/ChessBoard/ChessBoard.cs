using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using Pieces;
using Utils;
using Controller;
using TMPro;
using UnityEngine.Tilemaps;
using UnityEngine.EventSystems;

public class ChessBoard : MonoBehaviour
{
    public static ChessBoard Instance { get; private set; }

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

    [Header("References")]
    public GameController gameController;
    public Button ultimateButton;

    [Header("UI Elements")]
    // public Image diceImage;
    public TMP_Text rollText;
    public GameObject infoPanel;
    public TMP_Text infoNameText;
    public TMP_Text infoTeamText;
    public TMP_Text infoLevelText;
    public Image infoSpriteImage;

    [Header("Board Highlights")]
    public Color highlightColor;
    public Color whiteSacredRoadColor;
    public Color blackSacredRoadColor;

    [Header("Board & Highlights")]
    [SerializeField] private Tilemap highlightTilemap;
    [SerializeField] private Tilemap whiteSacredRoadTilemap;
    [SerializeField] private Tilemap blackSacredRoadTilemap;

    private Vector2Int enPassantTile = new Vector2Int(-1, -1);
    private Piece selectedPiece;
    private Piece infoPiece;
    private int roll;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;
        else
        {
            Destroy(gameObject);
            return;
        }
        gameController = GameController.Instance;
    }

    private void Start()
    {
        Debug.Log("[ChessBoard] Start() firing, ultimateButton = " + (ultimateButton != null));
        AddPieces();
        ultimateButton.onClick.AddListener(OnUltimateButtonClicked);
        ultimateButton.gameObject.SetActive(false);
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

    #region Highlights
    /// <summary>
    /// Clears all three overlay layers.
    /// </summary>
    public void ClearHighlights()
    {
        highlightTilemap.ClearAllTiles();
        whiteSacredRoadTilemap.ClearAllTiles();
        blackSacredRoadTilemap.ClearAllTiles();
    }

    /// <summary>
    /// TODO: Highlights the given tile
    /// </summary>
    public void ShowSacredRoads(List<Vector2Int> whiteRoad, List<Vector2Int> blackRoad)
    {

        Debug.Log($"[Highlights] ShowSacredRoads called — whiteRoad:{whiteRoad.Count}, blackRoad:{blackRoad.Count}");


    }
    #endregion

    // Yeah I'll get to that soon, said on 07.07.2025
    #region IBoardContext Implementation
    public bool IsValidPosition(int row, int col)
    {
        return row >= 0 && row < 8 && col >= 0 && col < 8;
    }

    public Piece GetPieceAt(int row, int col)
    {
        foreach (var p in pieces)
            if (p.Row == row && p.Col == col)
                return p;
        return null;
    }

    public Vector2Int EnPassantTile => enPassantTile;
    #endregion

    /// <summary>
    /// Converts grid coordinates (row, col) into world‐space position
    /// so your pieces land on the correct tile.
    /// </summary>
    public Vector3 GridToWorld(int row, int col)
    {
        Vector3Int cellPos = new Vector3Int(col, row, 0);
        return boardTilemap.GetCellCenterWorld(cellPos);
    }

    /// <summary>
    /// Returns true if the piece’s move is valid *and* doesn’t leave its king in check.
    /// </summary>
    public bool CanMove(Piece piece, int destRow, int destCol)
    {
        if (!piece.IsValidMove(destRow, destCol))
            return false;

        // Assuming you have a reference to your GameController:
        return !gameController.CheckCheck(piece, destRow, destCol);
    }

    /// <summary>
    /// Remove a piece from the board’s piece list.
    /// </summary>
    public void RemovePiece(Piece piece)
    {
        pieces.Remove(piece);
        Destroy(piece.gameObject);
    }

    public void HideCapturedPiece(Piece piece)
    {
        pieces.Remove(piece);
        piece.gameObject.SetActive(false);
    }

    /// <summary>
    /// Add a piece back into the board’s piece list.
    /// </summary>
    public void AddPiece(Piece piece)
    {
        pieces.Add(piece);
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

    private void HandleClick(int row, int col)
    {
        var clickedPiece = GetPieceAt(row, col);

        if (selectedPiece == null)
            SelectPiece(row, col);
        else if (clickedPiece != null && clickedPiece.Team == selectedPiece.Team)
            SelectPiece(row, col);
        else
            AttemptMove(selectedPiece, row, col, clickedPiece);
    }

    private void SelectPiece(int row, int col)
    {
        var p = GetPieceAt(row, col);
        if (p != null && p.Team == gameController.IsWhiteTurn)
        {
            selectedPiece = p;
            infoPiece = p;
        }
        else
            infoPiece = p;
    }

    private void AttemptMove(Piece piece, int destRow, int destCol, Piece destPiece)
    {
        if (!piece.IsValidMove(destRow, destCol) || gameController.CheckCheck(piece, destRow, destCol))
        {
            selectedPiece = null;
            infoPiece = destPiece;
            return;
        }

        // Handle captures and duels
        if (destPiece != null && destPiece.Team != piece.Team)
        {
            if (HandleCaptureOrDuel(piece, destPiece))
            {
                selectedPiece = null;
                return;
            }
        }

        // Sacred road for Bishop
        if (piece is Bishop)
        {
            var path = ComputePath(piece.Row, piece.Col, destRow, destCol);
            gameController.ActivateSacredRoad(piece, path, piece.Team);
        }

        // En Passant for Pawn
        if (piece is Pawn)
        {
            int direction = piece.Team ? 1 : -1;
            if (Mathf.Abs(piece.Row - destRow) == 2)
                enPassantTile = new Vector2Int(destRow - direction, destCol);
            else if (destRow == enPassantTile.x && destCol == enPassantTile.y)
            {
                var captured = GetPieceAt(destRow - direction, destCol);
                if (captured != null && HandleCaptureOrDuel(piece, captured))
                {
                    selectedPiece = null;
                    return;
                }
                enPassantTile = new Vector2Int(-1, -1);
            }
            else
                enPassantTile = new Vector2Int(-1, -1);
        }
        else
            enPassantTile = new Vector2Int(-1, -1);

        // Castling for King
        if (piece is King && Mathf.Abs(destCol - piece.Col) == 2)
        {
            int dir = (destCol - piece.Col) > 0 ? 1 : -1;
            int rookOrigCol = dir > 0 ? 7 : 0;
            var rook = GetPieceAt(piece.Row, rookOrigCol) as Rook;
            if (rook != null)
            {
                MovePiece(rook, piece.Row, piece.Col + dir);
                rook.MarkMoved();
            }
        }

        // Finalize move
        piece.MarkMoved();
        MovePiece(piece, destRow, destCol);

        gameController.ProcessSacredCrossing(piece, this);
        gameController.ToggleTurn();
        if (gameController.IsGameOver(piece))
        {
            Debug.Log("Game over");
            //gameController.EndGame();
        }


        selectedPiece = null;
        infoPiece = null;
    }

    private bool HandleCaptureOrDuel(Piece attacker, Piece defender)
    {
        Debug.Log($"[Capture] {attacker.Name} {attacker.Team} (lvl {attacker.Level}) -> {defender.Name} (lvl {defender.Level})");

        // if defender ≤ attacker → auto-capture
        if (defender.Level <= attacker.Level)
        {
            Debug.Log("[Capture] Defender level ≤ attacker → auto-capture");
            gameController.CapturePiece(defender, attacker);
            return false;
        }

        // defender > attacker → duel
        roll = Dice.Roll(10);
        Debug.Log($"[Capture] Duel roll = {roll}");

        if (gameController.Duel(roll, attacker, defender))
        {
            Debug.Log("[Capture] Attacker won the duel");
            gameController.CapturePiece(defender, attacker);
            return false;
        }
        else
        {
            Debug.Log("[Capture] Attacker lost the duel");
            gameController.CapturePiece(attacker, defender);
            gameController.ToggleTurn();
            return true;
        }
    }




    private void MovePiece(Piece p, int row, int col)
    {
        p.SetGridPosition(row, col);
    }

    private List<Vector2Int> ComputePath(int fromRow, int fromCol, int toRow, int toCol)
    {
        var path = new List<Vector2Int> { new Vector2Int(fromRow, fromCol) };
        int dr = Math.Sign(toRow - fromRow);
        int dc = Math.Sign(toCol - fromCol);
        int r = fromRow + dr, c = fromCol + dc;
        while (r != toRow || c != toCol)
        {
            path.Add(new Vector2Int(r, c));
            r += dr;
            c += dc;
        }
        return path;
    }

    private void OnUltimateButtonClicked()
    {
        Debug.Log("[UI] Ultimate button clicked!");
        if (selectedPiece != null)
        {
            Debug.Log($"[UI] selectedPiece = {selectedPiece.Name} – calling UseUltimateAbility");
            selectedPiece.UseUltimateAbility(gameController);
        }
        else
        {
            Debug.Log("[UI] selectedPiece is null");
        }

    }

    private void UpdateUI()
    {
        rollText.text = "Roll: " + roll.ToString();
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
}
