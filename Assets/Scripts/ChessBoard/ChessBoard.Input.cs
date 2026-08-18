using Pieces;
using Pieces;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

public partial class ChessBoard
{
    private Piece _ascensionMovePiece;
    private bool IsAscensionMoveActive => _inputMode == BoardInputMode.AscensionMove;

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

        if (IsAscensionMoveActive)
        {
            selectedPiece = _ascensionMovePiece;
            infoPiece = _ascensionMovePiece;

            if (clickedPiece != _ascensionMovePiece)
                AttemptMove(row, col);

            return;
        }

        if (selectedPiece == null)
            SelectPiece(row, col);
        else if (clickedPiece != null && clickedPiece.Team == selectedPiece.Team)
            SelectPiece(row, col);
        else
            AttemptMove(row, col);
    }

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
        if (IsAscensionMoveActive)
        {
            selectedPiece = _ascensionMovePiece;
            infoPiece = _ascensionMovePiece;
        }
        else
        {
            selectedPiece = null;
            infoPiece = null;
        }
    }

    public void BeginAscensionMove(Piece piece)
    {
        if (piece == null) return;

        ExitTargetSelection();
        _ascensionMovePiece = piece;
        selectedPiece = piece;
        infoPiece = piece;
        _inputMode = BoardInputMode.AscensionMove;
        ShowAscensionMovePrompt();
        UpdateUI();
    }

    private void ExitAscensionMoveMode()
    {
        if (!IsAscensionMoveActive) return;

        _inputMode = BoardInputMode.Normal;
        _ascensionMovePiece = null;
        selectedPiece = null;
        infoPiece = null;
        UpdateUI();
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
        if (np && (np.IsCaptured.Value || np.IsAscended.Value)) return false;

        if (!TryGetLocalTeam(out var localTeam)) return false;
        if (!IsLocalPlayersTurn(localTeam)) return false;

        bool localTeamIsWhite = (localTeam == TeamSide.White);
        return piece.Team == localTeamIsWhite;
    }
}
