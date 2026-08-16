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

    private readonly struct CommandSenderContext
    {
        public CommandSenderContext(ulong senderClientId, TeamSide senderTeam, bool isWhiteTurn)
        {
            SenderClientId = senderClientId;
            SenderTeam = senderTeam;
            IsWhiteTurn = isWhiteTurn;
        }

        public ulong SenderClientId { get; }
        public TeamSide SenderTeam { get; }
        public bool IsWhiteTurn { get; }
    }

    // Client -> Host 
    [ServerRpc(RequireOwnership = false)]
    public void RequestMoveServerRpc(ulong pieceId, int toRow, int toCol, ServerRpcParams rpcParams = default)
    {
        if (!TryValidatePieceCommand(pieceId, rpcParams, out var piece, out _, out _))
            return;

        TryExecuteMoveCommand(piece, toRow, toCol);
    }

    private void TryExecuteMoveCommand(Piece piece, int toRow, int toCol)
    {
        if (piece == null) return;

        // If destination contains a captured piece somehow, ignore it
        var target = GameControllerMono.Instance?.PieceAt(toRow, toCol);
        if (target)
        {
            var tnp = target.GetComponent<NetworkPiece>();
            if (tnp && tnp.IsCaptured.Value) return;
        }

        GameControllerMono.Instance?.TryMove(piece, toRow, toCol);
    }

    private bool TryValidatePieceCommand(
        ulong pieceId,
        ServerRpcParams rpcParams,
        out Piece piece,
        out TeamSide senderTeam,
        out string failureReason)
    {
        return TryValidatePieceCommand(
            pieceId,
            rpcParams.Receive.SenderClientId,
            out piece,
            out senderTeam,
            out failureReason);
    }

    private bool TryValidatePieceCommand(
        ulong pieceId,
        ulong senderClientId,
        out Piece piece,
        out TeamSide senderTeam,
        out string failureReason)
    {
        piece = null;
        senderTeam = TeamSide.None;
        failureReason = null;

        if (!TryValidateCommandSender(senderClientId, out var context, out failureReason))
            return false;

        senderTeam = context.SenderTeam;

        var networkManager = NetworkManager.Singleton;

        var spawns = networkManager.SpawnManager.SpawnedObjects;
        if (!spawns.TryGetValue(pieceId, out var netObj))
        {
            failureReason = "NetworkObject id was not found.";
            return false;
        }

        piece = netObj.GetComponent<Piece>();
        if (!piece || !netObj.IsSpawned)
        {
            failureReason = "NetworkObject has no spawned Piece.";
            return false;
        }

        var np = netObj.GetComponent<NetworkPiece>();
        if (np && np.IsCaptured.Value)
        {
            failureReason = "Piece is captured.";
            return false;
        }

        bool pieceIsWhite = piece.Team;
        if ((pieceIsWhite && senderTeam != TeamSide.White) ||
            (!pieceIsWhite && senderTeam != TeamSide.Black))
        {
            failureReason = "Piece does not belong to sender team.";
            return false;
        }

        return true;
    }

    private bool TryValidateCommandSender(
        ServerRpcParams rpcParams,
        out CommandSenderContext context,
        out string failureReason)
    {
        return TryValidateCommandSender(
            rpcParams.Receive.SenderClientId,
            out context,
            out failureReason);
    }

    private bool TryValidateCommandSender(
        ulong senderClientId,
        out CommandSenderContext context,
        out string failureReason)
    {
        context = default;
        failureReason = null;

        if (!IsServer)
        {
            failureReason = "Not running on server.";
            return false;
        }

        var networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            failureReason = "NetworkManager is missing.";
            return false;
        }

        var teams = PlayerTeams.Instance;
        if (!teams || !teams.IsSpawned)
        {
            failureReason = "PlayerTeams is missing.";
            return false;
        }

        var senderTeam = PlayerTeams.GetTeam(senderClientId);
        if (senderTeam == TeamSide.None)
        {
            failureReason = "Sender has no team.";
            return false;
        }

        if (!TurnSync.Instance)
        {
            failureReason = "TurnSync is missing.";
            return false;
        }

        bool whiteTurn = TurnSync.Instance.WhiteTurn.Value;
        if ((senderTeam == TeamSide.White) != whiteTurn)
        {
            failureReason = "Not sender team's turn.";
            return false;
        }

        context = new CommandSenderContext(senderClientId, senderTeam, whiteTurn);
        return true;
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
        if (piece == null) return;

        var netObj = piece.GetComponent<NetworkObject>();
        if (netObj == null) return;

        if (IsServer)
        {
            var networkManager = NetworkManager.Singleton;
            if (networkManager == null) return;

            if (!TryValidatePieceCommand(
                    netObj.NetworkObjectId,
                    networkManager.LocalClientId,
                    out var validatedPiece,
                    out _,
                    out _))
                return;

            TryExecuteMoveCommand(validatedPiece, toRow, toCol);
        }
        else
        {
            RequestMoveServerRpc(netObj.NetworkObjectId, toRow, toCol);
        }

        Debug.Log($"[MoveRelay] Sending move of {piece.name} -> {toRow},{toCol}");
    }

    public void SendUltimate(Piece piece)
    {
        if (piece == null) return;

        var netObj = piece.GetComponent<NetworkObject>();
        if (netObj == null) return;

        var id = netObj.NetworkObjectId;
        if (piece is Rook)
            StartRookUltimateServerRpc(id);
        else if (piece is Knight)
            StartKnightUltimateServerRpc(id);
        else
            RequestUltimateServerRpc(id);
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
        if (!TryValidatePieceCommand(pieceId, rpcParams, out var piece, out _, out _))
            return;

        var controller = _controller != null ? _controller : GameControllerMono.Instance;
        if (controller == null) return;

        // Host executes the ultimate
        controller.TryUseUltimate(piece);
    }


    // trying by all means to make the tower work, I'll change it in the future I swear
    // === Rook Ultimate: client picks target, server applies ===
    [ServerRpc(RequireOwnership = false)]
    public void StartRookUltimateServerRpc(ulong rookId, ServerRpcParams rpcParams = default)
    {
        var sender = rpcParams.Receive.SenderClientId;
        if (!TryValidatePieceCommand(rookId, rpcParams, out var rookPiece, out _, out _))
            return;

        // Build valid target list (second enemy in each rook ray)
        var ids = GetRookUltimateTargetIds(rookPiece);

        // Send only to the requesting client
        var sendParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { sender } }
        };
        ChooseRookUltimateTargetClientRpc(rookId, ids.ToArray(), sendParams);
    }

    private List<ulong> GetRookUltimateTargetIds(Piece rookPiece)
    {
        var ids = new List<ulong>();
        if (rookPiece == null || GameControllerMono.Instance == null) return ids;

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

        return ids;
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
    public void StartKnightUltimateServerRpc(ulong knightId, ServerRpcParams rpcParams = default)
    {
        var sender = rpcParams.Receive.SenderClientId;
        if (!TryValidatePieceCommand(knightId, rpcParams, out var knightPiece, out _, out _))
            return;

        if (!(knightPiece is Knight)) return;
        if (!knightPiece.CanUseUltimate()) return;
        if (knightPiece.StunnedTurns > 0) return;

        var ids = GetKnightUltimateTargetIds(knightPiece);
        var sendParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { sender } }
        };
        ChooseKnightUltimateTargetClientRpc(knightId, ids.ToArray(), sendParams);
    }

    private List<ulong> GetKnightUltimateTargetIds(Piece knightPiece)
    {
        var ids = new List<ulong>();
        var networkManager = NetworkManager.Singleton;
        if (knightPiece == null || networkManager == null) return ids;

        foreach (var entry in networkManager.SpawnManager.SpawnedObjects)
        {
            var netObj = entry.Value;
            if (netObj == null || !netObj.IsSpawned) continue;

            var piece = netObj.GetComponent<Piece>();
            if (piece == null || piece.Team == knightPiece.Team) continue;

            var np = netObj.GetComponent<NetworkPiece>();
            if (np == null || np.IsCaptured.Value) continue;

            ids.Add(netObj.NetworkObjectId);
        }

        return ids;
    }

    [ClientRpc]
    void ChooseKnightUltimateTargetClientRpc(ulong knightId, ulong[] targetIds, ClientRpcParams _ = default)
    {
        var nm = NetworkManager.Singleton;
        var board = FindObjectOfType<ChessBoard>();
        if (nm == null || board == null) return;

        var list = new List<Piece>();
        foreach (var id in targetIds)
            if (nm.SpawnManager.SpawnedObjects.TryGetValue(id, out var obj))
                list.Add(obj.GetComponent<Piece>());

        board.BeginTargetSelection(list, picked =>
        {
            if (picked == null) return;

            var chosen = picked.GetComponent<NetworkObject>().NetworkObjectId;
            SubmitKnightUltimateTargetServerRpc(knightId, chosen);
        });
    }

    [ServerRpc(RequireOwnership = false)]
    void SubmitKnightUltimateTargetServerRpc(ulong knightId, ulong targetId, ServerRpcParams rpcParams = default)
    {
        if (!TryValidatePieceCommand(knightId, rpcParams, out var knightPiece, out _, out _))
            return;

        var spawns = NetworkManager.Singleton.SpawnManager.SpawnedObjects;
        if (!spawns.TryGetValue(targetId, out var targetObj)) return;
        if (!targetObj.IsSpawned) return;

        var target = targetObj.GetComponent<Piece>();
        if (!target) return;

        var controller = _controller != null ? _controller : GameControllerMono.Instance;
        if (controller == null) return;

        controller.TryUseKnightUltimate(knightPiece, target);
    }

    [ServerRpc(RequireOwnership = false)]
    void SubmitRookUltimateTargetServerRpc(ulong rookId, ulong targetId, ServerRpcParams rpcParams = default)
    {
        if (!TryValidatePieceCommand(rookId, rpcParams, out var rookPiece, out var senderTeam, out var reason))
            return;

        if (targetId == 0) return; // canceled

        var spawns = NetworkManager.Singleton.SpawnManager.SpawnedObjects;
        if (!spawns.TryGetValue(targetId, out var targetObj)) return;
        if (!targetObj.IsSpawned) return;

        var target = targetObj.GetComponent<Piece>();
        if (!target) return;

        var controller = _controller != null ? _controller : GameControllerMono.Instance;
        if (controller == null) return;

        controller.TryUseRookUltimate(rookPiece, target);
    }


}
