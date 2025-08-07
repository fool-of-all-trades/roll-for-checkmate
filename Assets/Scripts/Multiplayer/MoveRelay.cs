using Controller;
using Pieces;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Sends move requests from the local player (client) to the host.
/// Host validates and, if legal, performs the move.
/// </summary>
public class MoveRelay : NetworkBehaviour
{
    [ServerRpc(RequireOwnership = false)]
    public void RequestMoveServerRpc(ulong pieceId, int toRow, int toCol)
    {
        Debug.LogError($"This is a RequestMoveServerRpc function, we got to the first line");

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(pieceId, out var netObj))
            return;

        Debug.LogError($"This is a RequestMoveServerRpc function, we got to the line after the first if check. Hopefully");

        var piece = netObj.GetComponent<Piece>();
        if (piece == null)
        {
            Debug.LogError($"Tried to send move for unspawned piece {piece.name}");
            return;
        }

        if (!netObj.IsSpawned)
        {
            Debug.LogError($"Tried to send move for unspawned network obj {piece.name}");
            return;
        }

        Debug.Log($"[Server] Got RPC for piece {pieceId} -> {toRow},{toCol}");

        var gameCtrl = FindObjectOfType<GameControllerMono>();
        gameCtrl?.TryMove(piece, toRow, toCol);
    }

    /* ------------------------------------------------------------------ */
    /*               helper called by ChessBoard.AttemptMove              */
    /* ------------------------------------------------------------------ */


    private bool _ready;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _ready = true;
        Debug.Log($"[MoveRelay] Ready on {(IsServer ? "Server" : "Client")} ID={NetworkObjectId}");
    }

    public void SendMove(Piece piece, int toRow, int toCol)
    {
        if (!_ready)
        {
            Debug.LogWarning("[MoveRelay] Not spawned yet – cannot send move.");
            return;
        }
        var netObj = piece.GetComponent<NetworkObject>();
        RequestMoveServerRpc(netObj.NetworkObjectId, toRow, toCol);


        Debug.Log($"[Client] Sending move of {piece.name} -> {toRow},{toCol}");
    }

}
