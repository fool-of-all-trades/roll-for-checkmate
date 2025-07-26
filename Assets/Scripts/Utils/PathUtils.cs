using System;
using Pieces;

public static class PathUtils
{
    /// <summary>
    /// Determines if the squares between king and rook are empty.
    /// Used for King's castling move.
    /// </summary>
    /// <returns>True if path is clear</returns>
    public static bool PathClearExceptEndpoints(Func<int, int, Piece> pieceAt, int row, int fromCol, int toCol)
    {
        int dir = Math.Sign(toCol - fromCol);
        for (int c = fromCol + dir; c != toCol; c += dir)
        {
            if (pieceAt(row, c) != null) return false;
        }
        return true;
    }

    /// <summary>
    /// Determines if the diagonal path from (fromRow, fromCol) to (toRow, toCol) is clear.
    /// Used for Bishop's and Queen's diagonal moves.
    /// </summary>
    /// <returns>True if path is clear</returns>
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

    /// <summary>
    /// Determines if the straight path from (fromRow, fromCol) to (toRow, toCol) is clear. 
    /// Used for Rook's and Queen's diagonal moves.
    /// </summary>
    /// <returns>True if path is clear</returns>
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