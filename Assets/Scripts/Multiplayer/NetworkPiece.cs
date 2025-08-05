using Unity.Netcode;
using Pieces;
using UnityEngine;
using static UnityEngine.Rendering.DebugUI.Table;

/// <summary>
/// Bridges the data already stored in Piece to Netcode.
/// The host is authoritative; clients are read-only.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkPiece : NetworkBehaviour
{
    // team gets replicated automatically
    public NetworkVariable<bool> Team = new NetworkVariable<bool>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<int> Row = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<int> Col = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    Piece piece;

    void Awake() => piece = GetComponent<Piece>();

    void OnDestroy()
    {
        // tidy up subscriptions when the object despawns
        if (!IsServer)
        {
            Row.OnValueChanged -= OnCoordsChanged;
            Col.OnValueChanged -= OnCoordsChanged;
        }
    }

    public override void OnNetworkSpawn()
    {
        // runs on *all* peers
        piece.ChangeTeam(Team.Value);

        if (piece.board == null)
            piece.board = FindObjectOfType<ChessBoard>();

        // only clients need to listen for host updates
        if (!IsServer)
        {
            // add ourselves to the client's ChessBoard.pieces list
            var board = FindObjectOfType<ChessBoard>();
            board.pieces.Add(piece);

            // subscribe to position updates
            Row.OnValueChanged += OnCoordsChanged;
            Col.OnValueChanged += OnCoordsChanged;

            // position us correctly
            piece.SetViewPosition(Row.Value, Col.Value);
        }
    }

    // host calls this after a legal move
    public void CommitGridPos(int r, int c)
    {
        Row.Value = r;
        Col.Value = c;

        // host sees movement locally right away
        piece.SetViewPosition(r, c);
    }

    /// <summary>
    /// Only the host calls this immediately before Spawn();
    /// it sets the authoritative value that will be sent to every client.
    /// </summary>
    public void InitNetwork(bool team)
    {
        Team.Value = team;
        piece.ChangeTeam(team);
    }

    // fired on *clients* whenever either Row or Col changes
    void OnCoordsChanged(int _, int __)
    {
        piece.SetViewPosition(Row.Value, Col.Value);
    }

}
