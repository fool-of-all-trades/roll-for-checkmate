using Pieces;
using UnityEngine;

public interface IResurrectionService
{
    Vector2Int? GetResurrectionSquare(bool team);

    bool ResurrectPawn(bool team);

    bool ResurrectPiece(bool team);

    void Init(System.Func<int, int, Piece> pieceAt,
                     System.Action<Piece, int, int> addPieceToBoard,
                     CaptureManager captureMgr);
}
