using UnityEngine;
using Abilities;
using Unity.Netcode;
using static UnityEngine.Rendering.DebugUI;

namespace Pieces
{
    [RequireComponent(typeof(SpriteRenderer))]
    public abstract class Piece : MonoBehaviour
    {
        [Header("Team Sprites")]
        public Sprite whiteSprite;
        public Sprite blackSprite;

        [SerializeField] protected SpriteRenderer spriteRenderer;

        protected Ability baseAbility;
        protected Ability ultimateAbility;

        public ChessBoard board;

        protected string pieceName;
        protected int level = 1; // max level is 5
        protected bool team; // true = white, false = black
        protected int row, col;
        protected int cursedTurns;
        protected int stunnedTurns;
        protected bool hasMoved;

        private void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        #region Properties
        public bool Team { get => team; private set => team = value; }
        public int Row { get => row; private set => row = value; }
        public int Col { get => col; private set => col = value; }
        public int Level { get => level; private set => level = value; }
        public int CursedTurns { get => cursedTurns; private set => cursedTurns = value; }
        public bool HasMoved { get => hasMoved; private set => hasMoved = value; }
        public string Name { get => pieceName; private set => pieceName = value; }
        public int StunnedTurns { get => stunnedTurns; private set => stunnedTurns = value; }
        #endregion

        public void Init(ChessBoard boardCtx, int startRow, int startCol, bool team)
        {
            this.board = boardCtx;

            this.Team = team;

            if (spriteRenderer != null)
                spriteRenderer.sprite = this.Team ? whiteSprite : blackSprite;

            // set the grid coords + set the transform.position so the Piece appears in the right place
            this.Row = startRow;
            this.Col = startCol;

            SetViewPosition(startRow, startCol);
        }

        /// <summary>
        /// Sets row/col and the GameObject's transform.position.
        /// Used in the ChessBoard (view layer).
        /// </summary>
        /// <param name="newRow"></param>
        /// <param name="newCol"></param>
        public void SetViewPosition(int newRow, int newCol)
        {
            row = newRow;
            col = newCol;
            transform.position = board.GridToWorld(newRow, newCol);
        }

        /// <summary>
        /// Sets row/col but does not touch the GameObject's transform.position.
        /// Used in the controller/services (logic layer).
        /// </summary>
        public void SetBoardCoords(int r, int c)
        {
            //if it did update the transform.position then the Piece would teleport twice
            row = r;
            col = c;

            var np = GetComponent<NetworkPiece>();
            if (np != null && np.IsServer)
                np.CommitGridPos(r, c);      // replicates to everyone so that the client can see host's moves
        }

        /// <summary>
        /// Sets row/col for temporary rule simulations only.
        /// Does not move visuals or replicate network state.
        /// </summary>
        public void SetBoardCoordsForSimulation(int r, int c)
        {
            row = r;
            col = c;
        }

        public void ChangeTeam(bool newTeam)
        {
            team = newTeam;

            if (spriteRenderer != null)
                spriteRenderer.sprite = team ? whiteSprite : blackSprite;
        }

        public void MarkMoved() => hasMoved = true;

        public void UpdateLevel(int delta) => level += delta;

        public void DecreaseCursedTurns()
        {
            if (cursedTurns > 0) cursedTurns--;

            var np = GetComponent<NetworkPiece>();
            if (np != null && np.IsServer)
                np.CursedTurns.Value = cursedTurns;
        }

        public void SetCursedTurns(int t) => cursedTurns = t;

        public void SetStunnedTurns(int turns, bool replicate = true)
        {
            stunnedTurns = Mathf.Max(0, turns);

            if (replicate && NetworkManager.Singleton && NetworkManager.Singleton.IsServer)
                GetComponent<NetworkPiece>().StunnedTurns.Value = stunnedTurns;
        }

        public void TickStunnedTurns() 
        {
            if (stunnedTurns > 0)
                SetStunnedTurns(stunnedTurns - 1);
        }

        public bool IsStunned() => this != null && this.StunnedTurns > 0;

        public void UseUltimateAbility(IGameController controller)
        {
            if (CanUseUltimate())
                ultimateAbility.UseAbility(controller, this);
        }

        /// <summary>
        /// Checks if the ultimate ability can be used based on the piece's level and cursed turns.
        /// </summary>
        public bool CanUseUltimate()
            => ultimateAbility != null && ultimateAbility.CanUseUltimate(level) && cursedTurns == 0;

        public bool HasUsedUltimate()
            => ultimateAbility != null && ultimateAbility.HasUsedUltimate;

        public void SetUltimateUsed(bool used)
        {
            ultimateAbility?.SetUltimateUsed(used);
        }

        /// <summary>
        /// Implements the specific geometrical movement rules for each piece type.
        /// No checking safety, that's the controller's job.
        /// </summary>
        public abstract bool IsValidMove(int newRow, int newCol);
    }
}

