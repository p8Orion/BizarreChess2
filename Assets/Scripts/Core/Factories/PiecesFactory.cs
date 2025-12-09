using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Units;
using BizarreChess.Core.Skills;

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
            var allNames = new[] { "King", "Queen", "Rook", "Bishop", "Knight", "Camel", "Crossbowman", "Cannon", "Pawn", "Lancer", "Defender" };
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
                "Crossbowman" => CreateCrossbowmanDefinition(),
                "Cannon" => CreateCannonDefinition(),
                "Pawn" => CreatePawnDefinition(),
                "Lancer" => CreateLancerDefinition(),
                "Defender" => CreateDefenderDefinition(),
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

        private static UnitDefinition CreateCrossbowmanDefinition()
        {
            var crossbowman = ScriptableObject.CreateInstance<UnitDefinition>();
            crossbowman.UnitId = "Crossbowman";
            crossbowman.DisplayName = "Crossbowman";
            crossbowman.PieceType = PieceType.Custom;
            crossbowman.BaseCost = 4;

            crossbowman.MovementPatterns = new List<MovementPattern>
            {
                // Moves like a king (1 square any direction)
                new MovementPattern(MovementType.Adjacent, 1) { MoveOnly = true },
                // Captures diagonally up to 3 squares, can shoot over abysses, unit doesn't move when capturing
                new MovementPattern(MovementType.DiagonalLeaper, 3) { CaptureOnly = true, RangedCapture = true }
            };

            // No standard unicode for Crossbowman, use Pawn as fallback
            crossbowman.UnicodeWhite = '♙';
            crossbowman.UnicodeBlack = '♟';

            // 3D Rendering
            ApplyPieceTextures(crossbowman, "Crossbowman");

            return crossbowman;
        }

        private static UnitDefinition CreateCannonDefinition()
        {
            var cannon = ScriptableObject.CreateInstance<UnitDefinition>();
            cannon.UnitId = "Cannon";
            cannon.DisplayName = "Cannon";
            cannon.PieceType = PieceType.Custom;
            cannon.BaseCost = 5;

            cannon.MovementPatterns = new List<MovementPattern>
            {
                // Moves like a rook (orthogonal, unlimited)
                new MovementPattern(MovementType.Orthogonal, 1) { MoveOnly = true },
                // Fires orthogonally at range 2-4, doesn't move when capturing
                new MovementPattern(MovementType.Orthogonal, 4) { MinDistance = 2, CaptureOnly = true, RangedCapture = true }
            };

            // Use Rook unicode as fallback
            cannon.UnicodeWhite = ChessUnicode.WhiteRook;
            cannon.UnicodeBlack = ChessUnicode.BlackRook;

            // 3D Rendering
            ApplyPieceTextures(cannon, "Cannon");

            return cannon;
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

        private static UnitDefinition CreateLancerDefinition()
        {
            var lancer = ScriptableObject.CreateInstance<UnitDefinition>();
            lancer.UnitId = "Lancer";
            lancer.DisplayName = "Lancer";
            lancer.PieceType = PieceType.Pawn; // Pawn variant
            lancer.CanPromote = true;
            lancer.CanEnPassant = false; // No en passant for lancer
            lancer.PromotionRow = 7;
            lancer.BaseCost = 1;

            lancer.MovementPatterns = new List<MovementPattern>
            {
                // Moves AND captures forward (unlike pawn which captures diagonally)
                new MovementPattern(MovementType.Forward, 1),
                new MovementPattern(MovementType.Forward, 2) { FirstMoveOnly = true }
            };

            // Use pawn unicode as fallback
            lancer.UnicodeWhite = ChessUnicode.WhitePawn;
            lancer.UnicodeBlack = ChessUnicode.BlackPawn;

            // 3D Rendering - will fallback to token if no texture
            ApplyPieceTextures(lancer, "Lancer");

            return lancer;
        }

        private static UnitDefinition CreateDefenderDefinition()
        {
            var defender = ScriptableObject.CreateInstance<UnitDefinition>();
            defender.UnitId = "Defender";
            defender.DisplayName = "Defender";
            defender.PieceType = PieceType.Pawn; // Pawn variant
            defender.CanPromote = true;
            defender.CanEnPassant = false; // No en passant for defender
            defender.PromotionRow = 7;
            defender.BaseCost = 2; // Slightly more expensive due to forcefield

            // Same as pawn but NO double move on first turn
            defender.MovementPatterns = new List<MovementPattern>
            {
                new MovementPattern(MovementType.Forward, 1) { MoveOnly = true },
                new MovementPattern(MovementType.Forward, 2) { FirstMoveOnly = true }
            };

            // Forcefield skill - blocks one capture
            defender.Skills = new List<Skill>
            {
                new ForcefieldSkill()
            };

            // Use pawn unicode as fallback
            defender.UnicodeWhite = ChessUnicode.WhitePawn;
            defender.UnicodeBlack = ChessUnicode.BlackPawn;

            // 3D Rendering - will fallback to token if no texture
            ApplyPieceTextures(defender, "Defender");

            return defender;
        }

        #endregion

        #region Texture Loading

        /// <summary>
        /// Apply piece textures from Resources folder.
        /// Priority order:
        /// 1. Revolution (Pieces/Revolution/) - lathe-like 3D volume
        /// 2. Displacement (Pieces/Displacement/) - cylindrical displacement map
        /// 3. Token (Pieces/Tokens/) - flat cylinder with PNG on top
        /// 4. Fallback: Token2D with no texture (solid color cylinder)
        /// </summary>
        private static void ApplyPieceTextures(UnitDefinition unit, string pieceName)
        {
            // Try revolution texture first (lathe-like 3D from profile PNG)
            var revolutionTex = Resources.Load<Texture2D>($"Pieces/Revolution/{pieceName}");
            if (revolutionTex != null)
            {
                unit.RenderMode = PieceRenderMode.RevolutionVolume;
                unit.RevolutionTexture = revolutionTex;
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
