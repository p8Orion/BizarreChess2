using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Units;
using BizarreChess.Core.Armies;

namespace BizarreChess.Core.Factories
{
    /// <summary>
    /// Factory for creating army definitions.
    /// Uses PiecesFactory for piece creation.
    /// </summary>
    public static class ArmyFactory
    {
        #region Classic Army

        /// <summary>
        /// Create the classic chess army (16 pieces in standard positions).
        /// </summary>
        public static ArmyDefinition CreateClassicArmy()
        {
            var units = PiecesFactory.CreateAllPieceDefinitions();
            return CreateClassicArmy(units);
        }

        /// <summary>
        /// Create the classic chess army using provided unit definitions.
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

        #region Custom Army Building

        /// <summary>
        /// Create an empty army definition to build upon.
        /// </summary>
        public static ArmyDefinition CreateEmptyArmy(string armyId, string displayName, string description = "")
        {
            var army = ScriptableObject.CreateInstance<ArmyDefinition>();
            army.ArmyId = armyId;
            army.DisplayName = displayName;
            army.Description = description;
            army.RequiresKing = true;
            army.Slots = new List<ArmySlot>();
            return army;
        }

        /// <summary>
        /// Create a custom army from a list of piece placements.
        /// </summary>
        public static ArmyDefinition CreateCustomArmy(
            string armyId,
            string displayName,
            string description,
            List<PiecePlacement> placements,
            bool requiresKing = true)
        {
            var army = CreateEmptyArmy(armyId, displayName, description);
            army.RequiresKing = requiresKing;

            foreach (var placement in placements)
            {
                var unit = PiecesFactory.CreatePieceDefinition(placement.PieceName);
                if (unit != null)
                {
                    army.Slots.Add(new ArmySlot(unit, placement.X, placement.Y, placement.Row));
                }
            }

            return army;
        }

        #endregion

        #region Variant Armies

        /// <summary>
        /// Create a pawn-less army (only major pieces).
        /// Useful for endgame practice.
        /// </summary>
        public static ArmyDefinition CreateMajorPiecesOnlyArmy()
        {
            var units = PiecesFactory.CreateAllPieceDefinitions();

            var army = CreateEmptyArmy(
                "major_pieces_only",
                "Major Pieces Only",
                "Army with only major pieces, no pawns"
            );

            // Same back row as classic
            army.Slots.Add(new ArmySlot(units["Rook"], 0, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(units["Knight"], 1, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(units["Bishop"], 2, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(units["Queen"], 3, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(units["King"], 4, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(units["Bishop"], 5, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(units["Knight"], 6, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(units["Rook"], 7, 0, SlotRow.Back));

            return army;
        }

        /// <summary>
        /// Create a minimal army with just King and Pawns.
        /// </summary>
        public static ArmyDefinition CreateKingAndPawnsArmy()
        {
            var units = PiecesFactory.CreateAllPieceDefinitions();

            var army = CreateEmptyArmy(
                "king_and_pawns",
                "King and Pawns",
                "Minimal army with only King and Pawns"
            );

            army.Slots.Add(new ArmySlot(units["King"], 4, 0, SlotRow.Back));

            for (int x = 0; x < 8; x++)
            {
                army.Slots.Add(new ArmySlot(units["Pawn"], x, 1, SlotRow.Front));
            }

            return army;
        }

        /// <summary>
        /// Create a horde army (lots of pawns).
        /// </summary>
        public static ArmyDefinition CreateHordeArmy(int rows = 4)
        {
            var units = PiecesFactory.CreateAllPieceDefinitions();

            var army = CreateEmptyArmy(
                "horde",
                "Horde",
                $"Army with {rows} rows of pawns"
            );
            army.RequiresKing = false; // Horde doesn't need a king

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    army.Slots.Add(new ArmySlot(units["Pawn"], x, y, y == 0 ? SlotRow.Back : SlotRow.Front));
                }
            }

            return army;
        }

        #endregion
    }

    /// <summary>
    /// Simple struct for defining piece placements when building custom armies.
    /// </summary>
    public struct PiecePlacement
    {
        public string PieceName;
        public int X;
        public int Y;
        public SlotRow Row;

        public PiecePlacement(string pieceName, int x, int y, SlotRow row = SlotRow.Back)
        {
            PieceName = pieceName;
            X = x;
            Y = y;
            Row = row;
        }
    }
}

