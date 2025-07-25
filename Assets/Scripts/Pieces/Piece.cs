using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Abilities;
using Controller;

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
        protected int level = 1;
        protected bool team; // true = white, false = black
        protected int row, col;
        protected int cursedTurns;
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
        #endregion

        #region Grid Management
        public void SetGridPosition(int newRow, int newCol)
        {
            row = newRow;
            col = newCol;
            transform.position = board.GridToWorld(newRow, newCol);
        }

        // Inside your Piece class
        public void SetBoardCoords(int r, int c)
        {
            row = r;
            col = c;
        }


        public void Init(ChessBoard boardCtx, int startRow, int startCol, bool team)
        {
            this.board = boardCtx;

            // set the grid coords + move the transform
            this.Row = startRow;
            this.Col = startCol;
            this.Team = team;

            if (spriteRenderer != null)
                spriteRenderer.sprite = this.Team ? whiteSprite : blackSprite;

            SetGridPosition(startRow, startCol);
        }

        public void ChangeTeam(bool newTeam)
        {
            team = newTeam;

            if (spriteRenderer != null)
                spriteRenderer.sprite = team ? whiteSprite : blackSprite;
        }

        public void MarkMoved() => hasMoved = true;
        public void DecreaseCursedTurns()
        {
            if (cursedTurns > 0) cursedTurns--;
        }

        public void UpdateLevel(int delta)
        {
            level += delta;
        }

        public void SetCursedTurns(int t) => cursedTurns = t;
        #endregion

        #region Abilities
        public void UseBaseAbility(IGameController controller)
        {
            if (baseAbility != null && cursedTurns == 0)
                baseAbility.UseAbility(controller, this);
        }

        public void UseUltimateAbility(IGameController controller)
        {
            if (CanUseUltimate())
                ultimateAbility.UseAbility(controller, this);
        }

        public bool CanUseUltimate()
            => ultimateAbility != null && ultimateAbility.CanUseUltimate(level) && cursedTurns == 0;
        #endregion

        /// <summary>
        /// Implements the specific movement rules for each piece type.
        /// </summary>
        public abstract bool IsValidMove(int newRow, int newCol);
    }
}

