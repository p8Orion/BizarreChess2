using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Board;

namespace BizarreChess.Core.Factories
{
    /// <summary>
    /// Factory for creating board definitions.
    /// Supports classic 8x8 boards and custom configurations.
    /// </summary>
    public static class BoardFactory
    {
        #region Classic Board

        /// <summary>
        /// Create a standard 8x8 chess board definition.
        /// </summary>
        public static BoardDefinition CreateClassicBoard(BoardSkin skin = null)
        {
            return CreateRectangularBoard(8, 8, "classic_8x8", "Classic Chess Board", skin);
        }

        #endregion

        #region Rectangular Boards

        /// <summary>
        /// Create a rectangular board with standard chess connectivity.
        /// </summary>
        public static BoardDefinition CreateRectangularBoard(
            int width,
            int height,
            string boardId = null,
            string displayName = null,
            BoardSkin skin = null)
        {
            var board = ScriptableObject.CreateInstance<BoardDefinition>();
            board.BoardId = boardId ?? $"board_{width}x{height}";
            board.DisplayName = displayName ?? $"{width}x{height} Board";
            board.Width = width;
            board.Height = height;
            board.PlacementMode = PlacementMode.Automatic;
            board.Nodes = new List<NodeDefinition>();
            board.Edges = new List<EdgeDefinition>();
            board.SpawnZones = new List<SpawnZone>();
            board.Skin = skin ?? BoardSkins.ClassicWood;

            // Create nodes
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int id = y * width + x;
                    bool isLight = (x + y) % 2 == 1;

                    board.Nodes.Add(new NodeDefinition(
                        id: id,
                        position: new Vector2(x, y),
                        type: NodeType.Normal,
                        isLight: isLight
                    ));
                }
            }

            // Create edges (8-way connectivity)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int id = y * width + x;

                    // Connect to right neighbor
                    if (x < width - 1)
                    {
                        board.Edges.Add(new EdgeDefinition(id, id + 1));
                    }

                    // Connect to top neighbor
                    if (y < height - 1)
                    {
                        board.Edges.Add(new EdgeDefinition(id, id + width));
                    }

                    // Connect to top-right diagonal
                    if (x < width - 1 && y < height - 1)
                    {
                        board.Edges.Add(new EdgeDefinition(id, id + width + 1));
                    }

                    // Connect to top-left diagonal
                    if (x > 0 && y < height - 1)
                    {
                        board.Edges.Add(new EdgeDefinition(id, id + width - 1));
                    }
                }
            }

            // Create standard spawn zones (bottom rows for P1, top rows for P2)
            AddStandardSpawnZones(board);

            return board;
        }

        /// <summary>
        /// Add standard spawn zones for a 2-player board.
        /// Player 1 gets bottom 2 rows, Player 2 gets top 2 rows.
        /// </summary>
        private static void AddStandardSpawnZones(BoardDefinition board)
        {
            int width = board.Width;
            int height = board.Height;

            // Player 1: rows 0-1 (bottom)
            var player1BackRow = new List<int>();
            var player1FrontRow = new List<int>();
            for (int x = 0; x < width; x++)
            {
                player1BackRow.Add(x);              // row 0
                player1FrontRow.Add(width + x);     // row 1
            }

            // Player 2: rows (height-2) and (height-1) (top)
            var player2FrontRow = new List<int>();
            var player2BackRow = new List<int>();
            for (int x = 0; x < width; x++)
            {
                player2FrontRow.Add((height - 2) * width + x);  // second to last row
                player2BackRow.Add((height - 1) * width + x);   // last row
            }

            board.SpawnZones.Add(new SpawnZone(0, player1BackRow, player1FrontRow));
            board.SpawnZones.Add(new SpawnZone(1, player2BackRow, player2FrontRow));
        }

        #endregion

        #region Custom Board Building

        /// <summary>
        /// Create an empty board definition to build upon.
        /// </summary>
        public static BoardDefinition CreateEmptyBoard(
            string boardId,
            string displayName,
            int width = 8,
            int height = 8,
            BoardSkin skin = null)
        {
            var board = ScriptableObject.CreateInstance<BoardDefinition>();
            board.BoardId = boardId;
            board.DisplayName = displayName;
            board.Width = width;
            board.Height = height;
            board.PlacementMode = PlacementMode.Automatic;
            board.Nodes = new List<NodeDefinition>();
            board.Edges = new List<EdgeDefinition>();
            board.SpawnZones = new List<SpawnZone>();
            board.Skin = skin ?? BoardSkins.ClassicWood;
            return board;
        }

        /// <summary>
        /// Add a node to a board.
        /// </summary>
        public static void AddNode(
            BoardDefinition board,
            int id,
            Vector2 position,
            NodeType type = NodeType.Normal,
            bool isLight = true)
        {
            board.Nodes.Add(new NodeDefinition(id, position, type, isLight));
        }

        /// <summary>
        /// Add an edge between two nodes.
        /// </summary>
        public static void AddEdge(
            BoardDefinition board,
            int fromId,
            int toId,
            bool bidirectional = true,
            EdgeType type = EdgeType.Normal)
        {
            board.Edges.Add(new EdgeDefinition(fromId, toId, bidirectional, type));
        }

        /// <summary>
        /// Add a spawn zone to a board.
        /// </summary>
        public static void AddSpawnZone(
            BoardDefinition board,
            int playerSlot,
            List<int> backRow,
            List<int> frontRow)
        {
            board.SpawnZones.Add(new SpawnZone(playerSlot, backRow, frontRow));
        }

        #endregion

        #region Variant Boards

        /// <summary>
        /// Create a 6x6 mini chess board.
        /// </summary>
        public static BoardDefinition CreateMiniBoard(BoardSkin skin = null)
        {
            return CreateRectangularBoard(6, 6, "mini_6x6", "Mini Chess Board", skin);
        }

        /// <summary>
        /// Create a 10x10 grand chess board.
        /// </summary>
        public static BoardDefinition CreateGrandBoard(BoardSkin skin = null)
        {
            return CreateRectangularBoard(10, 10, "grand_10x10", "Grand Chess Board", skin);
        }

        /// <summary>
        /// Create an 8x10 Capablanca chess board.
        /// </summary>
        public static BoardDefinition CreateCapablancaBoard(BoardSkin skin = null)
        {
            return CreateRectangularBoard(10, 8, "capablanca_10x8", "Capablanca Chess Board", skin);
        }

        /// <summary>
        /// Create a board with X% of tiles as Abyss and Y% as Impassable.
        /// Abyss tiles cannot be landed on but maintain edge connectivity (can be jumped over).
        /// Impassable tiles cannot be landed on AND block leaper paths (mountains).
        /// Special tiles will not appear in the first 2 or last 2 rows (spawn zones).
        /// Pattern is symmetric (mirrored horizontally and vertically) for fairness.
        /// </summary>
        /// <param name="sizeX">Board width</param>
        /// <param name="sizeY">Board height</param>
        /// <param name="abyssPercentage">Percentage of eligible tiles to become abyss (0-100)</param>
        /// <param name="impassablePercentage">Percentage of eligible tiles to become impassable mountains (0-100)</param>
        /// <param name="seed">Random seed for reproducible generation (-1 for random)</param>
        public static BoardDefinition CreateBoardWithAbyss(
            int sizeX,
            int sizeY,
            float abyssPercentage,
            float impassablePercentage = 0f,
            int seed = -1,
            string boardId = null,
            string displayName = null,
            BoardSkin skin = null)
        {
            var board = ScriptableObject.CreateInstance<BoardDefinition>();
            
            string idSuffix = impassablePercentage > 0 
                ? $"abyss_{sizeX}x{sizeY}_{abyssPercentage:F0}a_{impassablePercentage:F0}i"
                : $"abyss_{sizeX}x{sizeY}_{abyssPercentage:F0}pct";
            string nameSuffix = impassablePercentage > 0
                ? $"{sizeX}x{sizeY} Board ({abyssPercentage:F0}% Abyss, {impassablePercentage:F0}% Mountains)"
                : $"{sizeX}x{sizeY} Abyss Board ({abyssPercentage:F0}%)";
                
            board.BoardId = boardId ?? idSuffix;
            board.DisplayName = displayName ?? nameSuffix;
            board.Width = sizeX;
            board.Height = sizeY;
            board.PlacementMode = PlacementMode.Automatic;
            board.Nodes = new List<NodeDefinition>();
            board.Edges = new List<EdgeDefinition>();
            board.SpawnZones = new List<SpawnZone>();
            board.Skin = skin ?? BoardSkins.ClassicWood;

            // Use provided seed or generate random one
            var random = seed >= 0 ? new System.Random(seed) : new System.Random();

            // Only consider bottom half of eligible area (rows 2 to center-1)
            // These will be mirrored to create symmetric pattern
            int centerY = sizeY / 2;
            var bottomHalfTiles = new List<Vector2Int>();
            
            for (int y = 2; y < centerY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    bottomHalfTiles.Add(new Vector2Int(x, y));
                }
            }

            // Calculate total eligible tiles (both halves)
            int totalEligibleTiles = bottomHalfTiles.Count * 2; // Each tile has a mirror
            
            // Calculate target counts for each terrain type
            int targetAbyssCount = Mathf.RoundToInt(totalEligibleTiles * Mathf.Clamp(abyssPercentage, 0f, 100f) / 100f);
            int targetImpassableCount = Mathf.RoundToInt(totalEligibleTiles * Mathf.Clamp(impassablePercentage, 0f, 100f) / 100f);
            
            // Each selection creates 2 tiles (original + mirror)
            int abyssSelectionsNeeded = targetAbyssCount / 2;
            int impassableSelectionsNeeded = targetImpassableCount / 2;

            // Shuffle tiles for random distribution
            ShuffleList(bottomHalfTiles, random);
            
            var abyssTiles = new HashSet<int>();
            var impassableTiles = new HashSet<int>();
            
            int tileIndex = 0;
            
            // Select abyss tiles first
            for (int i = 0; i < abyssSelectionsNeeded && tileIndex < bottomHalfTiles.Count; i++, tileIndex++)
            {
                var pos = bottomHalfTiles[tileIndex];
                int id = pos.y * sizeX + pos.x;
                
                // Add original tile
                abyssTiles.Add(id);
                
                // Add mirrored tile (both horizontally and vertically)
                int mirrorX = sizeX - 1 - pos.x;
                int mirrorY = sizeY - 1 - pos.y;
                int mirrorId = mirrorY * sizeX + mirrorX;
                abyssTiles.Add(mirrorId);
            }
            
            // Select impassable tiles from remaining tiles
            for (int i = 0; i < impassableSelectionsNeeded && tileIndex < bottomHalfTiles.Count; i++, tileIndex++)
            {
                var pos = bottomHalfTiles[tileIndex];
                int id = pos.y * sizeX + pos.x;
                
                // Add original tile
                impassableTiles.Add(id);
                
                // Add mirrored tile (both horizontally and vertically)
                int mirrorX = sizeX - 1 - pos.x;
                int mirrorY = sizeY - 1 - pos.y;
                int mirrorId = mirrorY * sizeX + mirrorX;
                impassableTiles.Add(mirrorId);
            }

            // Create all nodes
            for (int y = 0; y < sizeY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int id = y * sizeX + x;
                    bool isLight = (x + y) % 2 == 1;
                    
                    NodeType nodeType = NodeType.Normal;
                    if (abyssTiles.Contains(id))
                        nodeType = NodeType.Abyss;
                    else if (impassableTiles.Contains(id))
                        nodeType = NodeType.Impassable;

                    board.Nodes.Add(new NodeDefinition(
                        id: id,
                        position: new Vector2(x, y),
                        type: nodeType,
                        isLight: isLight
                    ));
                }
            }

            // Create edges (8-way connectivity including through abyss - allows jumping)
            for (int y = 0; y < sizeY; y++)
            {
                for (int x = 0; x < sizeX; x++)
                {
                    int id = y * sizeX + x;

                    // Connect to right neighbor
                    if (x < sizeX - 1)
                    {
                        board.Edges.Add(new EdgeDefinition(id, id + 1));
                    }

                    // Connect to top neighbor
                    if (y < sizeY - 1)
                    {
                        board.Edges.Add(new EdgeDefinition(id, id + sizeX));
                    }

                    // Connect to top-right diagonal
                    if (x < sizeX - 1 && y < sizeY - 1)
                    {
                        board.Edges.Add(new EdgeDefinition(id, id + sizeX + 1));
                    }

                    // Connect to top-left diagonal
                    if (x > 0 && y < sizeY - 1)
                    {
                        board.Edges.Add(new EdgeDefinition(id, id + sizeX - 1));
                    }
                }
            }

            // Add standard spawn zones
            AddStandardSpawnZones(board);

            return board;
        }

        /// <summary>
        /// Fisher-Yates shuffle for list randomization.
        /// </summary>
        private static void ShuffleList<T>(List<T> list, System.Random random)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        /// <summary>
        /// Create a board with some nodes removed (irregular shape).
        /// </summary>
        public static BoardDefinition CreateBoardWithHoles(
            int width,
            int height,
            List<Vector2Int> holes,
            string boardId = null,
            string displayName = null,
            BoardSkin skin = null)
        {
            var board = CreateEmptyBoard(
                boardId ?? $"holes_{width}x{height}",
                displayName ?? $"{width}x{height} Board with Holes",
                width,
                height,
                skin
            );

            var holeSet = new HashSet<Vector2Int>(holes);

            // Create nodes (skip holes)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    if (holeSet.Contains(new Vector2Int(x, y)))
                        continue;

                    int id = y * width + x;
                    bool isLight = (x + y) % 2 == 1;

                    board.Nodes.Add(new NodeDefinition(
                        id: id,
                        position: new Vector2(x, y),
                        type: NodeType.Normal,
                        isLight: isLight
                    ));
                }
            }

            // Create edges (only between existing nodes)
            var nodeIds = new HashSet<int>();
            foreach (var node in board.Nodes)
            {
                nodeIds.Add(node.Id);
            }

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int id = y * width + x;
                    if (!nodeIds.Contains(id))
                        continue;

                    // Connect to right neighbor
                    int rightId = id + 1;
                    if (x < width - 1 && nodeIds.Contains(rightId))
                    {
                        board.Edges.Add(new EdgeDefinition(id, rightId));
                    }

                    // Connect to top neighbor
                    int topId = id + width;
                    if (y < height - 1 && nodeIds.Contains(topId))
                    {
                        board.Edges.Add(new EdgeDefinition(id, topId));
                    }

                    // Connect to top-right diagonal
                    int topRightId = id + width + 1;
                    if (x < width - 1 && y < height - 1 && nodeIds.Contains(topRightId))
                    {
                        board.Edges.Add(new EdgeDefinition(id, topRightId));
                    }

                    // Connect to top-left diagonal
                    int topLeftId = id + width - 1;
                    if (x > 0 && y < height - 1 && nodeIds.Contains(topLeftId))
                    {
                        board.Edges.Add(new EdgeDefinition(id, topLeftId));
                    }
                }
            }

            // Add standard spawn zones (caller should customize if holes affect spawn areas)
            AddStandardSpawnZones(board);

            return board;
        }

        #endregion
    }
}

