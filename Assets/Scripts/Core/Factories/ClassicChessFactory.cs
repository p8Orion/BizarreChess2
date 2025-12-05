using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Graph;
using BizarreChess.Core.Units;
using BizarreChess.Core.Armies;

namespace BizarreChess.Core.Factories
{
    /// <summary>
    /// Factory for creating classic 8x8 chess board and army.
    /// </summary>
    public static class ClassicChessFactory
    {
        #region Board Creation

        /// <summary>
        /// Create a standard 8x8 chess board definition.
        /// </summary>
        public static BoardDefinition CreateClassicBoard()
        {
            var board = ScriptableObject.CreateInstance<BoardDefinition>();
            board.BoardId = "classic_8x8";
            board.DisplayName = "Classic Chess Board";
            board.Width = 8;
            board.Height = 8;
            board.PlacementMode = PlacementMode.Automatic;
            board.Nodes = new List<NodeDefinition>();
            board.Edges = new List<EdgeDefinition>();
            board.SpawnZones = new List<SpawnZone>();

            // Create 64 nodes
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int id = y * 8 + x;
                    bool isLight = (x + y) % 2 == 1;

                    board.Nodes.Add(new NodeDefinition(
                        id: id,
                        position: new Vector2(x, y),
                        type: NodeType.Normal,
                        isLight: isLight
                    ));
                }
            }

            // Create edges (8-way connectivity for king movement, other pieces use patterns)
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int id = y * 8 + x;

                    // Connect to right neighbor
                    if (x < 7)
                    {
                        board.Edges.Add(new EdgeDefinition(id, id + 1));
                    }

                    // Connect to top neighbor
                    if (y < 7)
                    {
                        board.Edges.Add(new EdgeDefinition(id, id + 8));
                    }

                    // Connect to top-right diagonal
                    if (x < 7 && y < 7)
                    {
                        board.Edges.Add(new EdgeDefinition(id, id + 9));
                    }

                    // Connect to top-left diagonal
                    if (x > 0 && y < 7)
                    {
                        board.Edges.Add(new EdgeDefinition(id, id + 7));
                    }
                }
            }

            // Create spawn zones
            // Player 1: rows 0-1 (nodes 0-15)
            var player1BackRow = new List<int> { 0, 1, 2, 3, 4, 5, 6, 7 };
            var player1FrontRow = new List<int> { 8, 9, 10, 11, 12, 13, 14, 15 };

            // Player 2: rows 6-7 (nodes 48-63)
            var player2FrontRow = new List<int> { 48, 49, 50, 51, 52, 53, 54, 55 };
            var player2BackRow = new List<int> { 56, 57, 58, 59, 60, 61, 62, 63 };

            board.SpawnZones.Add(new SpawnZone(0, player1BackRow, player1FrontRow));
            board.SpawnZones.Add(new SpawnZone(1, player2BackRow, player2FrontRow));

            return board;
        }

        #endregion

        #region Unit Definitions

        public static UnitDefinition CreateKingDefinition()
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

            return king;
        }

        public static UnitDefinition CreateQueenDefinition()
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

            return queen;
        }

        public static UnitDefinition CreateRookDefinition()
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

            return rook;
        }

        public static UnitDefinition CreateBishopDefinition()
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

            return bishop;
        }

        public static UnitDefinition CreateKnightDefinition()
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
                new MovementPattern(MovementType.Knight) { CanJump = true }
            };

            knight.UnicodeWhite = ChessUnicode.WhiteKnight;
            knight.UnicodeBlack = ChessUnicode.BlackKnight;

            return knight;
        }

        public static UnitDefinition CreatePawnDefinition()
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

            return pawn;
        }

        /// <summary>
        /// Get all classic unit definitions.
        /// </summary>
        public static Dictionary<string, UnitDefinition> CreateAllUnitDefinitions()
        {
            return new Dictionary<string, UnitDefinition>
            {
                { "King", CreateKingDefinition() },
                { "Queen", CreateQueenDefinition() },
                { "Rook", CreateRookDefinition() },
                { "Bishop", CreateBishopDefinition() },
                { "Knight", CreateKnightDefinition() },
                { "Pawn", CreatePawnDefinition() }
            };
        }

        #endregion

        #region Army Creation

        /// <summary>
        /// Create the classic chess army (16 pieces in standard positions).
        /// </summary>
        public static ArmyDefinition CreateClassicArmy(Dictionary<string, UnitDefinition> units)
        {
            var army = ScriptableObject.CreateInstance<ArmyDefinition>();
            army.ArmyId = "classic_chess_army";
            army.DisplayName = "Classic Chess Army";
            army.Description = "Standard chess army with 16 pieces";
            army.RequiresKing = true;
            army.Slots = new List<ArmySlot>();

            // Back row (y=0): Rook, Knight, Bishop, Queen, King, Bishop, Knight, Rook
            army.Slots.Add(new ArmySlot(units["Rook"], 0, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(units["Knight"], 1, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(units["Bishop"], 2, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(units["Queen"], 3, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(units["King"], 4, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(units["Bishop"], 5, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(units["Knight"], 6, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(units["Rook"], 7, 0, SlotRow.Back));

            // Front row (y=1): 8 pawns
            for (int x = 0; x < 8; x++)
            {
                army.Slots.Add(new ArmySlot(units["Pawn"], x, 1, SlotRow.Front));
            }

            return army;
        }

        #endregion

        #region Convenience Methods

        /// <summary>
        /// Create everything needed for a classic chess game.
        /// </summary>
        public static ClassicChessSetup CreateCompleteSetup()
        {
            var units = CreateAllUnitDefinitions();
            var board = CreateClassicBoard();
            var army = CreateClassicArmy(units);

            return new ClassicChessSetup
            {
                Board = board,
                UnitDefinitions = units,
                DefaultArmy = army
            };
        }

        #endregion
    }

    /// <summary>
    /// Container for all classic chess assets.
    /// </summary>
    public class ClassicChessSetup
    {
        public BoardDefinition Board;
        public Dictionary<string, UnitDefinition> UnitDefinitions;
        public ArmyDefinition DefaultArmy;
    }
}

