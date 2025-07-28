using Pieces;
using System.Collections.Generic;
using UnityEngine;

namespace Abilities
{
    /// <summary>
    /// Knight ultimate – "Divine Smite".
    /// Player clicks one enemy piece anywhere:
    ///   • that piece’s Level becomes 1
    ///   • it is stunned for 1 full turn (cannot move)
    /// </summary>
    public class KnightUltimateAbility : Ability
    {
        public KnightUltimateAbility(int levelReq) : base(levelReq) { }

        public override void UseAbility(IGameController controller, Piece owner)
        {
            if (usedUltimate)
            {
                Debug.Log("Knight ultimate already used.");
                return;
            }

            if (owner.StunnedTurns > 0)
            {
                Debug.Log("Knight is stunned and cannot use abilities.");
                return;
            }

            // Ask the view (ChessBoard) to start target-selection mode.
            ChessBoard board = owner.board;

            List<Piece> allPieces = board.pieces;
            List<Piece> targets = new List<Piece>();

            foreach (var p in allPieces)
            {
                if (p.Team != owner.Team)
                    targets.Add(p);
            }

            // I know it looks confusing but this is just passing two functions as parameters
            board.BeginTargetSelection(
                targets,
                onChosen: (target =>
                {
                    if (target == null)
                    {
                        Debug.Log("Divine smite canceled.");
                        return;
                    }

                    Debug.Log($"Knight DIVINE SMITES the shit out of {target.Name}!");

                    target.UpdateLevel(1 - target.Level);
                    target.SetStunnedTurns(2);

                    usedUltimate = true;

                    // TODO: fire an event so UI can flash the target square
                })
            );
        }
    }
}
