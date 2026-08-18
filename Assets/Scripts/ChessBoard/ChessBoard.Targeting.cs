using System;
using System.Collections.Generic;
using Pieces;

public partial class ChessBoard
{
    private bool IsTargetSelectionActive => _inputMode == BoardInputMode.TargetSelection;
    private bool _targetSelectionAllowsCancel = true;

    public void BeginTargetSelection(
        IReadOnlyList<Piece> targets,
        Action<Piece> onChosen)
    {
        EnterTargetSelection(targets, onChosen, allowCancel: true);

        // highlight only targets
        foreach (var p in targets)
        {
            //HighlightSquare(p.Row, p.Col, Color.yellow);
        }
    }

    public void BeginMandatoryTargetSelection(
        IReadOnlyList<Piece> targets,
        Action<Piece> onChosen)
    {
        selectedPiece = null;
        infoPiece = null;
        EnterTargetSelection(targets, onChosen, allowCancel: false);
        UpdateUI();
    }

    private void EnterTargetSelection(
        IReadOnlyList<Piece> targets,
        Action<Piece> onChosen,
        bool allowCancel)
    {
        pendingTargetChoices = targets;
        pendingTargetCallback = onChosen;
        _targetSelectionAllowsCancel = allowCancel;
        _inputMode = BoardInputMode.TargetSelection;
    }

    private void ExitTargetSelection()
    {
        //ClearHighlights();
        _inputMode = BoardInputMode.Normal;
        pendingTargetChoices = null;
        pendingTargetCallback = null;
        _targetSelectionAllowsCancel = true;
    }

    private void CancelTargetSelection()
    {
        if (!_targetSelectionAllowsCancel) return;
        CompleteTargetSelection(null);
    }

    private void CompleteTargetSelection(Piece target)
    {
        if (!IsTargetSelectionActive) return;

        var callback = pendingTargetCallback;
        ExitTargetSelection();
        callback?.Invoke(target);
    }
}
