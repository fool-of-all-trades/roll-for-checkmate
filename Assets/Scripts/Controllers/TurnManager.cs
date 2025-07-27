using Pieces;

public class TurnManager
{
    readonly bool whiteStarts = true;

    public bool WhiteTurn { get; private set; }

    /// <summary>Square eligible for en passant capture (can be null).</summary>
    public (int row, int col)? EnPassantSquare { get; private set; }

    /// <summary>Pawn that just moved two squares, enabling en passant.</summary>
    public Pawn LastDoubleStepPawn { get; private set; }

    public TurnManager() =>  (WhiteTurn) = (whiteStarts);

    public void ToggleTurn()
    {
        bool sideThatJustMoved = WhiteTurn;

        WhiteTurn = !WhiteTurn;

        // If the en‑passant square was created by the side that just moved,
        // the *next* toggle (after the opponent’s reply) should clear it.
        if (LastDoubleStepPawn != null && LastDoubleStepPawn.Team == WhiteTurn)
        {
            EnPassantSquare = null;
            LastDoubleStepPawn = null;
        }
    }

    public void SetEnPassant(int row, int col, Pawn pawn)
    {
        EnPassantSquare = (row, col);
        LastDoubleStepPawn = pawn;
    }
}
