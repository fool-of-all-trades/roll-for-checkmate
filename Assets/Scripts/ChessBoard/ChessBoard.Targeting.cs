using System;
using System.Collections.Generic;
using Pieces;

public partial class ChessBoard
{
    private bool IsTargetSelectionActive => _inputMode == BoardInputMode.TargetSelection;

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
}
