using Controller;
using Pieces;
using System;
using System.Collections.Generic;

public class TurnManager
{
    readonly bool whiteStarts = true;

    public bool WhiteTurn { get; private set; }

    /// <summary>Square eligible for en passant capture (can be null).</summary>
    public (int row, int col)? EnPassantSquare { get; private set; }

    /// <summary>Pawn that just moved two squares, enabling en passant.</summary>
    public Pawn LastDoubleStepPawn { get; private set; }

    public TurnManager()
    {
        ResetForNewMatch();
    }

    public void ResetForNewMatch()
    {
        WhiteTurn = whiteStarts;
        ClearEnPassant();
    }

    public void ToggleTurn()
    {
        bool sideThatJustMoved = WhiteTurn;

        WhiteTurn = !WhiteTurn;

        // If the en‑passant square was created by the side that just moved,
        // the *next* toggle (after the opponent’s reply) should clear it.
        if (LastDoubleStepPawn != null && LastDoubleStepPawn.Team == WhiteTurn)
            ClearEnPassant();
    }

    public void SetEnPassant(int row, int col, Pawn pawn)
    {
        EnPassantSquare = (row, col);
        LastDoubleStepPawn = pawn;
    }

    public void ClearEnPassant()
    {
        EnPassantSquare = null;
        LastDoubleStepPawn = null;
    }

    public bool TryGetValidEnPassantVictim(
        Pawn capturingPawn,
        int targetRow,
        int targetCol,
        Func<int, int, Piece> pieceAt,
        out Pawn victim)
    {
        victim = null;

        if (capturingPawn == null || pieceAt == null || !EnPassantSquare.HasValue)
            return false;

        var square = EnPassantSquare.Value;
        if (square.row != targetRow || square.col != targetCol)
            return false;
        if (pieceAt(targetRow, targetCol) != null)
            return false;

        var candidate = LastDoubleStepPawn;
        if (candidate == null || !candidate.gameObject.activeInHierarchy)
            return false;
        if (candidate.Team == capturingPawn.Team)
            return false;

        var networkPiece = candidate.GetComponent<NetworkPiece>();
        if (networkPiece != null &&
            (networkPiece.IsCaptured.Value || networkPiece.IsAscended.Value))
            return false;

        int direction = capturingPawn.Team ? 1 : -1;
        if (targetRow - capturingPawn.Row != direction ||
            Math.Abs(targetCol - capturingPawn.Col) != 1)
            return false;

        int expectedVictimRow = targetRow - direction;
        if (candidate.Row != expectedVictimRow || candidate.Col != targetCol)
            return false;
        if (pieceAt(expectedVictimRow, targetCol) != candidate)
            return false;

        victim = candidate;
        return true;
    }
}
