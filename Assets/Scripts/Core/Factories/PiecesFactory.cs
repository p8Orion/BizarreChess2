using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Units;

namespace BizarreChess.Core.Factories
{
    /// <summary>
    /// Factory for creating chess piece definitions with caching.
    /// Use Get() to obtain cached instances, avoiding duplicate ScriptableObjects.
    /// </summary>
    public static class PiecesFactory
    {
        #region Cache

        private static readonly Dictionary<string, UnitDefinition> _cache = new Dictionary<string, UnitDefinition>();

        /// <summary>
        /// Get a piece definition by name. Returns cached instance or creates and caches a new one.
        /// </summary>
        public static UnitDefinition Get(string pieceName)
        {
            if (_cache.TryGetValue(pieceName, out var cached))
                return cached;

            var definition = CreateDefinition(pieceName);
            if (definition != null)
                _cache[pieceName] = definition;

            return definition;
        }

        /// <summary>
        /// Get all known piece definitions (cached).
        /// </summary>
        public static Dictionary<string, UnitDefinition> GetAll()
        {
            // Ensure all pieces are cached
            var allNames = new[] { "King", "Queen", "Rook", "Bishop", "Knight", "Camel", "Pawn" };
            foreach (var name in allNames)
                Get(name);

            return new Dictionary<string, UnitDefinition>(_cache);
        }

        /// <summary>
        /// Clear the cache. Useful for testing or hot-reloading.
        /// </summary>
        public static void ClearCache()
        {
            _cache.Clear();
        }

        /// <summary>
        /// Create a new definition (internal, use Get() for cached access).
        /// </summary>
        private static UnitDefinition CreateDefinition(string pieceName)
        {
            return pieceName switch
            {
                "King" => CreateKingDefinition(),
                "Queen" => CreateQueenDefinition(),
                "Rook" => CreateRookDefinition(),
                "Bishop" => CreateBishopDefinition(),
                "Knight" => CreateKnightDefinition(),
                "Camel" => CreateCamelDefinition(),
                "Pawn" => CreatePawnDefinition(),
                _ => null
            };
        }

        #endregion

        #region Individual Piece Definitions (Private)

        private static UnitDefinition CreateKingDefinition()
        {
            var king = ScriptableObject.CreateInstance<UnitDefinition>();
            king.UnitId = "King";
            king.DisplayName = "King";
            king.PieceType = PieceType.King;
            king.IsKing = true;
            king.CanCastle = true;
            king.BaseCost = 0; // Priceless / required

            king.BaseStats = new UnitBaseStats
            {
                Health = 100,
                Attack = 5,
                Defense = 5,
                Speed = 3,
                Range = 1,
                Movement = 1
            };

            king.GrowthStats = UnitGrowthStats.Default;

            king.MovementPatterns = new List<MovementPattern>
            {
                new MovementPattern(MovementType.Adjacent, 1)
            };

            king.UnicodeWhite = ChessUnicode.WhiteKing;
            king.UnicodeBlack = ChessUnicode.BlackKing;

            // 3D Rendering
            ApplyPieceTextures(king, "King");
            king.PieceHeight = 2f;

            return king;
        }

        private static UnitDefinition CreateQueenDefinition()
        {
            var queen = ScriptableObject.CreateInstance<UnitDefinition>();
            queen.UnitId = "Queen";
            queen.DisplayName = "Queen";
            queen.PieceType = PieceType.Queen;
            queen.BaseCost = 9;

            queen.BaseStats = new UnitBaseStats
            {
                Health = 50,
                Attack = 15,
                Defense = 3,
                Speed = 8,
                Range = 1,
                Movement = 8
            };

            queen.GrowthStats = new UnitGrowthStats
            {
                HealthPerLevel = 5,
                AttackPerLevel = 2,
                DefensePerLevel = 1,
                SpeedPerLevel = 0
            };

            queen.MovementPatterns = new List<MovementPattern>
            {
                new MovementPattern(MovementType.Orthogonal, -1),
                new MovementPattern(MovementType.Diagonal, -1)
            };

            queen.UnicodeWhite = ChessUnicode.WhiteQueen;
            queen.UnicodeBlack = ChessUnicode.BlackQueen;

            // 3D Rendering
            ApplyPieceTextures(queen, "Queen");
            queen.PieceHeight = 2f;

            return queen;
        }

        private static UnitDefinition CreateRookDefinition()
        {
            var rook = ScriptableObject.CreateInstance<UnitDefinition>();
            rook.UnitId = "Rook";
            rook.DisplayName = "Rook";
            rook.PieceType = PieceType.Rook;
            rook.CanCastle = true;
            rook.BaseCost = 5;

            rook.BaseStats = new UnitBaseStats
            {
                Health = 60,
                Attack = 10,
                Defense = 5,
                Speed = 5,
                Range = 1,
                Movement = 8
            };

            rook.GrowthStats = UnitGrowthStats.Default;

            rook.MovementPatterns = new List<MovementPattern>
            {
                new MovementPattern(MovementType.Orthogonal, -1)
            };

            rook.UnicodeWhite = ChessUnicode.WhiteRook;
            rook.UnicodeBlack = ChessUnicode.BlackRook;

            // 3D Rendering
            ApplyPieceTextures(rook, "Rook");

            return rook;
        }

        private static UnitDefinition CreateBishopDefinition()
        {
            var bishop = ScriptableObject.CreateInstance<UnitDefinition>();
            bishop.UnitId = "Bishop";
            bishop.DisplayName = "Bishop";
            bishop.PieceType = PieceType.Bishop;
            bishop.BaseCost = 3;

            bishop.BaseStats = new UnitBaseStats
            {
                Health = 40,
                Attack = 8,
                Defense = 2,
                Speed = 6,
                Range = 1,
                Movement = 8
            };

            bishop.GrowthStats = UnitGrowthStats.Default;

            bishop.MovementPatterns = new List<MovementPattern>
            {
                new MovementPattern(MovementType.Diagonal, -1)
            };

            bishop.UnicodeWhite = ChessUnicode.WhiteBishop;
            bishop.UnicodeBlack = ChessUnicode.BlackBishop;

            // 3D Rendering
            ApplyPieceTextures(bishop, "Bishop");

            return bishop;
        }

        private static UnitDefinition CreateKnightDefinition()
        {
            var knight = ScriptableObject.CreateInstance<UnitDefinition>();
            knight.UnitId = "Knight";
            knight.DisplayName = "Knight";
            knight.PieceType = PieceType.Knight;
            knight.BaseCost = 3;

            knight.BaseStats = new UnitBaseStats
            {
                Health = 45,
                Attack = 8,
                Defense = 3,
                Speed = 7,
                Range = 1,
                Movement = 1
            };

            knight.GrowthStats = UnitGrowthStats.Default;

            knight.MovementPatterns = new List<MovementPattern>
            {
                MovementPattern.Leaper(2, 1)  // Knight: L-shape (2,1)
            };

            knight.UnicodeWhite = ChessUnicode.WhiteKnight;
            knight.UnicodeBlack = ChessUnicode.BlackKnight;

            // 3D Rendering
            ApplyPieceTextures(knight, "Knight");

            return knight;
        }

        private static UnitDefinition CreateCamelDefinition()
        {
            var camel = ScriptableObject.CreateInstance<UnitDefinition>();
            camel.UnitId = "Camel";
            camel.DisplayName = "Camel";
            camel.PieceType = PieceType.Custom;
            camel.BaseCost = 2; // Slightly less valuable than knight (fewer moves on small boards)

            camel.BaseStats = new UnitBaseStats
            {
                Health = 40,
                Attack = 7,
                Defense = 2,
                Speed = 8,
                Range = 1,
                Movement = 1
            };

            camel.GrowthStats = UnitGrowthStats.Default;

            camel.MovementPatterns = new List<MovementPattern>
            {
                MovementPattern.Leaper(3, 1)  // Camel: extended L-shape (3,1)
            };

            // No standard unicode for Camel, use Knight as fallback
            camel.UnicodeWhite = ChessUnicode.WhiteKnight;
            camel.UnicodeBlack = ChessUnicode.BlackKnight;

            // 3D Rendering - will fallback to token if no texture
            ApplyPieceTextures(camel, "Camel");

            return camel;
        }

        private static UnitDefinition CreatePawnDefinition()
        {
            var pawn = ScriptableObject.CreateInstance<UnitDefinition>();
            pawn.UnitId = "Pawn";
            pawn.DisplayName = "Pawn";
            pawn.PieceType = PieceType.Pawn;
            pawn.CanPromote = true;
            pawn.CanEnPassant = true;
            pawn.PromotionRow = 7; // Will be mirrored for player 2
            pawn.BaseCost = 1;

            pawn.BaseStats = new UnitBaseStats
            {
                Health = 20,
                Attack = 5,
                Defense = 1,
                Speed = 4,
                Range = 1,
                Movement = 1
            };

            pawn.GrowthStats = new UnitGrowthStats
            {
                HealthPerLevel = 3,
                AttackPerLevel = 1,
                DefensePerLevel = 1,
                SpeedPerLevel = 0
            };

            pawn.MovementPatterns = new List<MovementPattern>
            {
                new MovementPattern(MovementType.Forward, 1) { MoveOnly = true },
                new MovementPattern(MovementType.Forward, 2) { MoveOnly = true, FirstMoveOnly = true },
                new MovementPattern(MovementType.DiagonalCapture) { CaptureOnly = true }
            };

            pawn.UnicodeWhite = ChessUnicode.WhitePawn;
            pawn.UnicodeBlack = ChessUnicode.BlackPawn;

            // 3D Rendering
            ApplyPieceTextures(pawn, "Pawn");

            return pawn;
        }

        #endregion

        #region Texture Loading

        /// <summary>
        /// Apply piece textures from Resources folder.
        /// Tries to load from Pieces/Displacement first, falls back to Pieces/Tokens, then Token2D with no texture.
        /// </summary>
        private static void ApplyPieceTextures(UnitDefinition unit, string pieceName)
        {
            // Try displacement texture first (3D from PNG)
            var displacementTex = Resources.Load<Texture2D>($"Pieces/Displacement/{pieceName}");
            if (displacementTex != null)
            {
                unit.RenderMode = PieceRenderMode.DisplacementMap;
                unit.DisplacementTexture = displacementTex;
                return;
            }

            // Try token texture (flat cylinder with PNG)
            var tokenTex = Resources.Load<Texture2D>($"Pieces/Tokens/{pieceName}");
            if (tokenTex != null)
            {
                unit.RenderMode = PieceRenderMode.Token2D;
                unit.TokenTexture = tokenTex;
                return;
            }

            // Fallback: Token2D with no texture (solid color cylinder)
            unit.RenderMode = PieceRenderMode.Token2D;
            unit.TokenTexture = null;
        }

        #endregion

    }
}

