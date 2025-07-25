using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Pieces;

public class ResurrectionService : MonoBehaviour, IResurrectionService
{
    // Dependencies injected
    System.Func<int, int, Piece> pieceAt;
    System.Action<Piece, int, int> addPieceToBoard;
    CaptureManager captureMgr;

    const int BOARD_MIN = 0, BOARD_MAX = 7;

    public void Init(System.Func<int, int, Piece> pieceAt,
                     System.Action<Piece, int, int> addPieceToBoard,
                     CaptureManager captureMgr)
    {
        this.pieceAt = pieceAt;
        this.addPieceToBoard = addPieceToBoard;
        this.captureMgr = captureMgr;
    }

    public Vector2Int? GetResurrectionSquare(bool team)
    {
        int backRank = team ? 0 : 7;
        int rowMin = team ? 0 : 4;
        int rowMax = team ? 3 : 7;
        var candidates = new List<Vector2Int>();

        for (int r = rowMin; r <= rowMax; r++)
        {
            for (int c = BOARD_MIN; c <= BOARD_MAX; c++)
            {
                if (pieceAt(r, c) == null)
                    candidates.Add(new Vector2Int(r, c));
            }
        }

        if (candidates.Count == 0) return null;

        candidates.Sort((a, b) =>
        {
            int da = Mathf.Abs(a.x - backRank);
            int db = Mathf.Abs(b.x - backRank);
            if (da != db) return da.CompareTo(db);

            float ca = Mathf.Abs(a.y - 3.5f);
            float cb = Mathf.Abs(b.y - 3.5f);
            return ca.CompareTo(cb);
        });

        return candidates[0];
    }

    public bool ResurrectPawn(bool team)
    {
        var sq = GetResurrectionSquare(team);
        if (!sq.HasValue) return false;

        var pool = captureMgr.GetCapturedList(team);
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] is Pawn pawn)
            {
                pool.RemoveAt(i);
                RevivePiece(pawn, team, sq.Value.x, sq.Value.y);
                return true;
            }
        }
        return false;
    }

    public bool ResurrectPiece(bool team)
    {
        var sq = GetResurrectionSquare(team);
        if (!sq.HasValue) return false;

        var pool = captureMgr.GetCapturedList(team);
        var nonPawns = pool.Where(p => p is not Pawn).ToList();

        if (nonPawns.Count > 0)
        {
            var piece = nonPawns[Random.Range(0, nonPawns.Count)];
            pool.Remove(piece);
            RevivePiece(piece, team, sq.Value.x, sq.Value.y);
            return true;
        }

        Debug.Log("No non-pawn to resurrect, trying pawn...");
        return ResurrectPawn(team);
    }

    void RevivePiece(Piece piece, bool team, int r, int c)
    {
        piece.ChangeTeam(team);
        piece.gameObject.SetActive(true);
        
        // We do NOT call SetGridPosition (that uses ChessBoard). Use controller’s addPieceToBoard.
        addPieceToBoard(piece, r, c);
        Debug.Log($"{piece.Name} resurrected at ({r},{c}) for {(team ? "White" : "Black")}");
    }
}
