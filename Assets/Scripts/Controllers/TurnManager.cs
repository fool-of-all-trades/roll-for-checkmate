using Pieces;

public class TurnManager
{
    readonly bool whiteStarts = true;
    public bool WhiteTurn { get; private set; }

    public (int row, int col)? EnPassantSquare { get; private set; }
    public Pawn LastDoubleStepPawn { get; private set; }

    public TurnManager() =>  (WhiteTurn) = (whiteStarts);

    public void ToggleTurn()
    {
        bool sideThatJustMoved = WhiteTurn;

        WhiteTurn = !WhiteTurn;


        // If the en‑passant square was created by the side that just moved,
        // the *next* toggle (i.e. after the opponent’s reply) should clear it.
        if (LastDoubleStepPawn != null &&
            LastDoubleStepPawn.Team == WhiteTurn)    // we’re back to the creator’s turn
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
