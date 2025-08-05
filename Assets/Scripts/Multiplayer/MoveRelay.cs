using Controller;
using Pieces;
using Unity.Netcode;

/// <summary>
/// Sends move requests from the local player (client) to the host.
/// Host validates and, if legal, performs the move.
/// </summary>
public class MoveRelay : NetworkBehaviour
{
    [ServerRpc(RequireOwnership = false)]
    void RequestMoveServerRpc(ulong pieceId, int toRow, int toCol)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects
                 .TryGetValue(pieceId, out var netObj))
            return;

        var piece = netObj.GetComponent<Piece>();
        if (piece == null) return;

        var gameCtrl = FindObjectOfType<GameControllerMono>();
        gameCtrl?.TryMove(piece, toRow, toCol);
    }

    /* ------------------------------------------------------------------ */
    /*               helper called by ChessBoard.AttemptMove              */
    /* ------------------------------------------------------------------ */
    public void SendMove(Piece piece, int toRow, int toCol)
    {
        var netObj = piece.GetComponent<NetworkObject>();
        RequestMoveServerRpc(netObj.NetworkObjectId, toRow, toCol);
    }
}
