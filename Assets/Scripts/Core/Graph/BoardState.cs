using System;
using System.Collections.Generic;

namespace BizarreChess.Core.Graph
{
    /// <summary>
    /// Runtime state of the board (mutable during match, synchronized in multiplayer).
    /// </summary>
    [Serializable]
    public class BoardState
    {
        public List<NodeState> Nodes;
        public List<EdgeState> Edges;

        // Adjacency list for fast lookups (rebuilt when edges change)
        [NonSerialized]
        private Dictionary<int, List<int>> _adjacencyList;
        [NonSerialized]
        private bool _adjacencyDirty = true;

        public BoardState()
        {
            Nodes = new List<NodeState>();
            Edges = new List<EdgeState>();
        }

        #region Node Operations

        public NodeState GetNode(int nodeId)
        {
            return Nodes[nodeId];
        }

        public void SetNode(int nodeId, NodeState state)
        {
            Nodes[nodeId] = state;
        }

        public bool IsNodePassable(int nodeId)
        {
            if (nodeId < 0 || nodeId >= Nodes.Count) return false;
            return Nodes[nodeId].IsPassable;
        }

        public void DestroyNode(int nodeId)
        {
            var node = Nodes[nodeId];
            node.CurrentType = NodeType.Destroyed;
            node.IsActive = false;
            Nodes[nodeId] = node;
        }

        public void ChangeNodeType(int nodeId, NodeType newType)
        {
            var node = Nodes[nodeId];
            node.CurrentType = newType;
            Nodes[nodeId] = node;
        }

        public void SetNodeUnstable(int nodeId, int turnsUntilDestruction)
        {
            var node = Nodes[nodeId];
            node.CurrentType = NodeType.Unstable;
            node.EffectDuration = turnsUntilDestruction;
            Nodes[nodeId] = node;
        }

        #endregion

        #region Edge Operations

        public void AddEdge(int fromNode, int toNode, bool bidirectional = true)
        {
            Edges.Add(new EdgeState
            {
                FromNodeId = fromNode,
                ToNodeId = toNode,
                IsActive = true,
                CurrentType = EdgeType.Normal
            });
            _adjacencyDirty = true;
        }

        public void RemoveEdge(int fromNode, int toNode)
        {
            for (int i = Edges.Count - 1; i >= 0; i--)
            {
                var edge = Edges[i];
                if ((edge.FromNodeId == fromNode && edge.ToNodeId == toNode) ||
                    (edge.FromNodeId == toNode && edge.ToNodeId == fromNode))
                {
                    edge.IsActive = false;
                    Edges[i] = edge;
                }
            }
            _adjacencyDirty = true;
        }

        public bool AreNodesConnected(int nodeA, int nodeB)
        {
            RebuildAdjacencyIfNeeded();
            if (_adjacencyList.TryGetValue(nodeA, out var neighbors))
            {
                return neighbors.Contains(nodeB);
            }
            return false;
        }

        #endregion

        #region Adjacency

        public List<int> GetAdjacentNodes(int nodeId)
        {
            RebuildAdjacencyIfNeeded();
            if (_adjacencyList.TryGetValue(nodeId, out var neighbors))
            {
                return neighbors;
            }
            return new List<int>();
        }

        public List<int> GetPassableAdjacentNodes(int nodeId)
        {
            var adjacent = GetAdjacentNodes(nodeId);
            var passable = new List<int>();
            foreach (var adjId in adjacent)
            {
                if (IsNodePassable(adjId))
                {
                    passable.Add(adjId);
                }
            }
            return passable;
        }

        private void RebuildAdjacencyIfNeeded()
        {
            if (!_adjacencyDirty && _adjacencyList != null) return;

            _adjacencyList = new Dictionary<int, List<int>>();
            
            foreach (var node in Nodes)
            {
                _adjacencyList[node.Id] = new List<int>();
            }

            foreach (var edge in Edges)
            {
                if (!edge.IsActive) continue;

                if (!_adjacencyList.ContainsKey(edge.FromNodeId))
                    _adjacencyList[edge.FromNodeId] = new List<int>();
                
                _adjacencyList[edge.FromNodeId].Add(edge.ToNodeId);

                // Add reverse direction for bidirectional edges
                if (edge.CurrentType != EdgeType.OneWay)
                {
                    if (!_adjacencyList.ContainsKey(edge.ToNodeId))
                        _adjacencyList[edge.ToNodeId] = new List<int>();
                    
                    _adjacencyList[edge.ToNodeId].Add(edge.FromNodeId);
                }
            }

            _adjacencyDirty = false;
        }

        public void InvalidateAdjacency()
        {
            _adjacencyDirty = true;
        }

        #endregion

        #region Turn Processing

        /// <summary>
        /// Process turn-based effects (unstable nodes, temporary effects, etc.)
        /// </summary>
        public void ProcessTurnEnd()
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                var node = Nodes[i];
                
                if (node.CurrentType == NodeType.Unstable && node.EffectDuration > 0)
                {
                    node.EffectDuration--;
                    if (node.EffectDuration <= 0)
                    {
                        node.CurrentType = NodeType.Destroyed;
                        node.IsActive = false;
                    }
                    Nodes[i] = node;
                }
            }
        }

        #endregion
    }
}

