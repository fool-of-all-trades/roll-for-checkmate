///// <summary>
///// Minimal interface exposing only the methods and properties
///// needed by Piece-derived classes to validate moves.
///// </summary>
//public interface IBoardContext
//{
//    /// <summary>
//    /// Returns true if (row, col) is within the 8x8 board limits.
//    /// </summary>
//    bool IsValidPosition(int row, int col);

//    /// <summary>
//    /// Returns the Piece at the given grid coordinates, or null if empty.
//    /// </summary>
//    Piece GetPieceAt(int row, int col);

//    /// <summary>
//    /// The current en passant target tile (x=row, y=col), or (-1, -1) if none.
//    /// </summary>
//    UnityEngine.Vector2Int EnPassantTile { get; }
//}
