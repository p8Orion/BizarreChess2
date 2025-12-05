using System.Collections.Generic;
using BizarreChess.Core.Graph;
using BizarreChess.Core.Units;

namespace BizarreChess.Core.Armies
{
    /// <summary>
    /// Places army units on the board according to spawn zones.
    /// </summary>
    public static class ArmyPlacer
    {
        /// <summary>
        /// Place an army on the board according to spawn zone rules.
        /// </summary>
        public static List<UnitState> PlaceArmy(
            ArmyDefinition army,
            SpawnZone spawnZone,
            int playerId,
            int startingUnitId,
            bool isSecondPlayer)
        {
            var units = new List<UnitState>();
            int unitId = startingUnitId;

            foreach (var slot in army.Slots)
            {
                if (slot.Unit == null) continue;

                int nodeId = MapSlotToNode(slot, spawnZone, isSecondPlayer);
                
                if (nodeId >= 0)
                {
                    var unit = UnitState.Create(unitId++, slot.Unit, playerId, nodeId);
                    units.Add(unit);
                }
            }

            return units;
        }

        /// <summary>
        /// Map a slot's relative position to an actual node ID.
        /// </summary>
        private static int MapSlotToNode(ArmySlot slot, SpawnZone zone, bool mirror)
        {
            // Get the appropriate row
            var row = slot.Row == SlotRow.Back ? zone.BackRowNodeIds : zone.FrontRowNodeIds;
            
            // If no specific rows defined, use all spawn nodes
            if (row == null || row.Count == 0)
            {
                row = zone.SpawnNodeIds;
            }

            if (row == null || row.Count == 0)
                return -1;

            // Map relative X position to row index
            int index = slot.RelativePosition.x;
            
            // Mirror for second player
            if (mirror)
            {
                index = row.Count - 1 - index;
            }

            // Clamp to valid range
            if (index < 0 || index >= row.Count)
                return -1;

            return row[index];
        }

        /// <summary>
        /// Create units for manual placement phase (not placed on board yet).
        /// </summary>
        public static List<UnitState> CreateUnitsForPlacement(
            ArmyDefinition army,
            int playerId,
            int startingUnitId)
        {
            var units = new List<UnitState>();
            int unitId = startingUnitId;

            foreach (var slot in army.Slots)
            {
                if (slot.Unit == null) continue;

                var unit = UnitState.Create(unitId++, slot.Unit, playerId, -1); // -1 = not placed
                units.Add(unit);
            }

            return units;
        }

        /// <summary>
        /// Validate that a node is a valid placement for a player.
        /// </summary>
        public static bool IsValidPlacement(int nodeId, SpawnZone zone, BoardState boardState)
        {
            if (!zone.SpawnNodeIds.Contains(nodeId))
                return false;

            if (!boardState.IsNodePassable(nodeId))
                return false;

            return true;
        }
    }
}

