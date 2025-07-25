using Pieces;
using UnityEngine;

public interface IResurrectionService
{
    /// Returns a good square to spawn a resurrected unit, or null if none.
    Vector2Int? GetResurrectionSquare(bool team);

    /// Tries to resurrect a pawn of 'team' from the captured pool. Returns true on success.
    bool ResurrectPawn(bool team);

    /// Tries to resurrect any non-pawn piece of 'team'; falls back to pawn. Returns true on success.
    bool ResurrectPiece(bool team);

    void Init(System.Func<int, int, Piece> pieceAt,
                     System.Action<Piece, int, int> addPieceToBoard,
                     CaptureManager captureMgr);
}
