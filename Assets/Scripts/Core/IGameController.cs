using System.Collections.Generic;
using Pieces;
using System;

/// <summary>
/// Facade for chess-rule logic.  Exposes just enough for
/// the view layer (ChessBoard) to drive gameplay without
/// knowing any implementation details.
/// </summary>
public interface IGameController
{
    /// <summary>
    /// Raised *after* a legal move is executed and the internal
    /// board array has been updated.
    /// </summary>
    event Action<MoveResult> OnMoveAccepted;

    event Action<int> OnDuelRolled;

    /// <summary>
    /// Attempts to move <paramref name="piece"/> to the target
    /// square.  Returns true if the move is legal and applied;
    /// false if it is rejected.
    /// </summary>
    bool TryMove(Piece piece, int toRow, int toCol);

    /// <summary>
    /// Read-only peek at the piece currently occupying the square
    /// (or null if empty).  Useful for selection and highlighting.
    /// </summary>
    Piece PieceAt(int row, int col);

    /// <summary>
    /// Initializes the game controller with a collection of pieces.
    /// </summary>
    void Initialize(IEnumerable<Piece> pieces);

    public bool CheckCheck(Piece piece, int destRow, int destCol);

    public bool IsGameOver(Piece lastMovedPiece);


    // REMOVE LATER
    bool IsWhiteTurn { get; }
    public void CapturePiece(Piece captured, Piece winner);


    /// Relocates a piece without checking legality (used by ultimates).
    /// Returns false if the destination square is occupied.
    bool TryRelocate(Piece piece, int toRow, int toCol);
}
