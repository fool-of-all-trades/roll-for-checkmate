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
    private GameControllerMono _controller;

    // Client -> Host 
    [ServerRpc(RequireOwnership = false)]
    public void RequestMoveServerRpc(ulong pieceId, int toRow, int toCol)
    {
        Debug.Log($"This is a RequestMoveServerRpc function, we got to the first line");

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(pieceId, out var netObj))
            return;

        Debug.Log($"This is a RequestMoveServerRpc function, we got to the line after the first if check. Hopefully");

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

        //var gameCtrl = FindObjectOfType<GameControllerMono>();

        _controller = FindObjectOfType<GameControllerMono>();

        // 1) Let your controller validate & produce a MoveResult:
        var result = _controller?.TryMove(piece, toRow, toCol);
    }

    // Host -> Client
    [ClientRpc]
    void BroadcastMoveClientRpc(
        ulong movingPieceId,
        int fromRow, int fromCol,
        int toRow, int toCol,
        ulong capturedPieceId
    )
    {
        // only let real *remote* clients apply this; host already did it
        if (IsServer) return;

        var netMgr = NetworkManager.Singleton;
        var board = FindObjectOfType<ChessBoard>();

        // look up the Piece instances by NetworkObjectId
        var movingPiece = netMgr.SpawnManager.SpawnedObjects[movingPieceId]
                             .GetComponent<Piece>();
        Piece capturedPiece = null;
        if (capturedPieceId != 0)
            capturedPiece = netMgr.SpawnManager.SpawnedObjects[capturedPieceId]
                               .GetComponent<Piece>();

        // reconstruct the MoveResult
        var result = new MoveResult(
           movingPiece,
           fromRow, fromCol,
           toRow, toCol,
           capturedPiece
        );

        // apply exactly the same visuals/capture logic you have on the host
        board.ApplyMoveVisuals(result);
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

        if (!IsServer) return;

        // find your controller (that raises OnMoveAccepted)
        _controller = FindObjectOfType<GameControllerMono>();
        _controller.OnMoveAccepted += OnMoveAccepted;
    }

    private void OnMoveAccepted(MoveResult m)
    {
        // figure out the IDs
        var movingId = m.Piece.GetComponent<NetworkObject>().NetworkObjectId;
        var capturedId = m.Captured != null
            ? m.Captured.GetComponent<NetworkObject>().NetworkObjectId
            : 0;

        // ship it to all clients
        BroadcastMoveClientRpc(
            movingId,
            m.FromRow, m.FromCol,
            m.ToRow, m.ToCol,
            capturedId
        );
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && _controller != null)
            _controller.OnMoveAccepted -= OnMoveAccepted;
        base.OnNetworkDespawn();
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
