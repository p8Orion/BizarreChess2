using System.Collections.Generic;
using UnityEngine;

namespace BizarreChess.Core.Graph
{
    /// <summary>
    /// High-level API for querying and operating on the board graph.
    /// Wraps BoardState with pathfinding and movement queries.
    /// </summary>
    public class BoardGraph
    {
        public BoardDefinition Definition { get; private set; }
        public BoardState State { get; private set; }

        public BoardGraph(BoardDefinition definition)
        {
            Definition = definition;
            State = definition.CreateInitialState();
        }

        public BoardGraph(BoardDefinition definition, BoardState existingState)
        {
            Definition = definition;
            State = existingState;
        }

        #region Node Queries

        public NodeState GetNode(int nodeId) => State.GetNode(nodeId);
        
        public NodeDefinition GetNodeDefinition(int nodeId) => Definition.Nodes[nodeId];
        
        public bool IsPassable(int nodeId) => State.IsNodePassable(nodeId);

        public Vector2 GetNodePosition(int nodeId) => Definition.Nodes[nodeId].Position;

        public int GetNodeIdAtPosition(int x, int y) => Definition.GetNodeId(x, y);

        public Vector2Int GetNodeCoordinates(int nodeId) => Definition.GetCoordinates(nodeId);

        #endregion

        #region Pathfinding

        /// <summary>
        /// Find shortest path between two nodes using BFS.
        /// Returns empty list if no path exists.
        /// </summary>
        public List<int> FindPath(int fromNode, int toNode)
        {
            if (fromNode == toNode) return new List<int> { fromNode };
            if (!IsPassable(toNode)) return new List<int>();

            var visited = new HashSet<int>();
            var queue = new Queue<int>();
            var cameFrom = new Dictionary<int, int>();

            queue.Enqueue(fromNode);
            visited.Add(fromNode);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();

                foreach (int neighbor in State.GetPassableAdjacentNodes(current))
                {
                    if (visited.Contains(neighbor)) continue;
                    
                    cameFrom[neighbor] = current;
                    
                    if (neighbor == toNode)
                    {
                        return ReconstructPath(cameFrom, fromNode, toNode);
                    }
                    
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }

            return new List<int>(); // No path found
        }

        private List<int> ReconstructPath(Dictionary<int, int> cameFrom, int start, int end)
        {
            var path = new List<int>();
            int current = end;
            
            while (current != start)
            {
                path.Add(current);
                current = cameFrom[current];
            }
            path.Add(start);
            path.Reverse();
            
            return path;
        }

        /// <summary>
        /// Get all nodes reachable within a certain distance.
        /// </summary>
        public List<int> GetNodesInRange(int fromNode, int maxDistance)
        {
            var result = new List<int>();
            var visited = new HashSet<int>();
            var queue = new Queue<(int node, int distance)>();

            queue.Enqueue((fromNode, 0));
            visited.Add(fromNode);

            while (queue.Count > 0)
            {
                var (current, dist) = queue.Dequeue();
                result.Add(current);

                if (dist >= maxDistance) continue;

                foreach (int neighbor in State.GetPassableAdjacentNodes(current))
                {
                    if (visited.Contains(neighbor)) continue;
                    visited.Add(neighbor);
                    queue.Enqueue((neighbor, dist + 1));
                }
            }

            return result;
        }

        /// <summary>
        /// Get distance between two nodes (-1 if unreachable).
        /// </summary>
        public int GetDistance(int fromNode, int toNode)
        {
            var path = FindPath(fromNode, toNode);
            return path.Count > 0 ? path.Count - 1 : -1;
        }

        #endregion

        #region Line of Sight (for ranged attacks, bishop/rook movement)

        /// <summary>
        /// Get all nodes in a straight line from origin in a direction.
        /// Stops at board edge or impassable node.
        /// </summary>
        public List<int> GetNodesInLine(int fromNode, Vector2Int direction, int maxDistance = 100)
        {
            var result = new List<int>();
            var coords = Definition.GetCoordinates(fromNode);
            
            for (int i = 1; i <= maxDistance; i++)
            {
                int x = coords.x + direction.x * i;
                int y = coords.y + direction.y * i;
                
                if (x < 0 || x >= Definition.Width || y < 0 || y >= Definition.Height)
                    break;
                
                int nodeId = Definition.GetNodeId(x, y);
                
                if (!State.IsNodePassable(nodeId))
                    break;
                
                result.Add(nodeId);
            }
            
            return result;
        }

        /// <summary>
        /// Get all diagonal lines from a node (for bishop-like movement).
        /// </summary>
        public List<int> GetDiagonalNodes(int fromNode, int maxDistance = 100)
        {
            var result = new List<int>();
            var directions = new Vector2Int[]
            {
                new Vector2Int(1, 1),
                new Vector2Int(1, -1),
                new Vector2Int(-1, 1),
                new Vector2Int(-1, -1)
            };

            foreach (var dir in directions)
            {
                result.AddRange(GetNodesInLine(fromNode, dir, maxDistance));
            }

            return result;
        }

        /// <summary>
        /// Get all orthogonal lines from a node (for rook-like movement).
        /// </summary>
        public List<int> GetOrthogonalNodes(int fromNode, int maxDistance = 100)
        {
            var result = new List<int>();
            var directions = new Vector2Int[]
            {
                new Vector2Int(1, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(0, -1)
            };

            foreach (var dir in directions)
            {
                result.AddRange(GetNodesInLine(fromNode, dir, maxDistance));
            }

            return result;
        }

        /// <summary>
        /// Get knight-move positions from a node.
        /// </summary>
        public List<int> GetKnightMoveNodes(int fromNode)
        {
            var result = new List<int>();
            var coords = Definition.GetCoordinates(fromNode);
            
            var offsets = new Vector2Int[]
            {
                new Vector2Int(2, 1), new Vector2Int(2, -1),
                new Vector2Int(-2, 1), new Vector2Int(-2, -1),
                new Vector2Int(1, 2), new Vector2Int(1, -2),
                new Vector2Int(-1, 2), new Vector2Int(-1, -2)
            };

            foreach (var offset in offsets)
            {
                int x = coords.x + offset.x;
                int y = coords.y + offset.y;
                
                if (x < 0 || x >= Definition.Width || y < 0 || y >= Definition.Height)
                    continue;
                
                int nodeId = Definition.GetNodeId(x, y);
                
                if (State.IsNodePassable(nodeId))
                {
                    result.Add(nodeId);
                }
            }

            return result;
        }

        #endregion

        #region Board Modifications

        public void DestroyNode(int nodeId) => State.DestroyNode(nodeId);
        
        public void ChangeNodeType(int nodeId, NodeType newType) => State.ChangeNodeType(nodeId, newType);
        
        public void ProcessTurnEnd() => State.ProcessTurnEnd();

        #endregion
    }
}

