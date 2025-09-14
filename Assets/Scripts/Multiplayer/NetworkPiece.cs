using Unity.Netcode;
using Pieces;
using UnityEngine;

/// <summary>
/// Bridges the data already stored in Piece to Netcode.
/// The host is authoritative; clients are read-only.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class NetworkPiece : NetworkBehaviour
{
    // Variables that need to be transmitted over the network.
    public NetworkVariable<bool> Team = new(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<int> Row = new(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<int> Col = new(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // replicated captured state (authoritative on server)
    public NetworkVariable<bool> IsCaptured = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public NetworkVariable<int> Level = new(1,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> StunnedTurns = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public NetworkVariable<int> CursedTurns = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private Piece piece;
    private Collider2D _col;
    private SpriteRenderer _sr;

    void Awake()
    {
        piece = GetComponent<Piece>();
        _col = GetComponent<Collider2D>();
        _sr = GetComponent<SpriteRenderer>();
    }

    public override void OnDestroy()
    {
        // tidy up subscriptions when the object despawns
        Row.OnValueChanged -= OnCoordsChanged;
        Col.OnValueChanged -= OnCoordsChanged;
        IsCaptured.OnValueChanged -= OnCapturedChanged;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
            SeedNetworkFromPiece();


        // runs on *all* peers
        piece.ChangeTeam(Team.Value);
        piece.UpdateLevel(Level.Value - piece.Level);

        piece.SetStunnedTurns(StunnedTurns.Value);
        piece.SetCursedTurns(CursedTurns.Value);

        if (!piece.board)
            piece.board = FindObjectOfType<ChessBoard>();

        // add ourselves to the client's ChessBoard list ( already done this on clients)
        if (!IsServer)
        {
            var board = FindObjectOfType<ChessBoard>();
            if (board != null)
                board.pieces.Add(piece); //  board uses this list elsewhere
        }

        // subscribe to changes (both sides benefit from consistent visuals)
        Row.OnValueChanged += OnCoordsChanged;
        Col.OnValueChanged += OnCoordsChanged;
        IsCaptured.OnValueChanged += OnCapturedChanged;
        Level.OnValueChanged += OnLevelChanged;
        Team.OnValueChanged += (_, now) => piece.ChangeTeam(now);

        StunnedTurns.OnValueChanged += (_, now) =>
        {
            piece.SetStunnedTurns(now, replicate: false); // update local model only
            piece.board.RefreshInfoIf(piece);
        };

        CursedTurns.OnValueChanged += (_, now) =>
        {
            piece.SetCursedTurns(now);
            piece.board.RefreshInfoIf(piece);
        };

        // apply initial states for late joiners
        piece.SetViewPosition(Row.Value, Col.Value);
        ApplyCapturedState(IsCaptured.Value);
    }

    private void SeedNetworkFromPiece()
    {
        // push the *correct* starting values from the server’s Piece
        Team.Value = piece.Team;
        Level.Value = piece.Level;         // <- this prevents everything becoming 1
        Row.Value = piece.Row;
        Col.Value = piece.Col;
        IsCaptured.Value = false;               // or your actual captured state
        StunnedTurns.Value = piece.StunnedTurns;
        CursedTurns.Value = piece.CursedTurns;
    }


    public override void OnNetworkDespawn()
    {
        Row.OnValueChanged -= OnCoordsChanged;
        Col.OnValueChanged -= OnCoordsChanged;
        Level.OnValueChanged -= OnLevelChanged;
        Team.OnValueChanged -= (_, __) => { };
        IsCaptured.OnValueChanged -= OnCapturedChanged;
        StunnedTurns.OnValueChanged -= (_, now) => { };
        CursedTurns.OnValueChanged -= (_, now) => { };
    }

    // host calls this after a legal move
    public void CommitGridPos(int r, int c)
    {
        Row.Value = r;
        Col.Value = c;
        piece.SetViewPosition(r, c);
    }

    /// <summary>Only the host calls this before Spawn(); sets authoritative team.</summary>
    public void InitNetwork(bool team)
    {
        Team.Value = team;
        piece.ChangeTeam(team);
    }

    // fired when either Row or Col changes
    private void OnCoordsChanged(int _, int __)
    {
        piece.SetViewPosition(Row.Value, Col.Value);
    }

    // fired when IsCaptured changes
    private void OnCapturedChanged(bool _, bool now)
    {
        ApplyCapturedState(now);
    }

    private void OnLevelChanged(int prev, int now)
    {
        ApplyLevel(now);
    }

    // centralize how "captured" looks/behaves
    private void ApplyCapturedState(bool captured)
    {
        // disable selection/clicks
        if (_col) _col.enabled = !captured;

        // hide from board by default (or route to a "graveyard" UI if you want)
        if (_sr) _sr.enabled = !captured;

    }

    private void ApplyLevel(int now)
    {
        // bring the local Piece model to the replicated value
        int delta = now - piece.Level;
        if (delta != 0)
            piece.UpdateLevel(delta);

        // refresh info panel if this piece is currently selected
        var board = FindObjectOfType<ChessBoard>();
        if (board != null) board.RefreshInfoIf(piece);  // add this helper below
    }
}
