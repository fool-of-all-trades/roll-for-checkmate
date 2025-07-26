using Pieces;

public interface ICurseService
{
    void ApplyCurse(Piece piece, int turns);
    void TickTurn();
    int RollModifier(Piece piece);
    bool IsCursed(Piece piece);
}
