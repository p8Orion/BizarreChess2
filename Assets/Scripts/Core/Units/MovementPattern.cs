using System;
using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Graph;

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
        public bool CanJump;             // Knight can jump over pieces
        public bool CaptureOnly;         // Pawn diagonal capture
        public bool MoveOnly;            // Pawn forward (can't capture going forward)
        public bool FirstMoveOnly;       // Pawn double move on first turn
        public Vector2Int Direction;     // For directional moves (pawn forward)

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

                case MovementType.Knight:
                    AddKnightTargets(result, board, fromNode, isOccupied, isEnemy);
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
                    // Can capture enemy, but can't go further
                    if (isEnemy(nodeId) && !MoveOnly)
                    {
                        result.Add(nodeId);
                    }
                    break;
                }

                if (!CaptureOnly)
                {
                    result.Add(nodeId);
                }
            }
        }

        private void AddKnightTargets(List<int> result, BoardGraph board, int fromNode,
            Func<int, bool> isOccupied, Func<int, bool> isEnemy)
        {
            var coords = board.Definition.GetCoordinates(fromNode);
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

                if (x < 0 || x >= board.Definition.Width || y < 0 || y >= board.Definition.Height)
                    continue;

                int nodeId = board.Definition.GetNodeId(x, y);

                if (!board.IsPassable(nodeId))
                    continue;

                if (isOccupied(nodeId) && !isEnemy(nodeId))
                    continue; // Can't move to friendly occupied square

                result.Add(nodeId);
            }
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
        Knight,          // L-shape jump
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
            new MovementPattern(MovementType.Knight) { CanJump = true }
        };

        public static MovementPattern[] Pawn => new[]
        {
            new MovementPattern(MovementType.Forward, 1) { MoveOnly = true },
            new MovementPattern(MovementType.Forward, 2) { MoveOnly = true, FirstMoveOnly = true },
            new MovementPattern(MovementType.DiagonalCapture) { CaptureOnly = true }
        };
    }
}

