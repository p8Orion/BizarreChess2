using System.Collections.Generic;
using BizarreChess.Core.Graph;
using BizarreChess.Core.Units;
using BizarreChess.Core.Armies;

namespace BizarreChess.Core.Factories
{
    /// <summary>
    /// High-level factory for creating complete chess game setups.
    /// Combines BoardFactory, PiecesFactory, and ArmyFactory for convenience.
    /// All components have sensible defaults but can be customized.
    /// </summary>
    public static class ChessFactory
    {
        #region Complete Setup

        /// <summary>
        /// Create a complete chess setup with all defaults (classic 8x8 board, classic pieces, classic army).
        /// </summary>
        public static ChessSetup CreateDefaultSetup()
        {
            return CreateSetup();
        }

        /// <summary>
        /// Create a complete chess setup with optional customizations.
        /// </summary>
        /// <param name="board">Custom board, or null for classic 8x8</param>
        /// <param name="pieces">Custom piece definitions, or null for classic pieces</param>
        /// <param name="playerArmies">Armies for each player, or null for classic army for all</param>
        public static ChessSetup CreateSetup(
            BoardDefinition board = null,
            Dictionary<string, UnitDefinition> pieces = null,
            List<ArmyDefinition> playerArmies = null)
        {
            pieces ??= PiecesFactory.CreateAllPieceDefinitions();
            board ??= BoardFactory.CreateClassicBoard();
            
            if (playerArmies == null)
            {
                var defaultArmy = ArmyFactory.CreateClassicArmy(pieces);
                playerArmies = new List<ArmyDefinition> { defaultArmy, defaultArmy };
            }

            return new ChessSetup
            {
                Board = board,
                Pieces = pieces,
                PlayerArmies = playerArmies
            };
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

        #region Pieces Shortcuts

        /// <summary>
        /// Create all classic chess pieces.
        /// </summary>
        public static Dictionary<string, UnitDefinition> CreatePieces()
            => PiecesFactory.CreateAllPieceDefinitions();

        /// <summary>
        /// Create a specific piece by name.
        /// </summary>
        public static UnitDefinition CreatePiece(string name)
            => PiecesFactory.CreatePieceDefinition(name);

        #endregion

        #region Army Shortcuts

        /// <summary>
        /// Create classic 16-piece army.
        /// </summary>
        public static ArmyDefinition CreateArmy() => ArmyFactory.CreateClassicArmy();

        /// <summary>
        /// Create classic army with specific pieces.
        /// </summary>
        public static ArmyDefinition CreateArmy(Dictionary<string, UnitDefinition> pieces)
            => ArmyFactory.CreateClassicArmy(pieces);

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
            var pieces = PiecesFactory.CreateAllPieceDefinitions();
            var board = BoardFactory.CreateMiniBoard();
            // TODO: Create appropriate mini army (less pieces)
            var army = ArmyFactory.CreateClassicArmy(pieces);

            return new ChessSetup
            {
                Board = board,
                Pieces = pieces,
                PlayerArmies = new List<ArmyDefinition> { army, army }
            };
        }

        /// <summary>
        /// Create a grand chess setup (10x10 board).
        /// </summary>
        public static ChessSetup CreateGrandSetup()
        {
            var pieces = PiecesFactory.CreateAllPieceDefinitions();
            var board = BoardFactory.CreateGrandBoard();
            // TODO: Create appropriate grand army (more pieces)
            var army = ArmyFactory.CreateClassicArmy(pieces);

            return new ChessSetup
            {
                Board = board,
                Pieces = pieces,
                PlayerArmies = new List<ArmyDefinition> { army, army }
            };
        }

        /// <summary>
        /// Create Capablanca chess setup (10x8 board).
        /// </summary>
        public static ChessSetup CreateCapablancaSetup()
        {
            var pieces = PiecesFactory.CreateAllPieceDefinitions();
            var board = BoardFactory.CreateCapablancaBoard();
            // TODO: Add Archbishop and Chancellor pieces, create Capablanca army
            var army = ArmyFactory.CreateClassicArmy(pieces);

            return new ChessSetup
            {
                Board = board,
                Pieces = pieces,
                PlayerArmies = new List<ArmyDefinition> { army, army }
            };
        }

        /// <summary>
        /// Create a horde mode setup (Player 1 = classic, Player 2 = horde).
        /// </summary>
        public static ChessSetup CreateHordeSetup()
        {
            var pieces = PiecesFactory.CreateAllPieceDefinitions();
            var board = BoardFactory.CreateClassicBoard();

            var defenderArmy = ArmyFactory.CreateClassicArmy(pieces);
            var hordeArmy = ArmyFactory.CreateHordeArmy(4);

            return new ChessSetup
            {
                Board = board,
                Pieces = pieces,
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
        public Dictionary<string, UnitDefinition> Pieces;
        
        /// <summary>
        /// Armies for each player. Index 0 = Player 1, Index 1 = Player 2, etc.
        /// </summary>
        public List<ArmyDefinition> PlayerArmies;

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
    }
}

