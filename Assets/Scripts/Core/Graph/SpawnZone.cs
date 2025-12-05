using System;
using System.Collections.Generic;

namespace BizarreChess.Core.Graph
{
    /// <summary>
    /// Defines where a player can place their units at the start of a match.
    /// This is a property of the MAP, not the army.
    /// </summary>
    [Serializable]
    public class SpawnZone
    {
        public int PlayerSlot;                    // 0 = Player 1, 1 = Player 2, etc.
        public List<int> SpawnNodeIds;            // All valid spawn nodes
        public List<int> BackRowNodeIds;          // For major pieces (optional subset)
        public List<int> FrontRowNodeIds;         // For pawns (optional subset)

        public SpawnZone()
        {
            SpawnNodeIds = new List<int>();
            BackRowNodeIds = new List<int>();
            FrontRowNodeIds = new List<int>();
        }

        public SpawnZone(int playerSlot, List<int> allNodes)
        {
            PlayerSlot = playerSlot;
            SpawnNodeIds = allNodes;
            BackRowNodeIds = new List<int>();
            FrontRowNodeIds = new List<int>();
        }

        public SpawnZone(int playerSlot, List<int> backRow, List<int> frontRow)
        {
            PlayerSlot = playerSlot;
            BackRowNodeIds = backRow;
            FrontRowNodeIds = frontRow;
            SpawnNodeIds = new List<int>();
            SpawnNodeIds.AddRange(backRow);
            SpawnNodeIds.AddRange(frontRow);
        }
    }

    /// <summary>
    /// How units are placed at the start of a match.
    /// </summary>
    public enum PlacementMode
    {
        Automatic,  // Army defines relative positions, map maps them to nodes
        Manual      // Interactive placement phase where player chooses positions
    }
}

