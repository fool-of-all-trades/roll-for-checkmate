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
    // team gets replicated automatically
    public NetworkVariable<bool> Team = new NetworkVariable<bool>(
        default,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    Piece piece;

    void Awake() => piece = GetComponent<Piece>();

    public override void OnNetworkSpawn()
    {
        piece.ChangeTeam(Team.Value);
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
}
