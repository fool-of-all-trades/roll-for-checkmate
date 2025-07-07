using System;
using System.Collections.Generic;
using Pieces;
using UnityEngine;

namespace Controller
{
    /// <summary>
    /// Provides functionality to predict if the king would be in danger after a hypothetical move.
    /// </summary>
    public class Checker
    {
        private Piece king;
        private ChessBoard board;

        /// <summary>
        /// Checks diagonal threats from Bishops or Queens.
        /// </summary>
        private bool RangeOfBishopOrQueen()
        {
            Vector2Int[] directions = {
                new Vector2Int(-1, -1), // ↖
                new Vector2Int(-1,  1), // ↗
                new Vector2Int( 1, -1), // ↙
                new Vector2Int( 1,  1)  // ↘
            };

            foreach (var d in directions)
            {
                for (int i = 1; i < 8; i++)
                {
                    int r = king.Row + d.x * i;
                    int c = king.Col + d.y * i;
                    var piece = board.GetPieceAt(r, c);
                    if (piece != null)
                    {
                        if (piece.Team != king.Team && (piece is Bishop || piece is Queen))
                            return true;
                        break;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Checks straight-line threats from Rooks or Queens.
        /// </summary>
        private bool RangeOfRookOrQueen()
        {
            Vector2Int[] directions = {
                new Vector2Int(-1, 0), // ↑
                new Vector2Int( 1, 0), // ↓
                new Vector2Int( 0,-1), // ←
                new Vector2Int( 0, 1)  // →
            };

            foreach (var d in directions)
            {
                for (int i = 1; i < 8; i++)
                {
                    int r = king.Row + d.x * i;
                    int c = king.Col + d.y * i;
                    var piece = board.GetPieceAt(r, c);
                    if (piece != null)
                    {
                        if (piece.Team != king.Team && (piece is Rook || piece is Queen))
                            return true;
                        break;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Checks L-shaped threats from Knights.
        /// </summary>
        private bool RangeOfKnight()
        {
            Vector2Int[] offsets = {
                new Vector2Int(-2, -1), new Vector2Int(-2,  1),
                new Vector2Int(-1, -2), new Vector2Int(-1,  2),
                new Vector2Int( 1, -2), new Vector2Int( 1,  2),
                new Vector2Int( 2, -1), new Vector2Int( 2,  1)
            };

            foreach (var d in offsets)
            {
                int r = king.Row + d.x;
                int c = king.Col + d.y;
                var piece = board.GetPieceAt(r, c);
                if (piece != null && piece.Team != king.Team && piece is Knight)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks one-square threats from the opposing King.
        /// </summary>
        private bool RangeOfKing()
        {
            Vector2Int[] offsets = {
                new Vector2Int(-1, -1), new Vector2Int(-1,  0), new Vector2Int(-1,  1),
                new Vector2Int( 0, -1),                     new Vector2Int( 0,  1),
                new Vector2Int( 1, -1), new Vector2Int( 1,  0), new Vector2Int( 1,  1)
            };

            foreach (var d in offsets)
            {
                int r = king.Row + d.x;
                int c = king.Col + d.y;
                var piece = board.GetPieceAt(r, c);
                if (piece != null && piece.Team != king.Team && piece is King)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks diagonal pawn attack threats against the King.
        /// </summary>
        private bool RangeOfPawn()
        {
            Vector2Int[] directions;
            if (king.Team) // white King, pawns attack upwards on board
            {
                directions = new Vector2Int[] { new Vector2Int(1, -1), new Vector2Int(1, 1) };
            }
            else // black King, pawns attack downwards
            {
                directions = new Vector2Int[] { new Vector2Int(-1, -1), new Vector2Int(-1, 1) };
            }

            foreach (var d in directions)
            {
                int r = king.Row + d.x;
                int c = king.Col + d.y;
                var piece = board.GetPieceAt(r, c);
                if (piece != null && piece.Team != king.Team && piece is Pawn)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Checks if any threat range affects the King.
        /// </summary>
        private bool IsKingInDanger()
        {
            return RangeOfRookOrQueen() || RangeOfBishopOrQueen() || RangeOfKing() || RangeOfKnight() || RangeOfPawn();
        }

        /// <summary>
        /// Simulates a piece moving to (destRow, destCol) and determines if the King would be in check.
        /// </summary>
        /// <param name="piece">The piece to move.</param>
        /// <param name="kingPiece">The King to check.</param>
        /// <param name="destRow">Destination row for the move.</param>
        /// <param name="destCol">Destination column for the move.</param>
        /// <returns>True if the King would be in check after the move.</returns>
        public bool PredictDanger(Piece piece, Piece kingPiece, int destRow, int destCol)
        {
            king = kingPiece;
            board = ChessBoard.Instance;

            // Store original state
            int originalRow = piece.Row;
            int originalCol = piece.Col;
            var captured = board.GetPieceAt(destRow, destCol);
            if (captured != null && captured.Level > piece.Level)
                captured = null;

            // Perform temporary move
            piece.SetGridPosition(destRow, destCol);
            if (captured != null)
            {
                board.HideCapturedPiece(captured);
            }
                

            bool result = IsKingInDanger();

            // Revert move
            piece.SetGridPosition(originalRow, originalCol);
            if (captured != null)
            {
                board.AddPiece(captured);
                captured.gameObject.SetActive(true);
            }

            return result;
        }
    }
}
