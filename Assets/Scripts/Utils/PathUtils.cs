using System;
using Pieces;

public static class PathUtils
{
    public static bool PathClearExceptEndpoints(Func<int, int, Piece> pieceAt, int row, int fromCol, int toCol)
    {
        int dir = Math.Sign(toCol - fromCol);
        for (int c = fromCol + dir; c != toCol; c += dir)
        {
            if (pieceAt(row, c) != null) return false;
        }
        return true;
    }

    public static bool PathClearDiagonal(Func<int, int, Piece> pieceAt, int fromRow, int fromCol, int toRow, int toCol)
    {
        int dr = Math.Sign(toRow - fromRow);
        int dc = Math.Sign(toCol - fromCol);
        int r = fromRow + dr;
        int c = fromCol + dc;
        while (r != toRow && c != toCol)
        {
            if (pieceAt(r, c) != null) return false;
            r += dr; c += dc;
        }
        return true;
    }

    public static bool PathClearStraight(Func<int, int, Piece> pieceAt, int fromRow, int fromCol, int toRow, int toCol)
    {
        int dr = Math.Sign(toRow - fromRow);
        int dc = Math.Sign(toCol - fromCol);
        int r = fromRow + dr;
        int c = fromCol + dc;
        while (r != toRow || c != toCol)
        {
            if (pieceAt(r, c) != null) return false;
            r += dr; c += dc;
        }
        return true;
    }
}