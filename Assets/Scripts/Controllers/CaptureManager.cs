using Pieces;
using System.Collections.Generic;

public class CaptureManager
{
    readonly List<Piece> whiteCaptured = new();
    readonly List<Piece> blackCaptured = new();

    /// <summary>
    /// Returns the list of captured pieces for the specified team - needed for Bishop's ultimate.
    /// </summary>
    public List<Piece> GetCapturedList(bool team)
    => team ? whiteCaptured : blackCaptured;

    /// <summary>
    /// Captures a piece, removing it from the pieces list and adding it to the captured list.
    /// The object is also deactivated in the scene, but not destroyed.
    /// Also the winner's level is updated based on the captured piece type, with limit of max level 5.
    /// </summary>
    public void CapturePiece(List<Piece> pieces, Piece captured, Piece winner)
    {
        if (captured == null) return;

        pieces.Remove(captured);
        captured.gameObject.SetActive(false);

        if (captured.Team) whiteCaptured.Add(captured);
        else blackCaptured.Add(captured);

        if (winner.Level < 5)
        {
            winner.UpdateLevel(captured is Pawn ? 1 : 2);
            if (winner.Level > 5)
                winner.UpdateLevel(5 - winner.Level);
        }
    }
}
