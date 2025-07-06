using System.Collections.Generic;
using UnityEngine;
using Pieces;
using Controller;

namespace Controller
{
    /// <summary>
    /// Singleton managing turn order, captured pieces, curses, and road effects.
    /// </summary>
    public class GameController
    {
        // Singleton instance
        public static GameController Instance { get; } = new GameController();

        private bool whiteTurn;
        private readonly List<Piece> whiteCaptured;
        private readonly List<Piece> blackCaptured;
        private readonly Checker checker;

        private readonly List<Piece> cursedPieces;

        private readonly List<Vector2Int> whiteRoad;
        private Piece whiteRoadOwner;
        private int whiteRoadUses;

        private readonly List<Vector2Int> blackRoad;
        private Piece blackRoadOwner;
        private int blackRoadUses;

        /// <summary>
        /// Squares highlighted for White's sacred road when in use.
        /// </summary>
        public List<Vector2Int> WhiteRoadSquares => whiteRoadUses > 0 ? new List<Vector2Int>(whiteRoad) : new List<Vector2Int>();

        /// <summary>
        /// Squares highlighted for Black's sacred road when in use.
        /// </summary>
        public List<Vector2Int> BlackRoadSquares => blackRoadUses > 0 ? new List<Vector2Int>(blackRoad) : new List<Vector2Int>();

        private GameController()
        {
            // White always starts
            whiteTurn = true;
            whiteCaptured = new List<Piece>();
            blackCaptured = new List<Piece>();
            cursedPieces = new List<Piece>();
            checker = new Checker();

            whiteRoad = new List<Vector2Int>();
            blackRoad = new List<Vector2Int>();
        }

        /// <summary>
        /// Indicates whether it's currently White's turn.
        /// </summary>
        public bool IsWhiteTurn => whiteTurn;

        /// <summary>
        /// Toggles the turn, applies curse turn decrements and clears expired curses.
        /// </summary>
        public void ToggleTurn()
        {
            whiteTurn = !whiteTurn;

            // Decrease curse counters and remove expired curses
            for (int i = cursedPieces.Count - 1; i >= 0; i--)
            {
                var piece = cursedPieces[i];
                if (piece.CursedTurns <= 0)
                {
                    cursedPieces.RemoveAt(i);
                }
                else
                {
                    Debug.Log($"{piece.Name} is cursed for {piece.CursedTurns} more turns.");
                    piece.DecreaseCursedTurns();
                }
            }
        }

        /// <summary>
        /// Resets the entire game state and reloads the board.
        /// </summary>
        public void ResetGame()
        {
            whiteTurn = true;
            whiteCaptured.Clear();
            blackCaptured.Clear();
            cursedPieces.Clear();

            whiteRoad.Clear();
            whiteRoadOwner = null;
            whiteRoadUses = 0;

            blackRoad.Clear();
            blackRoadOwner = null;
            blackRoadUses = 0;

            // Reset the board to initial setup
            //ChessBoard.Instance.Reset();
        }

        /// <summary>
        /// Handles capturing logic, leveling, and queen's passive curse.
        /// </summary>
        public void CapturePiece(Piece captured, Piece winner)
        {
            // Track captured piece by team
            if (captured.Team) whiteCaptured.Add(captured);
            else blackCaptured.Add(captured);

            ChessBoard.Instance.RemovePiece(captured);

            // Level up winner (max level 5)
            if (winner.Level < 5)
            {
                if (captured is Pawn)
                    winner.UpdateLevel(1);
                else
                    winner.UpdateLevel(2);
            }

            // Queen passive curse: winner cursed for 3 turns (6 toggles)
            if (captured is Queen)
            {
                winner.SetCursedTurns(6);
                cursedPieces.Add(winner);
                Debug.Log($"{winner.Name} is cursed for 3 turns!");
            }
        }

        /// <summary>
        /// Returns the list of pieces captured by White.
        /// </summary>
        public List<Piece> GetWhiteCaptured() => whiteCaptured;

        /// <summary>
        /// Returns the list of pieces captured by Black.
        /// </summary>
        public List<Piece> GetBlackCaptured() => blackCaptured;

        /// <summary>
        /// Resolves a duel between two pieces based on a dice roll and passive bonuses.
        /// </summary>
        public bool Duel(int rollResult, Piece attacker, Piece defender)
        {
            Debug.Log($"Dice roll: {rollResult}");

            // Queen passive curse: attacker rolls -3 if cursed
            int queenCurse = attacker.CursedTurns > 0 ? -3 : 0;

            // Rook passive bonus: +2 if an allied rook is adjacent to attacker
            int rookBonus = 0;
            //var board = this as IBoardContext ?? ChessBoard.Instance;
            var board = ChessBoard.Instance;
            int ar = attacker.Row, ac = attacker.Col;
            Vector2Int[] rookDirs = { new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1) };
            foreach (var d in rookDirs)
            {
                int nr = ar + d.x, nc = ac + d.y;
                if (board.IsValidPosition(nr, nc))
                {
                    var neighbor = board.GetPieceAt(nr, nc);
                    if (neighbor is Rook && neighbor.Team == attacker.Team)
                    {
                        rookBonus = 2;
                        Debug.Log("Rook empowers ally! +2 to roll.");
                        break;
                    }
                }
            }

            // Knight passive bonus: +1 if attacker is behind the defender
            int behindBonus = 0;
            if (attacker is Knight && defender != null)
            {
                int startRow = attacker.Row;
                int targetRow = defender.Row;
                bool defenderIsWhite = defender.Team;

                bool isRear = defenderIsWhite ? (startRow < targetRow) : (startRow > targetRow);
                if (isRear)
                {
                    behindBonus = 1;
                    Debug.Log("Knight attacks from rear! +1 to roll.");
                }
            }

            int effectiveRoll = rollResult + behindBonus + rookBonus + queenCurse;
            int threshold = 5 + defender.Level - attacker.Level;
            return effectiveRoll > threshold;
        }

        /// <summary>
        /// Finds the king piece for the given team.
        /// </summary>
        public Piece GetKing(bool team)
        {
            foreach (var piece in ChessBoard.Instance.pieces)
            {
                if (piece is King && piece.Team == team)
                    return piece;
            }
            return null;
        }



        /// <summary>
        /// Checks if moving the last piece places the specified king in check.
        /// </summary>
        public bool IsCheck(Piece king, Piece lastPiece)
        {
            return lastPiece.IsValidMove(king.Row, king.Col);
        }

        /// <summary>
        /// Determines if a hypothetical move would result in the king being in check.
        /// </summary>
        public bool CheckCheck(Piece piece, int destRow, int destCol)
        {
            return checker.PredictDanger(piece, GetKing(piece.Team), destRow, destCol);
        }

        /// <summary>
        /// Evaluates whether the current player has any legal moves left. If not, declares checkmate or stalemate.
        /// </summary>
        public bool IsGameOver(Piece selectedPiece)
        {
            bool team = IsWhiteTurn;
            Piece king = GetKing(team);
            var piecesCopy = new List<Piece>(ChessBoard.Instance.pieces);
            foreach (var p in piecesCopy)
            {
                if (p.Team == team)
                {
                    for (int row = 0; row < 8; row++)
                    {
                        for (int col = 0; col < 8; col++)
                        {
                            if (ChessBoard.Instance.CanMove(p, row, col))
                                return false;
                        }
                    }
                }
            }
            if (IsCheck(king, selectedPiece))
                Debug.Log("CheckMate!");
            else
                Debug.Log("StaleMate!");
            return true;
        }

        #region SacredRoad
        /// <summary>
        /// Clears the sacred road highlights for the specified team.
        /// </summary>
        public void ClearSacredRoad(bool team)
        {
            if (team)
                whiteRoad.Clear();
            else
                blackRoad.Clear();
        }

        /// <summary>
        /// Activates the sacred road for a given owner and path.
        /// </summary>
        public void ActivateSacredRoad(Piece owner, List<Vector2Int> path, bool team)
        {
            if (team)
            {
                whiteRoad.Clear();
                whiteRoad.AddRange(path);
                whiteRoadOwner = owner;
                whiteRoadUses = 1;
            }
            else
            {
                blackRoad.Clear();
                blackRoad.AddRange(path);
                blackRoadOwner = owner;
                blackRoadUses = 1;
            }
            Debug.Log($"Sacred road for {(team ? "White" : "Black")} [{path.Count} squares]");
        }

        /// <summary>
        /// Returns whether a piece is on the given road.
        /// </summary>
        private bool IsOnSacredRoad(List<Vector2Int> road, Piece p)
        {
            foreach (var sq in road)
            {
                if (sq.x == p.Row && sq.y == p.Col)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Processes crossing of the sacred road by a piece, applying effects and clearing the road.
        /// </summary>
        public void ProcessSacredCrossing(Piece p, ChessBoard board)
        {
            if (whiteRoadUses > 0 && p != whiteRoadOwner && IsOnSacredRoad(whiteRoad, p))
            {
                UseSacredRoad(p, true);
                whiteRoadUses = 0;
                whiteRoad.Clear();
                whiteRoadOwner = null;
                return;
            }
            if (blackRoadUses > 0 && p != blackRoadOwner && IsOnSacredRoad(blackRoad, p))
            {
                UseSacredRoad(p, false);
                blackRoadUses = 0;
                blackRoad.Clear();
                blackRoadOwner = null;
            }
        }

        /// <summary>
        /// Applies the sacred road effect to a piece.
        /// </summary>
        public void UseSacredRoad(Piece p, bool roadTeam)
        {
            if (p.Team != roadTeam)
            {
                p.UpdateLevel(-1);
                Debug.Log($"{p.Name} crossed enemy sacred road, new level {p.Level}");
                if (p.Level <= 0)
                {
                    bool oldTeam = p.Team;
                    p.ChangeTeam(!oldTeam);
                    p.UpdateLevel(1);
                    Debug.Log($"{p.Name} switched team to {(p.Team ? "White" : "Black")} with level {p.Level}");
                }
            }
        }
        #endregion

        #region PawnRessurection
        /// <summary>
        /// Returns the first available square for pawn resurrection for the specified team, or null if none.
        /// </summary>
        public Vector2Int? GetResurrectionSquare(bool team)
        {
            var candidates = new List<Vector2Int>();
            if (team)
            {
                // White resurrection rows 0-2
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 8; col++)
                        if (ChessBoard.Instance.GetPieceAt(row, col) == null)
                            candidates.Add(new Vector2Int(row, col));
            }
            else
            {
                // Black resurrection rows 5-7
                for (int row = 5; row < 8; row++)
                    for (int col = 0; col < 8; col++)
                        if (ChessBoard.Instance.GetPieceAt(row, col) == null)
                            candidates.Add(new Vector2Int(row, col));
            }
            return candidates.Count > 0 ? (Vector2Int?)candidates[0] : null;
        }

        /// <summary>
        /// Resurrects a pawn for the specified team at the first available resurrection square.
        /// </summary>
        public bool ResurrectPawn(bool team)
        {
            var square = GetResurrectionSquare(team);
            if (!square.HasValue)
            {
                Debug.Log("No space available to resurrect a pawn.");
                return false;
            }

            var capturedList = team ? whiteCaptured : blackCaptured;
            for (int i = 0; i < capturedList.Count; i++)
            {
                var piece = capturedList[i];
                if (piece is Pawn)
                {
                    capturedList.RemoveAt(i);
                    piece.ChangeTeam(team);
                    piece.SetGridPosition(square.Value.x, square.Value.y);
                    ChessBoard.Instance.pieces.Add(piece);
                    Debug.Log($"{piece.Name} resurrected at ({square.Value.x}, {square.Value.y})");
                    return true;
                }
            }

            Debug.Log("No pawn in captured list to resurrect.");
            return false;
        }
        #endregion
    }
}
