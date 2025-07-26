using Pieces;
using System.Collections.Generic;

public class CaptureManager
{
    readonly List<Piece> whiteCaptured = new();
    readonly List<Piece> blackCaptured = new();

    public List<Piece> GetCapturedList(bool team)
    => team ? whiteCaptured : blackCaptured;

    public void CapturePiece(List<Piece> pieces, Piece captured, Piece winner)
    {
        if (captured == null) return;

        pieces.Remove(captured);
        captured.gameObject.SetActive(false);

        if (captured.Team) whiteCaptured.Add(captured);
        else blackCaptured.Add(captured);

        if (winner.Level < 5)
            winner.UpdateLevel(captured is Pawn ? 1 : 2);
    }
}
