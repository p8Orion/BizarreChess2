using System.Collections.Generic;
using UnityEngine;

namespace BizarreChess.Core.Graph
{
    /// <summary>
    /// ScriptableObject that defines a board template (immutable map data).
    /// </summary>
    [CreateAssetMenu(fileName = "NewBoard", menuName = "Bizarre Chess/Board Definition")]
    public class BoardDefinition : ScriptableObject
    {
        [Header("Board Info")]
        public string BoardId;
        public string DisplayName;

        [Header("Graph Structure")]
        public List<NodeDefinition> Nodes;
        public List<EdgeDefinition> Edges;

        [Header("Spawn Configuration")]
        public List<SpawnZone> SpawnZones;
        public PlacementMode PlacementMode = PlacementMode.Automatic;

        [Header("Board Properties")]
        public int Width = 8;
        public int Height = 8;

        /// <summary>
        /// Gets node ID from grid coordinates (for grid-based boards).
        /// </summary>
        public int GetNodeId(int x, int y)
        {
            return y * Width + x;
        }

        /// <summary>
        /// Gets grid coordinates from node ID.
        /// </summary>
        public Vector2Int GetCoordinates(int nodeId)
        {
            return new Vector2Int(nodeId % Width, nodeId / Width);
        }

        /// <summary>
        /// Creates the initial runtime state from this definition.
        /// </summary>
        public BoardState CreateInitialState()
        {
            var state = new BoardState();
            
            foreach (var nodeDef in Nodes)
            {
                state.Nodes.Add(NodeState.FromDefinition(nodeDef));
            }
            
            foreach (var edgeDef in Edges)
            {
                state.Edges.Add(EdgeState.FromDefinition(edgeDef));
            }
            
            return state;
        }

        /// <summary>
        /// Validates that spawn zones match the graph structure.
        /// </summary>
        public bool ValidateSpawnZones()
        {
            var nodeIds = new HashSet<int>();
            foreach (var node in Nodes)
            {
                nodeIds.Add(node.Id);
            }

            foreach (var zone in SpawnZones)
            {
                foreach (var nodeId in zone.SpawnNodeIds)
                {
                    if (!nodeIds.Contains(nodeId))
                        return false;
                }
            }
            return true;
        }
    }
}

