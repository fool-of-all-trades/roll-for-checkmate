using Controller;
using Pieces;
using Unity.Netcode;
using UnityEngine;
using Utils;

/// <summary>
/// Sends move requests from the local player (client) to the host.
/// Host validates and, if legal, performs the move.
/// </summary>
public class MoveRelay : NetworkBehaviour
{
    private GameControllerMono _controller;

    // Client -> Host 
    [ServerRpc(RequireOwnership = false)]
    public void RequestMoveServerRpc(ulong pieceId, int toRow, int toCol, ServerRpcParams rpcParams = default)
    {
        var spawns = NetworkManager.Singleton.SpawnManager.SpawnedObjects;
        if (!spawns.TryGetValue(pieceId, out var netObj)) return;

        var piece = netObj.GetComponent<Piece>();
        var np = netObj.GetComponent<NetworkPiece>();
        if (!piece || !netObj.IsSpawned) return;

        // Block captured pieces
        if (np && np.IsCaptured.Value) return;

        // Seat + turn enforcement
        var sender = rpcParams.Receive.SenderClientId;
        var teams = PlayerTeams.Instance;
        if (!teams) return;

        var senderTeam = PlayerTeams.GetTeam(sender);
        if (senderTeam == TeamSide.None) return;

        bool pieceIsWhite = piece.Team;
        if ((pieceIsWhite && senderTeam != TeamSide.White) ||
            (!pieceIsWhite && senderTeam != TeamSide.Black))
            return;

        bool whiteTurn = TurnSync.Instance && TurnSync.Instance.WhiteTurn.Value;
        if ((senderTeam == TeamSide.White) != whiteTurn) return;

        // If destination contains a captured piece somehow, ignore it
        var target = GameControllerMono.Instance?.PieceAt(toRow, toCol);
        if (target)
        {
            var tnp = target.GetComponent<NetworkPiece>();
            if (tnp && tnp.IsCaptured.Value) return;
        }

        GameControllerMono.Instance?.TryMove(piece, toRow, toCol);
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

        if (IsServer)
        {
            _controller = FindObjectOfType<GameControllerMono>();
            _controller.OnMoveAccepted += OnMoveAccepted;
            _controller.OnDuelRolled += OnDuelRolled;
        }

        // Important: only the owning client should hook its board to THIS relay
        if (IsOwner && IsClient)
        {
            var board = FindObjectOfType<ChessBoard>();
            if (board != null) board.SetMoveRelay(this);
        }
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
        if (IsServer && _controller != null) { 
            _controller.OnMoveAccepted -= OnMoveAccepted;
            _controller.OnDuelRolled -= OnDuelRolled;
        }

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

    private void OnDuelRolled(int raw)
    {
        // Update host UI immediately
        var board = FindObjectOfType<ChessBoard>();
        if (board != null) board.ShowRoll(raw);

        // Tell all clients
        ShowRollClientRpc(raw);
    }

    [ClientRpc]
    private void ShowRollClientRpc(int raw)
    {
        // We already updated host locally; only remote clients need this
        if (IsServer) return;

        var board = FindObjectOfType<ChessBoard>();
        if (board != null) board.ShowRoll(raw);
    }


    [ServerRpc(RequireOwnership = false)]
    public void RequestUltimateServerRpc(ulong pieceId, ServerRpcParams rpcParams = default)
    {
        var spawns = NetworkManager.Singleton.SpawnManager.SpawnedObjects;
        if (!spawns.TryGetValue(pieceId, out var netObj)) return;

        var piece = netObj.GetComponent<Piece>();
        var np = netObj.GetComponent<NetworkPiece>();
        if (!piece || !netObj.IsSpawned) return;

        // Block captured pieces
        if (np && np.IsCaptured.Value) return;

        // Seat + turn enforcement (mirror RequestMoveServerRpc)
        var sender = rpcParams.Receive.SenderClientId;
        var teams = PlayerTeams.Instance;
        if (!teams) return;

        var senderTeam = PlayerTeams.GetTeam(sender);
        if (senderTeam == TeamSide.None) return;

        bool pieceIsWhite = piece.Team;
        if ((pieceIsWhite && senderTeam != TeamSide.White) ||
            (!pieceIsWhite && senderTeam != TeamSide.Black))
            return;

        bool whiteTurn = TurnSync.Instance && TurnSync.Instance.WhiteTurn.Value;
        if ((senderTeam == TeamSide.White) != whiteTurn) return;

        // Host executes the ultimate
        piece.UseUltimateAbility(_controller);
    }

}
