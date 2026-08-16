using Pieces;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

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

            foreach (var p in board.pieces)
            {
                if (p.Team == owner.Team) continue;
                var np = p.GetComponent<NetworkPiece>();
                if (np != null && np.IsCaptured.Value) continue;
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

                    // Safety: authoritative changes only on the server
                    if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
                    {
                        Debug.LogWarning("[KnightUltimate] UseAbility callback on non-server. Ignoring.");
                        return;
                    }

                    if (controller.TryUseKnightUltimate(owner, target))
                        Debug.Log($"Knight DIVINE SMITES the shit out of {target.Name}!");
                })
            );
        }
    }
}
