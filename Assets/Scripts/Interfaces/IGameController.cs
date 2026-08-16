using System.Collections.Generic;
using Pieces;
using System;
using UnityEngine;

/// <summary>
/// Facade for chess-rule logic. Exposes just enough for
/// the view layer (ChessBoard) to drive gameplay without
/// knowing any implementation details.
/// </summary>
public interface IGameController
{
    void Initialize(IEnumerable<Piece> pieces);

    event Action<MoveResult> OnMoveAccepted;

    event Action<int> OnDuelRolled;

    event Action<IReadOnlyList<Vector2Int>, IReadOnlyList<Vector2Int>> OnRoadsChanged;

    bool TryMove(Piece piece, int toRow, int toCol);

    bool TryUseUltimate(Piece piece);

    bool TryUseKnightUltimate(Piece knightPiece, Piece target);

    bool TryUseRookUltimate(Piece rookPiece, Piece target);

    bool TryRelocate(Piece piece, int toRow, int toCol);

    Piece PieceAt(int row, int col);

    public bool CheckCheck(Piece piece, int destRow, int destCol);

    public bool IsGameOver(Piece lastMovedPiece);
    
    public bool ResurrectPawn(bool team);
    public bool ResurrectPiece(bool team);
    public Vector2Int? GetResurrectionSquare(bool team);


    // REMOVE LATER
    bool IsWhiteTurn { get; }
    public void CapturePiece(Piece captured, Piece winner);
}
