// Immutable “data packet” that GameControllerMono raises
// and ChessBoard listens to. No methods mutate state:
// all board updates have already happened before the event fires.
using Pieces;

/// <summary>
/// Describes a single accepted move.
/// </summary>
public readonly struct MoveResult
{
    public Piece Piece { get; }   // piece that moved
    public int FromRow { get; }
    public int FromCol { get; }
    public int ToRow { get; }
    public int ToCol { get; }
    public Piece Captured { get; }   // null if no capture

    public MoveResult(
        Piece piece,
        int fromRow, int fromCol,
        int toRow, int toCol,
        Piece captured = null)
    {
        Piece = piece;
        FromRow = fromRow;
        FromCol = fromCol;
        ToRow = toRow;
        ToCol = toCol;
        Captured = captured;
    }

    public override string ToString() =>
        $"{Piece.name}: ({FromRow},{FromCol}) → ({ToRow},{ToCol})"
        + (Captured ? $" capturing {Captured.name}" : string.Empty);
}