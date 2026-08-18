using Pieces;
using Pieces;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public partial class ChessBoard
{
    private TMP_Text _ultimateButtonLabel;
    private string _ultimateButtonDefaultLabel;

    private void OnUltimateButtonClicked()
    {
        Debug.Log("[UI] Ultimate button clicked!");

        if (IsAscensionMoveActive)
        {
            if (_ascensionMovePiece != null && TryGetMoveRelay(out var relay))
                relay.SendDeclineAscensionMove(_ascensionMovePiece);

            return;
        }

        if (selectedPiece == null)
        {
            Debug.Log("[UI] selectedPiece is null");
            return;
        }

        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsServer)
        {
            Debug.Log($"[UI] selectedPiece = {selectedPiece.Name} “ calling TryUseUltimate");
            controller.TryUseUltimate(selectedPiece);
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

        UpdateUltimateButtonPresentation();
    }

    private void UpdateUltimateButtonPresentation()
    {
        if (_ultimateButtonLabel == null)
        {
            _ultimateButtonLabel = ultimateButton.GetComponentInChildren<TMP_Text>();
            if (_ultimateButtonLabel != null)
                _ultimateButtonDefaultLabel = _ultimateButtonLabel.text;
        }

        if (_ultimateButtonLabel != null)
            _ultimateButtonLabel.text = IsAscensionMoveActive
                ? "End Turn"
                : _ultimateButtonDefaultLabel;

        ultimateButton.gameObject.SetActive(
            IsAscensionMoveActive ||
            (selectedPiece != null && selectedPiece.CanUseUltimate()));
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
            infoTurnText.text = "Waiting¦";
            return;
        }

        var myTeam = PlayerTeams.GetTeam(nm.LocalClientId);
        if (myTeam == TeamSide.None)
        {
            infoTurnText.text = "Waiting for seat¦";
            return;
        }

        bool whiteTurn = TurnSync.Instance.WhiteTurn.Value;
        bool myWhite = (myTeam == TeamSide.White);
        bool myTurn = (myWhite == whiteTurn);

        // Show both who's turn and can I move
        string who = whiteTurn ? "White" : "Black";
        infoTurnText.text = myTurn ? $"Your turn ({who})" : $"Opponent's turn ({who})";
    }

    public void ShowPawnAscensionPrompt()
    {
        if (infoTurnText != null)
            infoTurnText.text = "Choose who to bless";
    }

    private void ShowAscensionMovePrompt()
    {
        if (infoTurnText != null)
            infoTurnText.text = "Move again or end turn";
    }

    public void ShowRoll(int value)
    {
        rollText.text = $"Rolled: {value}";
    }
}
