using System;
using System.Collections;
using System.Collections.Generic;
using Pieces;
using Unity.Netcode;
using UnityEngine;

public class ChessBoardStartupCoordinator
{
    public IEnumerator WaitForNetworkStarted()
    {
        yield return new WaitUntil(() =>
            NetworkManager.Singleton != null &&
            (NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsClient));
    }

    public IEnumerator WaitForPlayerTeamsReady()
    {
        yield return new WaitUntil(() =>
            PlayerTeams.Instance != null && PlayerTeams.Instance.IsSpawned);
    }

    public IEnumerator WaitForClientSeat()
    {
        yield return new WaitUntil(() => PlayerTeams.MyTeam != TeamSide.None);
    }

    public IEnumerator WaitForTurnSync()
    {
        yield return new WaitUntil(() => TurnSync.Instance != null);
    }

    public IEnumerator WaitForClientBoardPieces(Action<List<Piece>> onReady)
    {
        // wait until at least one piece exists
        yield return new WaitUntil(() => UnityEngine.Object.FindObjectsOfType<Piece>().Length > 0);

        // wait until the count stabilizes for a few frames
        int lastCount = -1, stableFrames = 0;
        while (stableFrames < 3)
        {
            var current = UnityEngine.Object.FindObjectsOfType<Piece>();
            if (current.Length == lastCount) stableFrames++;
            else { stableFrames = 0; lastCount = current.Length; }
            yield return null;
        }

        onReady?.Invoke(new List<Piece>(UnityEngine.Object.FindObjectsOfType<Piece>()));
    }
}
