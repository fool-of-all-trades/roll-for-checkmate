using Controller;
using Pieces;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Utils;

/// <summary>
/// Sends move requests from the local player (client) to the host.
/// Host validates and, if legal, performs the move.
/// </summary>
public class MoveRelay : NetworkBehaviour
{
    private static MoveRelay s_broadcaster;

    private GameControllerMono _controller;
    private bool _isBroadcaster;

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
            TryRegisterAsBroadcaster();
        }

        // Important: only the owning client should hook its board to THIS relay
        if (IsOwner && IsClient)
        {
            var board = FindObjectOfType<ChessBoard>();
            if (board != null) board.SetMoveRelay(this);
        }
    }

    private void TryRegisterAsBroadcaster()
    {
        if (_isBroadcaster) return;
        if (s_broadcaster != null && s_broadcaster != this) return;

        _controller = FindObjectOfType<GameControllerMono>();
        if (_controller == null)
        {
            Debug.LogWarning("[MoveRelay] No GameControllerMono found; cannot register broadcaster.");
            return;
        }

        s_broadcaster = this;
        _isBroadcaster = true;
        _controller.OnMoveAccepted += OnMoveAccepted;
        _controller.OnDuelRolled += OnDuelRolled;
        Debug.Log($"[MoveRelay] Registered broadcaster ID={NetworkObjectId}");
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
        if (_isBroadcaster && _controller != null)
        {
            _controller.OnMoveAccepted -= OnMoveAccepted;
            _controller.OnDuelRolled -= OnDuelRolled;
        }

        if (s_broadcaster == this)
            s_broadcaster = null;

        _isBroadcaster = false;

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


    // trying by all means to make the tower work, I'll change it in the future I swear
    // === Rook Ultimate: client picks target, server applies ===
    [ServerRpc(RequireOwnership = false)]
    public void StartRookUltimateServerRpc(ulong rookId, ServerRpcParams rpcParams = default)
    {
        var spawns = NetworkManager.Singleton.SpawnManager.SpawnedObjects;
        if (!spawns.TryGetValue(rookId, out var rookObj)) return;

        var rookPiece = rookObj.GetComponent<Piece>();
        var rookNet = rookObj.GetComponent<NetworkPiece>();
        if (!rookPiece || !rookObj.IsSpawned || (rookNet && rookNet.IsCaptured.Value)) return;

        // Enforce: correct player & turn (same checks as RequestUltimateServerRpc)
        var sender = rpcParams.Receive.SenderClientId;
        var senderTeam = PlayerTeams.GetTeam(sender);
        if (senderTeam == TeamSide.None) return;

        bool pieceIsWhite = rookPiece.Team;
        if ((pieceIsWhite && senderTeam != TeamSide.White) ||
            (!pieceIsWhite && senderTeam != TeamSide.Black))
            return;

        bool whiteTurn = TurnSync.Instance && TurnSync.Instance.WhiteTurn.Value;
        if ((senderTeam == TeamSide.White) != whiteTurn) return;

        // Build valid target list (second enemy in each rook ray)
        var board = FindObjectOfType<ChessBoard>();
        var ids = new List<ulong>();

        int r0 = rookPiece.Row, c0 = rookPiece.Col;
        var dirs = new (int dr, int dc)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        foreach (var (dr, dc) in dirs)
        {
            bool skipped = false;
            int r = r0, c = c0;
            while (true)
            {
                r += dr; c += dc;
                if (r < 0 || r > 7 || c < 0 || c > 7) break;

                var p = GameControllerMono.Instance.PieceAt(r, c);
                if (p == null) continue;

                var np = p.GetComponent<NetworkPiece>();
                if (np != null && np.IsCaptured.Value) continue;

                if (!skipped)
                {
                    skipped = true; // skip first seen piece
                }
                else
                {
                    if (p.Team != rookPiece.Team)
                        ids.Add(p.GetComponent<NetworkObject>().NetworkObjectId);
                    break; // stop this ray
                }
            }
        }

        // Send only to the requesting client
        var sendParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { sender } }
        };
        ChooseRookUltimateTargetClientRpc(rookId, ids.ToArray(), sendParams);
    }

    [ClientRpc]
    void ChooseRookUltimateTargetClientRpc(ulong rookId, ulong[] targetIds, ClientRpcParams _ = default)
    {
        // Only the intended client receives this
        var nm = NetworkManager.Singleton;
        var board = FindObjectOfType<ChessBoard>();
        if (board == null) return;

        var list = new List<Piece>();
        foreach (var id in targetIds)
            if (nm.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj))
                list.Add(obj.GetComponent<Piece>());

        board.BeginTargetSelection(list, picked =>
        {
            var chosen = picked ? picked.GetComponent<NetworkObject>().NetworkObjectId : 0UL;
            SubmitRookUltimateTargetServerRpc(rookId, chosen);
        });
    }

    [ServerRpc(RequireOwnership = false)]
    void SubmitRookUltimateTargetServerRpc(ulong rookId, ulong targetId, ServerRpcParams _ = default)
    {
        if (targetId == 0) return; // canceled

        var spawns = NetworkManager.Singleton.SpawnManager.SpawnedObjects;
        if (!spawns.TryGetValue(rookId, out var rookObj)) return;
        if (!spawns.TryGetValue(targetId, out var targetObj)) return;

        var rook = rookObj.GetComponent<Piece>();
        var target = targetObj.GetComponent<Piece>();
        var tnp = targetObj.GetComponent<NetworkPiece>();
        if (!rook || !target || !tnp || tnp.IsCaptured.Value) return;

        // Apply replicated effects
        tnp.IsCaptured.Value = true;                 // hide / disable everywhere
        GameControllerMono.Instance.CapturePiece(target, rook); // XP, curses, etc.

        // Make the new level visible immediately to all clients (optional but nice)
        var rnp = rookObj.GetComponent<NetworkPiece>();
        if (rnp != null) rnp.Level.Value = rook.Level;
    }


}
