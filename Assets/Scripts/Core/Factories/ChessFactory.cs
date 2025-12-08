using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Board;
using BizarreChess.Core.Units;
using BizarreChess.Core.Armies;
using BizarreChess.Core.Player;

namespace BizarreChess.Core.Factories
{
    /// <summary>
    /// High-level factory for creating complete chess game setups.
    /// Combines BoardFactory and ArmyFactory for convenience.
    /// Piece definitions are managed internally by ArmyFactory via PiecesFactory.
    /// </summary>
    public static class ChessFactory
    {
        #region Complete Setup

        /// <summary>
        /// Create a complete chess setup with all defaults (classic 8x8 board, classic vs camel army).
        /// Uses a random seed for board generation.
        /// </summary>
        public static ChessSetup CreateDefaultSetup()
        {
            return CreateDefaultSetupWithSeed(-1);
        }
        
        /// <summary>
        /// Create a complete chess setup with a specific seed for reproducible board generation.
        /// Use this for networked games to ensure all clients have identical boards.
        /// </summary>
        /// <param name="seed">Random seed for board generation. Use -1 for random seed.</param>
        public static ChessSetup CreateDefaultSetupWithSeed(int seed)
        {
            var board = BoardFactory.CreateBoardWithAbyss(
                sizeX: 8,
                sizeY: 10,
                abyssPercentage: 5,  // 20% de los tiles elegibles serán abismo
                impassablePercentage: 5,
                seed: seed            // Seed para reproducibilidad
            );
            var classicArmy = ArmyFactory.CreateClassicArmy();
            var camelArmy = ArmyFactory.Get("camel_army");  // TODO: Create a camel army

            var setup = new ChessSetup
            {   
                Board = board,
                PlayerArmies = new List<ArmyDefinition> { camelArmy, camelArmy },
                PlayerColorSchemes = new List<PlayerColorScheme> { 
                    new PlayerColorScheme(new Color(0f, 0.75f, 0), new Color(0f, 0.75f, 0.75f), "Wood4"), 
                    new PlayerColorScheme(new Color(0.75f, 0, 0), new Color(0.75f, 0.75f, 0f), "Wood5"),   
                }
            };
            
            setup.ApplyColors();
            return setup;
        }

        /// <summary>
        /// Create a complete chess setup with optional customizations.
        /// </summary>
        /// <param name="board">Custom board, or null for classic 8x8</param>
        /// <param name="playerArmies">Armies for each player, or null for classic army for all</param>
        /// <param name="playerColors">Color schemes for each player, or null for defaults</param>
        public static ChessSetup CreateSetup(
            BoardDefinition board = null,
            List<ArmyDefinition> playerArmies = null,
            List<PlayerColorScheme> playerColors = null)
        {
            board ??= BoardFactory.CreateClassicBoard();
            
            if (playerArmies == null)
            {
                var defaultArmy = ArmyFactory.CreateClassicArmy();
                playerArmies = new List<ArmyDefinition> { defaultArmy, defaultArmy };
            }

            var setup = new ChessSetup
            {
                Board = board,
                PlayerArmies = playerArmies,
                PlayerColorSchemes = playerColors
            };
            
            // Apply colors immediately
            setup.ApplyColors();
            
            return setup;
        }

        /// <summary>
        /// Create setup with custom player colors.
        /// </summary>
        public static ChessSetup CreateSetup(
            Color player1Primary, Color player1Secondary,
            Color player2Primary, Color player2Secondary,
            BoardDefinition board = null,
            List<ArmyDefinition> playerArmies = null)
        {
            var colors = new List<PlayerColorScheme>
            {
                new PlayerColorScheme(player1Primary, player1Secondary),
                new PlayerColorScheme(player2Primary, player2Secondary)
            };
            
            return CreateSetup(board, playerArmies, colors);
        }

        #endregion

        #region Board Shortcuts

        /// <summary>
        /// Create classic 8x8 board.
        /// </summary>
        public static BoardDefinition CreateBoard() => BoardFactory.CreateClassicBoard();

        /// <summary>
        /// Create rectangular board with custom dimensions.
        /// </summary>
        public static BoardDefinition CreateBoard(int width, int height, string id = null, string name = null)
            => BoardFactory.CreateRectangularBoard(width, height, id, name);

        #endregion

        #region Army Shortcuts

        /// <summary>
        /// Create classic 16-piece army.
        /// </summary>
        public static ArmyDefinition CreateArmy() => ArmyFactory.CreateClassicArmy();

        /// <summary>
        /// Create custom army from placements.
        /// </summary>
        public static ArmyDefinition CreateArmy(
            string id,
            string name,
            string description,
            List<PiecePlacement> placements,
            bool requiresKing = true)
            => ArmyFactory.CreateCustomArmy(id, name, description, placements, requiresKing);

        #endregion

        #region Preset Setups

        /// <summary>
        /// Create a classic chess setup (8x8 board, standard pieces and positions).
        /// </summary>
        public static ChessSetup CreateClassicSetup() => CreateDefaultSetup();

        /// <summary>
        /// Create a mini chess setup (6x6 board).
        /// </summary>
        public static ChessSetup CreateMiniSetup()
        {
            var board = BoardFactory.CreateMiniBoard();
            // TODO: Create appropriate mini army (less pieces)
            var army = ArmyFactory.CreateClassicArmy();

            return new ChessSetup
            {
                Board = board,
                PlayerArmies = new List<ArmyDefinition> { army, army }
            };
        }

        /// <summary>
        /// Create a grand chess setup (10x10 board).
        /// </summary>
        public static ChessSetup CreateGrandSetup()
        {
            var board = BoardFactory.CreateGrandBoard();
            // TODO: Create appropriate grand army (more pieces)
            var army = ArmyFactory.CreateClassicArmy();

            return new ChessSetup
            {
                Board = board,
                PlayerArmies = new List<ArmyDefinition> { army, army }
            };
        }

        /// <summary>
        /// Create Capablanca chess setup (10x8 board).
        /// </summary>
        public static ChessSetup CreateCapablancaSetup()
        {
            var board = BoardFactory.CreateCapablancaBoard();
            // TODO: Add Archbishop and Chancellor pieces, create Capablanca army
            var army = ArmyFactory.CreateClassicArmy();

            return new ChessSetup
            {
                Board = board,
                PlayerArmies = new List<ArmyDefinition> { army, army }
            };
        }

        /// <summary>
        /// Create a horde mode setup (Player 1 = classic, Player 2 = horde).
        /// </summary>
        public static ChessSetup CreateHordeSetup()
        {
            var board = BoardFactory.CreateClassicBoard();
            var defenderArmy = ArmyFactory.CreateClassicArmy();
            var hordeArmy = ArmyFactory.CreateHordeArmy(4);

            return new ChessSetup
            {
                Board = board,
                PlayerArmies = new List<ArmyDefinition> { defenderArmy, hordeArmy }
            };
        }

        #endregion
    }

    /// <summary>
    /// Container for a complete chess game setup.
    /// </summary>
    public class ChessSetup
    {
        public BoardDefinition Board;
        
        /// <summary>
        /// Armies for each player. Index 0 = Player 1, Index 1 = Player 2, etc.
        /// </summary>
        public List<ArmyDefinition> PlayerArmies;
        
        /// <summary>
        /// Color schemes for each player. If null, uses defaults.
        /// </summary>
        public List<PlayerColorScheme> PlayerColorSchemes;
        
        /// <summary>
        /// Board skin (tile textures/colors). If null, uses default.
        /// </summary>
        public BoardSkin BoardSkin;

        /// <summary>
        /// Get all piece definitions used in this setup (from PiecesFactory cache).
        /// </summary>
        public Dictionary<string, UnitDefinition> Pieces => PiecesFactory.GetAll();

        // Alias for backwards compatibility
        public Dictionary<string, UnitDefinition> UnitDefinitions => Pieces;

        /// <summary>
        /// Get army for a specific player.
        /// </summary>
        public ArmyDefinition GetArmy(int playerIndex)
        {
            if (PlayerArmies == null || PlayerArmies.Count == 0)
                return null;
            
            // If only one army defined, all players use it
            if (playerIndex >= PlayerArmies.Count)
                return PlayerArmies[0];
            
            return PlayerArmies[playerIndex];
        }
        
        /// <summary>
        /// Get color scheme for a specific player.
        /// </summary>
        public PlayerColorScheme GetColorScheme(int playerIndex)
        {
            if (PlayerColorSchemes == null || PlayerColorSchemes.Count == 0)
                return PlayerColors.Get(playerIndex);
            
            if (playerIndex >= PlayerColorSchemes.Count)
                return PlayerColorSchemes[0];
            
            return PlayerColorSchemes[playerIndex];
        }
        
        /// <summary>
        /// Apply this setup's color schemes to the global PlayerColors.
        /// Call this before rendering the game.
        /// </summary>
        public void ApplyColors()
        {
            // Apply player colors
            if (PlayerColorSchemes == null || PlayerColorSchemes.Count == 0)
            {
                PlayerColors.ResetToDefaults();
            }
            else
            {
                for (int i = 0; i < PlayerColorSchemes.Count; i++)
                {
                    var scheme = PlayerColorSchemes[i];
                    PlayerColors.Set(i, scheme.PrimaryColor, scheme.SecondaryColor, scheme.PieceTexture);
                }
            }
            
            // Apply board skin
            ApplyBoardSkin();
        }
        
        /// <summary>
        /// Apply this setup's board skin to the global BoardSkins.
        /// </summary>
        public void ApplyBoardSkin()
        {
            if (BoardSkin != null)
            {
                BoardSkins.SetSkin(BoardSkin);
            }
            else
            {
                BoardSkins.ResetToDefault();
            }
        }
    }
}
