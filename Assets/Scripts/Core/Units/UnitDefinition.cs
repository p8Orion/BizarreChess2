using System;
using System.Collections.Generic;
using UnityEngine;

namespace BizarreChess.Core.Units
{
    /// <summary>
    /// ScriptableObject that defines a unit type (immutable template).
    /// </summary>
    [CreateAssetMenu(fileName = "NewUnit", menuName = "Bizarre Chess/Unit Definition")]
    public class UnitDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string UnitId;
        public string DisplayName;
        public PieceType PieceType;

        [Header("Base Stats")]
        public UnitBaseStats BaseStats;
        public UnitGrowthStats GrowthStats;

        [Header("Movement")]
        public List<MovementPattern> MovementPatterns;

        [Header("Special Properties")]
        public bool IsKing;              // Losing this unit loses the game
        public bool CanCastle;           // For king/rook
        public bool CanPromote;          // Pawn promotion
        public bool CanEnPassant;        // Pawn en passant
        public int PromotionRow = -1;    // Row where promotion happens (-1 = disabled)

        [Header("Cost")]
        public int BaseCost = 1;         // Army building cost

        [Header("Visuals")]
        public char UnicodeWhite;
        public char UnicodeBlack;
        public Sprite SpriteWhite;
        public Sprite SpriteBlack;

        [Header("Abilities (Future)")]
        public List<AbilityUnlock> Abilities;

        /// <summary>
        /// Get all valid target nodes for this unit's movement patterns.
        /// </summary>
        public List<int> GetAllValidMoves(Graph.BoardGraph board, int fromNode, int playerSide,
            Func<int, bool> isOccupied, Func<int, bool> isEnemy, bool hasMoved = false)
        {
            var result = new List<int>();
            var seen = new HashSet<int>();

            foreach (var pattern in MovementPatterns)
            {
                // Skip first-move-only patterns if unit has already moved
                if (pattern.FirstMoveOnly && hasMoved)
                    continue;

                var targets = pattern.GetValidTargets(board, fromNode, playerSide, isOccupied, isEnemy);
                foreach (var target in targets)
                {
                    if (!seen.Contains(target))
                    {
                        seen.Add(target);
                        result.Add(target);
                    }
                }
            }

            return result;
        }

        public char GetUnicode(int playerSide)
        {
            return playerSide == 0 ? UnicodeWhite : UnicodeBlack;
        }

        public Sprite GetSprite(int playerSide)
        {
            return playerSide == 0 ? SpriteWhite : SpriteBlack;
        }
    }

    /// <summary>
    /// Standard chess piece types.
    /// </summary>
    public enum PieceType
    {
        Pawn,
        Knight,
        Bishop,
        Rook,
        Queen,
        King,
        Custom
    }

    /// <summary>
    /// Ability that unlocks at a certain level.
    /// </summary>
    [Serializable]
    public class AbilityUnlock
    {
        public string AbilityId;
        public int RequiredLevel;
    }

    /// <summary>
    /// Unicode characters for chess pieces.
    /// </summary>
    public static class ChessUnicode
    {
        // White pieces (filled)
        public const char WhiteKing = '♔';    // U+2654
        public const char WhiteQueen = '♕';   // U+2655
        public const char WhiteRook = '♖';    // U+2656
        public const char WhiteBishop = '♗';  // U+2657
        public const char WhiteKnight = '♘';  // U+2658
        public const char WhitePawn = '♙';    // U+2659

        // Black pieces (outline)
        public const char BlackKing = '♚';    // U+265A
        public const char BlackQueen = '♛';   // U+265B
        public const char BlackRook = '♜';    // U+265C
        public const char BlackBishop = '♝';  // U+265D
        public const char BlackKnight = '♞';  // U+265E
        public const char BlackPawn = '♟';    // U+265F

        public static char GetPieceChar(PieceType type, bool isWhite)
        {
            return type switch
            {
                PieceType.King => isWhite ? WhiteKing : BlackKing,
                PieceType.Queen => isWhite ? WhiteQueen : BlackQueen,
                PieceType.Rook => isWhite ? WhiteRook : BlackRook,
                PieceType.Bishop => isWhite ? WhiteBishop : BlackBishop,
                PieceType.Knight => isWhite ? WhiteKnight : BlackKnight,
                PieceType.Pawn => isWhite ? WhitePawn : BlackPawn,
                _ => '?'
            };
        }

        /// <summary>
        /// Fallback letters if Unicode font not available.
        /// </summary>
        public static string GetPieceLetter(PieceType type)
        {
            return type switch
            {
                PieceType.King => "K",
                PieceType.Queen => "Q",
                PieceType.Rook => "R",
                PieceType.Bishop => "B",
                PieceType.Knight => "N",
                PieceType.Pawn => "P",
                _ => "?"
            };
        }
    }
}

