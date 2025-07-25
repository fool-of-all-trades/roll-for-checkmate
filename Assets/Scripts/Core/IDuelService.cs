using Pieces;
public interface IDuelService
{
    /// <summary>
    /// Returns <c>true</c> if the attacker wins the duel and captures,
    /// <c>false</c> if the defender wins and the attacker is captured.
    /// Implementations should encapsulate the dice roll and any UI feedback.
    /// </summary>
    bool ResolveDuel(Piece attacker, Piece defender, out int roll);
}
