using Pieces;

public interface ICurseService
{
    void ApplyCurse(Piece piece, int turns);   
    void TickTurn();                           // called once per turn switch
    int RollModifier(Piece piece);             // e.g. -3 if cursed
    bool IsCursed(Piece piece);
}
