using System;
using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Board;

namespace BizarreChess.Core.Units
{
    /// <summary>
    /// Defines how a unit can move on the board.
    /// </summary>
    [Serializable]
    public class MovementPattern
    {
        public MovementType Type;
        public int MaxDistance;          // -1 for unlimited (queen, rook, bishop)
        public bool CanJump;             // Leaper pieces can jump over others
        public bool CaptureOnly;         // Pawn diagonal capture
        public bool MoveOnly;            // Pawn forward (can't capture going forward)
        public bool FirstMoveOnly;       // Pawn double move on first turn
        public Vector2Int Direction;     // For directional moves (pawn forward)
        
        // Leaper movement parameters (for Knight, Camel, Zebra, etc.)
        public int LeapX;                // Primary leap distance (e.g., 2 for Knight)
        public int LeapY;                // Secondary leap distance (e.g., 1 for Knight)

        public MovementPattern() { }

        public MovementPattern(MovementType type, int maxDistance = -1)
        {
            Type = type;
            MaxDistance = maxDistance;
            CanJump = false;
            CaptureOnly = false;
            MoveOnly = false;
            FirstMoveOnly = false;
            Direction = Vector2Int.zero;
            LeapX = 0;
            LeapY = 0;
        }

        /// <summary>
        /// Create a Leaper movement pattern (Knight, Camel, Zebra, etc.)
        /// </summary>
        /// <param name="leapX">Primary leap distance (larger value)</param>
        /// <param name="leapY">Secondary leap distance (smaller value)</param>
        public static MovementPattern Leaper(int leapX, int leapY)
        {
            return new MovementPattern(MovementType.Leaper)
            {
                LeapX = leapX,
                LeapY = leapY,
                CanJump = true
            };
        }

        /// <summary>
        /// Get all valid target nodes for this movement pattern.
        /// </summary>
        public List<int> GetValidTargets(BoardGraph board, int fromNode, int playerSide, Func<int, bool> isOccupied, Func<int, bool> isEnemy)
        {
            var result = new List<int>();
            int maxDist = MaxDistance == -1 ? 100 : MaxDistance;

            switch (Type)
            {
                case MovementType.Orthogonal:
                    AddLineTargets(result, board, fromNode, new Vector2Int(1, 0), maxDist, isOccupied, isEnemy);
                    AddLineTargets(result, board, fromNode, new Vector2Int(-1, 0), maxDist, isOccupied, isEnemy);
                    AddLineTargets(result, board, fromNode, new Vector2Int(0, 1), maxDist, isOccupied, isEnemy);
                    AddLineTargets(result, board, fromNode, new Vector2Int(0, -1), maxDist, isOccupied, isEnemy);
                    break;

                case MovementType.Diagonal:
                    AddLineTargets(result, board, fromNode, new Vector2Int(1, 1), maxDist, isOccupied, isEnemy);
                    AddLineTargets(result, board, fromNode, new Vector2Int(1, -1), maxDist, isOccupied, isEnemy);
                    AddLineTargets(result, board, fromNode, new Vector2Int(-1, 1), maxDist, isOccupied, isEnemy);
                    AddLineTargets(result, board, fromNode, new Vector2Int(-1, -1), maxDist, isOccupied, isEnemy);
                    break;

                case MovementType.Leaper:
                    AddLeaperTargets(result, board, fromNode, LeapX, LeapY, isOccupied, isEnemy);
                    break;

                case MovementType.Adjacent:
                    AddAdjacentTargets(result, board, fromNode, maxDist, isOccupied, isEnemy);
                    break;

                case MovementType.Forward:
                    AddForwardTargets(result, board, fromNode, playerSide, maxDist, isOccupied, isEnemy);
                    break;

                case MovementType.DiagonalCapture:
                    AddDiagonalCaptureTargets(result, board, fromNode, playerSide, isEnemy);
                    break;
            }

            return result;
        }

        private void AddLineTargets(List<int> result, BoardGraph board, int fromNode, Vector2Int dir, int maxDist, 
            Func<int, bool> isOccupied, Func<int, bool> isEnemy)
        {
            var coords = board.Definition.GetCoordinates(fromNode);

            for (int i = 1; i <= maxDist; i++)
            {
                int x = coords.x + dir.x * i;
                int y = coords.y + dir.y * i;

                if (x < 0 || x >= board.Definition.Width || y < 0 || y >= board.Definition.Height)
                    break;

                int nodeId = board.Definition.GetNodeId(x, y);

                if (!board.IsPassable(nodeId))
                    break;

                if (isOccupied(nodeId))
                {
                    // Can capture enemy
                    if (isEnemy(nodeId) && !MoveOnly)
                    {
                        result.Add(nodeId);
                    }
                    
                    // If can't jump, stop here
                    if (!CanJump)
                        break;
                    
                    // If can jump, continue but don't add this square as a move target
                    // (we already added it if it was an enemy capture)
                    continue;
                }

                if (!CaptureOnly)
                {
                    result.Add(nodeId);
                }
            }
        }

        private void AddLeaperTargets(List<int> result, BoardGraph board, int fromNode,
            int leapX, int leapY, Func<int, bool> isOccupied, Func<int, bool> isEnemy)
        {
            var coords = board.Definition.GetCoordinates(fromNode);
            
            // Generate all 8 possible leap positions (4 if leapX == leapY)
            var offsets = new List<Vector2Int>
            {
                new Vector2Int(leapX, leapY), new Vector2Int(leapX, -leapY),
                new Vector2Int(-leapX, leapY), new Vector2Int(-leapX, -leapY)
            };
            
            // Add swapped offsets only if leapX != leapY (otherwise they'd be duplicates)
            if (leapX != leapY)
            {
                offsets.Add(new Vector2Int(leapY, leapX));
                offsets.Add(new Vector2Int(leapY, -leapX));
                offsets.Add(new Vector2Int(-leapY, leapX));
                offsets.Add(new Vector2Int(-leapY, -leapX));
            }

            foreach (var offset in offsets)
            {
                int x = coords.x + offset.x;
                int y = coords.y + offset.y;

                if (x < 0 || x >= board.Definition.Width || y < 0 || y >= board.Definition.Height)
                    continue;

                int nodeId = board.Definition.GetNodeId(x, y);

                if (!board.IsPassable(nodeId))
                    continue;

                if (isOccupied(nodeId) && !isEnemy(nodeId))
                    continue; // Can't move to friendly occupied square

                // Check if leap path is blocked by Impassable terrain
                if (IsLeapPathBlocked(board, coords, offset))
                    continue;

                result.Add(nodeId);
            }
        }

        /// <summary>
        /// Checks if a leaper's path is blocked by Impassable terrain.
        /// Explores ALL possible Manhattan paths (step-by-step horizontal/vertical moves).
        /// The leap is blocked only if ALL paths have an Impassable tile.
        /// Abyss tiles do NOT block leaper paths.
        /// </summary>
        private bool IsLeapPathBlocked(BoardGraph board, Vector2Int from, Vector2Int offset)
        {
            Vector2Int target = from + offset;
            // Try to find ANY valid path - if one exists, leap is not blocked
            return !HasValidLeapPath(board, from, target);
        }

        /// <summary>
        /// Recursively searches for any valid path from current to target,
        /// moving one step at a time (horizontal or vertical).
        /// Returns true if at least one valid path exists.
        /// </summary>
        private bool HasValidLeapPath(BoardGraph board, Vector2Int current, Vector2Int target)
        {
            // Base case: reached destination
            if (current == target)
                return true;
            
            int dx = target.x - current.x;
            int dy = target.y - current.y;
            
            // Try moving horizontally (if we still need to move in X)
            if (dx != 0)
            {
                int stepX = dx > 0 ? 1 : -1;
                Vector2Int next = new Vector2Int(current.x + stepX, current.y);
                
                // Intermediate tiles must not be Impassable (destination is checked elsewhere)
                bool isDestination = (next == target);
                bool canPass = isDestination || !IsTileImpassable(board, next.x, next.y);
                
                if (canPass && HasValidLeapPath(board, next, target))
                    return true;
            }
            
            // Try moving vertically (if we still need to move in Y)
            if (dy != 0)
            {
                int stepY = dy > 0 ? 1 : -1;
                Vector2Int next = new Vector2Int(current.x, current.y + stepY);
                
                bool isDestination = (next == target);
                bool canPass = isDestination || !IsTileImpassable(board, next.x, next.y);
                
                if (canPass && HasValidLeapPath(board, next, target))
                    return true;
            }
            
            // No valid path found from this position
            return false;
        }

        /// <summary>
        /// Checks if a tile at the given coordinates is Impassable (not Abyss).
        /// Returns false if coordinates are out of bounds.
        /// </summary>
        private bool IsTileImpassable(BoardGraph board, int x, int y)
        {
            if (x < 0 || x >= board.Definition.Width || y < 0 || y >= board.Definition.Height)
                return false;
            
            int nodeId = board.Definition.GetNodeId(x, y);
            return board.State.GetNode(nodeId).IsImpassable;
        }

        private void AddAdjacentTargets(List<int> result, BoardGraph board, int fromNode, int maxDist,
            Func<int, bool> isOccupied, Func<int, bool> isEnemy)
        {
            var coords = board.Definition.GetCoordinates(fromNode);
            var directions = new Vector2Int[]
            {
                new Vector2Int(1, 0), new Vector2Int(-1, 0),
                new Vector2Int(0, 1), new Vector2Int(0, -1),
                new Vector2Int(1, 1), new Vector2Int(1, -1),
                new Vector2Int(-1, 1), new Vector2Int(-1, -1)
            };

            foreach (var dir in directions)
            {
                int x = coords.x + dir.x;
                int y = coords.y + dir.y;

                if (x < 0 || x >= board.Definition.Width || y < 0 || y >= board.Definition.Height)
                    continue;

                int nodeId = board.Definition.GetNodeId(x, y);

                if (!board.IsPassable(nodeId))
                    continue;

                if (isOccupied(nodeId) && !isEnemy(nodeId))
                    continue;

                result.Add(nodeId);
            }
        }

        private void AddForwardTargets(List<int> result, BoardGraph board, int fromNode, int playerSide, int maxDist,
            Func<int, bool> isOccupied, Func<int, bool> isEnemy)
        {
            var coords = board.Definition.GetCoordinates(fromNode);
            int forwardDir = playerSide == 0 ? 1 : -1; // Player 0 moves up, Player 1 moves down

            for (int i = 1; i <= maxDist; i++)
            {
                int y = coords.y + forwardDir * i;

                if (y < 0 || y >= board.Definition.Height)
                    break;

                int nodeId = board.Definition.GetNodeId(coords.x, y);

                if (!board.IsPassable(nodeId))
                    break;

                if (isOccupied(nodeId))
                    break; // Pawn can't capture forward

                result.Add(nodeId);
            }
        }

        private void AddDiagonalCaptureTargets(List<int> result, BoardGraph board, int fromNode, int playerSide,
            Func<int, bool> isEnemy)
        {
            var coords = board.Definition.GetCoordinates(fromNode);
            int forwardDir = playerSide == 0 ? 1 : -1;

            var offsets = new int[] { -1, 1 };
            foreach (var xOffset in offsets)
            {
                int x = coords.x + xOffset;
                int y = coords.y + forwardDir;

                if (x < 0 || x >= board.Definition.Width || y < 0 || y >= board.Definition.Height)
                    continue;

                int nodeId = board.Definition.GetNodeId(x, y);

                if (!board.IsPassable(nodeId))
                    continue;

                // Can only move here if there's an enemy to capture
                if (isEnemy(nodeId))
                {
                    result.Add(nodeId);
                }
            }
        }
    }

    public enum MovementType
    {
        Orthogonal,      // Rook-like (horizontal/vertical lines)
        Diagonal,        // Bishop-like
        Leaper,          // Jump pattern defined by LeapX, LeapY (Knight=2,1 Camel=3,1 Zebra=3,2 etc.)
        Adjacent,        // King-like (1 square any direction)
        Forward,         // Pawn forward movement
        DiagonalCapture, // Pawn diagonal capture
        Custom           // For bizarre chess special pieces
    }

    /// <summary>
    /// Predefined movement patterns for classic chess pieces.
    /// </summary>
    public static class ClassicMovementPatterns
    {
        public static MovementPattern[] King => new[]
        {
            new MovementPattern(MovementType.Adjacent, 1)
        };

        public static MovementPattern[] Queen => new[]
        {
            new MovementPattern(MovementType.Orthogonal, -1),
            new MovementPattern(MovementType.Diagonal, -1)
        };

        public static MovementPattern[] Rook => new[]
        {
            new MovementPattern(MovementType.Orthogonal, -1)
        };

        public static MovementPattern[] Bishop => new[]
        {
            new MovementPattern(MovementType.Diagonal, -1)
        };

        public static MovementPattern[] Knight => new[]
        {
            MovementPattern.Leaper(2, 1)
        };

        public static MovementPattern[] Pawn => new[]
        {
            new MovementPattern(MovementType.Forward, 1) { MoveOnly = true },
            new MovementPattern(MovementType.Forward, 2) { MoveOnly = true, FirstMoveOnly = true },
            new MovementPattern(MovementType.DiagonalCapture) { CaptureOnly = true }
        };
    }
}

