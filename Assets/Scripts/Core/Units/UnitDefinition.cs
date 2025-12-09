using System;
using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Skills;

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

        [Header("Movement")]
        public List<MovementPattern> MovementPatterns;

        [Header("Skills")]
        [SerializeReference]
        public List<Skill> Skills;

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

        [Header("3D Render Mode")]
        public PieceRenderMode RenderMode;
        public Texture2D TokenTexture;        // For Token2D mode - displayed on top of flat cylinder
        /// <summary>
        /// For RevolutionVolume mode. The PNG is a cross-section profile cut from center to edge:
        /// - Y axis of image = height of the piece (bottom row = base, top row = tip)
        /// - X axis of image = radius from center outward (left = center axis, right = outer edge)
        /// 
        /// For each row, the generator scans left-to-right:
        /// - First non-transparent pixel = inner radius (hollow interior if X > 0)
        /// - Last non-transparent pixel = outer radius
        /// 
        /// Draw the right half of the piece silhouette. Examples:
        /// - Solid pawn: filled silhouette touching left edge (X=0) at all heights
        /// - Wine glass: gap at left (hollow interior), filled at right (glass walls)
        /// 
        /// The resulting mesh uses the player's primary color uniformly.
        /// </summary>
        public Texture2D RevolutionTexture;
        public float PieceHeight = 1.5f;        // Height for displacement/revolution mode

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
    /// Rendering mode for 3D chess pieces.
    /// </summary>
    public enum PieceRenderMode
    {
        /// <summary>Flat cylinder with PNG texture on top</summary>
        Token2D,
        /// <summary>
        /// 3D lathe/revolution volume from PNG profile.
        /// X axis = radius from center, Y axis = height.
        /// Alpha channel defines solid vs empty space.
        /// </summary>
        RevolutionVolume
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
