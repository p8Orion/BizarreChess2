using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Board;
using BizarreChess.Core.Player;
using BizarreChess.Core.Units;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Renders the game board (tiles, connections, effects).
    /// </summary>
    public class BoardRenderer : MonoBehaviour
    {
        [Header("Prefabs")]
        [SerializeField] private TileRenderer _tilePrefab;
        [SerializeField] private LineRenderer _connectionPrefab;

        [Header("Colors")]
        [SerializeField] private Color _attackHighlightColor = new Color(1f, 0.5f, 0.5f, 0.5f);
        [SerializeField] private Color _moveIndicatorColor = new Color(0.3f, 0.8f, 0.3f, 0.9f);
        // Tile colors/textures now come from BoardSkins.Current

        [Header("Layout")]
        [SerializeField] private float _tileSize = 1f;
        [SerializeField] private float _tileSpacing = 0f;
        [SerializeField] private float _tileThickness = 0.3f;

        private Dictionary<int, TileRenderer> _tiles = new Dictionary<int, TileRenderer>();
        private BoardGraph _boardGraph;
        private HashSet<int> _highlightedMoves = new HashSet<int>();
        private HashSet<int> _highlightedAttacks = new HashSet<int>();
        private HashSet<int> _highlightedHover = new HashSet<int>();
        private HashSet<int> _indicatedTiles = new HashSet<int>();
        
        // Store selection state for restoring after hover
        private MoveTargets _currentSelectionMoves;
        private Color _currentSelectionColor;

        public System.Action<int> OnTileClicked;

        #region Rendering

        /// <summary>
        /// Render the board from a BoardGraph.
        /// </summary>
        public void RenderBoard(BoardGraph boardGraph)
        {
            ClearBoard();
            _boardGraph = boardGraph;

            var definition = boardGraph.Definition;

            foreach (var nodeDef in definition.Nodes)
            {
                CreateTile(nodeDef, boardGraph.State.GetNode(nodeDef.Id));
            }

            // Render special connections (teleports, etc.)
            RenderSpecialConnections(boardGraph);
        }

        private void CreateTile(NodeDefinition nodeDef, NodeState nodeState)
        {
            // Don't render Abyss tiles - they should appear as voids
            if (nodeState.CurrentType == NodeType.Abyss)
                return;

            var style = GetTileStyle(nodeDef, nodeState);
            if (style == null) return;

            if (_tilePrefab == null)
            {
                CreatePlaceholderTile(nodeDef, nodeState, style);
                return;
            }

            var tile = Instantiate(_tilePrefab, transform);
            tile.Initialize(nodeDef, nodeState, style, _tileSize);
            tile.transform.localPosition = GetTilePosition(nodeDef.Position);
            tile.OnClicked += () => OnTileClicked?.Invoke(nodeDef.Id);

            _tiles[nodeDef.Id] = tile;
        }

        private void CreatePlaceholderTile(NodeDefinition nodeDef, NodeState nodeState, TileStyle style)
        {
            // For Impassable tiles, use floor style for the base tile
            var floorStyle = style;
            if (nodeState.CurrentType == NodeType.Impassable)
            {
                floorStyle = BoardSkins.Current.GetImpassableFloor();
            }

            // Create cube with thickness instead of flat quad
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Tile_{nodeDef.Id}";
            go.transform.SetParent(transform);
            
            // Position tile so top surface is at y=0 (tile extends downward)
            Vector3 basePos = GetTilePosition(nodeDef.Position);
            go.transform.localPosition = new Vector3(basePos.x, -_tileThickness / 2f, basePos.z);
            go.transform.localScale = new Vector3(_tileSize, _tileThickness, _tileSize);

            var renderer = go.GetComponent<Renderer>();
            var mat = CreateTileMaterial(floorStyle);
            renderer.material = mat;

            // Cube already has BoxCollider, just adjust if needed
            var boxCollider = go.GetComponent<BoxCollider>();
            if (boxCollider != null)
                boxCollider.size = Vector3.one;

            // Add click handler
            var clickHandler = go.AddComponent<TileClickHandler>();
            clickHandler.TileId = nodeDef.Id;
            clickHandler.OnClicked += (id) => OnTileClicked?.Invoke(id);

            // Create a TileRenderer wrapper
            var tileRenderer = go.AddComponent<TileRenderer>();
            tileRenderer.InitializePlaceholder(nodeDef.Id, renderer, floorStyle);

            _tiles[nodeDef.Id] = tileRenderer;
            
            // Add wall mesh for Impassable tiles (uses the original Impassable style)
            if (nodeState.CurrentType == NodeType.Impassable)
            {
                CreateWallOnTile(go, nodeDef, style);
            }
        }

        private static readonly string[] RockResourcePaths =
        {
            "Terrain/rock_01",
            "Terrain/rock_02",
            "Terrain/rock_03"
        };

        /// <summary>
        /// Places an alternating rock model on an Impassable tile.
        /// Rocks are parented to the board (not the scaled tile cube) so they keep their shape.
        /// </summary>
        private void CreateWallOnTile(GameObject parentTile, NodeDefinition nodeDef, TileStyle style)
        {
            var path = RockResourcePaths[Mathf.Abs(nodeDef.Id) % RockResourcePaths.Length];
            var prefab = Resources.Load<GameObject>(path);
            if (prefab != null)
            {
                var rock = Instantiate(prefab, transform);
                rock.name = $"Rock_{nodeDef.Id}";
                Vector3 tilePos = GetTilePosition(nodeDef.Position);
                rock.transform.localPosition = new Vector3(tilePos.x, 0f, tilePos.z);
                rock.transform.localRotation = Quaternion.Euler(0f, (nodeDef.Id % 4) * 90f, 0f);
                FitRockToTile(rock, 0.88f, 1.35f);
                foreach (var col in rock.GetComponentsInChildren<Collider>())
                    col.enabled = false;
                return;
            }

            var wallGo = new GameObject("Wall");
            wallGo.transform.SetParent(parentTile.transform);
            wallGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            wallGo.transform.localRotation = Quaternion.identity;
            wallGo.transform.localScale = Vector3.one;
            var meshFilter = wallGo.AddComponent<MeshFilter>();
            var meshRenderer = wallGo.AddComponent<MeshRenderer>();
            var neighbors = GetImpassableNeighbors(nodeDef);
            meshFilter.mesh = CreateWallMesh(0.25f, 2.4f, neighbors);
            var wallMat = CreateTileMaterial(style);
            wallMat.color = style.Color * 0.85f;
            meshRenderer.material = wallMat;
        }

        private static void FitRockToTile(GameObject rock, float footprint, float maxHeight)
        {
            var renderers = rock.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            float span = Mathf.Max(bounds.size.x, bounds.size.z);
            float scale = span > 0.0001f ? footprint / span : 1f;
            if (bounds.size.y * scale > maxHeight)
                scale = maxHeight / bounds.size.y;

            rock.transform.localScale *= scale;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            Vector3 pos = rock.transform.position;
            pos.x += rock.transform.position.x - bounds.center.x;
            pos.z += rock.transform.position.z - bounds.center.z;
            pos.y += rock.transform.position.y - bounds.min.y;
            rock.transform.position = pos;
        }

        /// <summary>
        /// Checks which adjacent tiles are also Impassable.
        /// Returns flags for 8 directions: N, NE, E, SE, S, SW, W, NW
        /// </summary>
        private bool[] GetImpassableNeighbors(NodeDefinition nodeDef)
        {
            var neighbors = new bool[8]; // N, NE, E, SE, S, SW, W, NW
            
            if (_boardGraph == null) return neighbors;
            
            var coords = _boardGraph.Definition.GetCoordinates(nodeDef.Id);
            int width = _boardGraph.Definition.Width;
            int height = _boardGraph.Definition.Height;
            
            // Direction offsets: N, NE, E, SE, S, SW, W, NW
            var offsets = new Vector2Int[]
            {
                new Vector2Int(0, 1),   // N
                new Vector2Int(1, 1),   // NE
                new Vector2Int(1, 0),   // E
                new Vector2Int(1, -1),  // SE
                new Vector2Int(0, -1),  // S
                new Vector2Int(-1, -1), // SW
                new Vector2Int(-1, 0),  // W
                new Vector2Int(-1, 1)   // NW
            };
            
            for (int i = 0; i < 8; i++)
            {
                int nx = coords.x + offsets[i].x;
                int ny = coords.y + offsets[i].y;
                
                if (nx < 0 || nx >= width || ny < 0 || ny >= height)
                    continue;
                
                int neighborId = _boardGraph.Definition.GetNodeId(nx, ny);
                var neighborState = _boardGraph.State.GetNode(neighborId);
                neighbors[i] = neighborState.IsImpassable;
            }
            
            return neighbors;
        }

        // Temporary storage for UVs during mesh construction
        private List<Vector2> _meshUVs;

        /// <summary>
        /// Creates a wall mesh that extends toward neighboring walls.
        /// </summary>
        private Mesh CreateWallMesh(float baseSize, float height, bool[] neighbors)
        {
            var mesh = new Mesh();
            mesh.name = "Wall";
            
            // Calculate extensions based on neighbors
            // neighbors: N(0), NE(1), E(2), SE(3), S(4), SW(5), W(6), NW(7)
            float edgeOfTile = 0.5f; // Edge of tile in local space
            
            // Start with base size (smaller than tile)
            float minX = -baseSize;
            float maxX = baseSize;
            float minZ = -baseSize;
            float maxZ = baseSize;
            
            // Extend toward orthogonal neighbors (to edge of tile)
            if (neighbors[0]) maxZ = edgeOfTile;  // N
            if (neighbors[2]) maxX = edgeOfTile;  // E
            if (neighbors[4]) minZ = -edgeOfTile; // S
            if (neighbors[6]) minX = -edgeOfTile; // W
            
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            _meshUVs = new List<Vector2>();
            
            // Main wall box
            AddBoxToMesh(vertices, triangles, minX, maxX, minZ, maxZ, 0, height);
            
            // Add corner extensions for diagonals
            // Only add corner if diagonal neighbor exists AND at least one adjacent orthogonal
            // NE corner (between N and E)
            if (neighbors[1] && (neighbors[0] || neighbors[2]))
            {
                float cornerMinX = neighbors[2] ? maxX : baseSize;
                float cornerMaxZ = neighbors[0] ? maxZ : baseSize;
                AddBoxToMesh(vertices, triangles, cornerMinX, edgeOfTile, cornerMaxZ, edgeOfTile, 0, height);
            }
            // SE corner (between E and S)
            if (neighbors[3] && (neighbors[2] || neighbors[4]))
            {
                float cornerMinX = neighbors[2] ? maxX : baseSize;
                float cornerMinZ = neighbors[4] ? minZ : -baseSize;
                AddBoxToMesh(vertices, triangles, cornerMinX, edgeOfTile, -edgeOfTile, cornerMinZ, 0, height);
            }
            // SW corner (between S and W)
            if (neighbors[5] && (neighbors[4] || neighbors[6]))
            {
                float cornerMaxX = neighbors[6] ? minX : -baseSize;
                float cornerMinZ = neighbors[4] ? minZ : -baseSize;
                AddBoxToMesh(vertices, triangles, -edgeOfTile, cornerMaxX, -edgeOfTile, cornerMinZ, 0, height);
            }
            // NW corner (between W and N)
            if (neighbors[7] && (neighbors[6] || neighbors[0]))
            {
                float cornerMaxX = neighbors[6] ? minX : -baseSize;
                float cornerMaxZ = neighbors[0] ? maxZ : baseSize;
                AddBoxToMesh(vertices, triangles, -edgeOfTile, cornerMaxX, cornerMaxZ, edgeOfTile, 0, height);
            }
            
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.uv = _meshUVs.ToArray();
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            return mesh;
        }

        /// <summary>
        /// Adds a box (6 faces) to the mesh vertex and triangle lists with proper UVs.
        /// Each face gets its own vertices for correct UV mapping.
        /// </summary>
        private void AddBoxToMesh(List<Vector3> vertices, List<int> triangles,
            float minX, float maxX, float minZ, float maxZ, float minY, float maxY)
        {
            // We need separate vertices per face for proper UVs (24 vertices instead of 8)
            int baseIndex = vertices.Count;
            
            float width = maxX - minX;
            float depth = maxZ - minZ;
            float height = maxY - minY;
            
            // Front face (Z = minZ)
            vertices.Add(new Vector3(minX, minY, minZ));
            vertices.Add(new Vector3(maxX, minY, minZ));
            vertices.Add(new Vector3(maxX, maxY, minZ));
            vertices.Add(new Vector3(minX, maxY, minZ));
            _meshUVs.Add(new Vector2(0, 0));
            _meshUVs.Add(new Vector2(width, 0));
            _meshUVs.Add(new Vector2(width, height));
            _meshUVs.Add(new Vector2(0, height));
            triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 2);
            
            // Back face (Z = maxZ)
            baseIndex = vertices.Count;
            vertices.Add(new Vector3(maxX, minY, maxZ));
            vertices.Add(new Vector3(minX, minY, maxZ));
            vertices.Add(new Vector3(minX, maxY, maxZ));
            vertices.Add(new Vector3(maxX, maxY, maxZ));
            _meshUVs.Add(new Vector2(0, 0));
            _meshUVs.Add(new Vector2(width, 0));
            _meshUVs.Add(new Vector2(width, height));
            _meshUVs.Add(new Vector2(0, height));
            triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 2);
            
            // Right face (X = maxX)
            baseIndex = vertices.Count;
            vertices.Add(new Vector3(maxX, minY, minZ));
            vertices.Add(new Vector3(maxX, minY, maxZ));
            vertices.Add(new Vector3(maxX, maxY, maxZ));
            vertices.Add(new Vector3(maxX, maxY, minZ));
            _meshUVs.Add(new Vector2(0, 0));
            _meshUVs.Add(new Vector2(depth, 0));
            _meshUVs.Add(new Vector2(depth, height));
            _meshUVs.Add(new Vector2(0, height));
            triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 2);
            
            // Left face (X = minX)
            baseIndex = vertices.Count;
            vertices.Add(new Vector3(minX, minY, maxZ));
            vertices.Add(new Vector3(minX, minY, minZ));
            vertices.Add(new Vector3(minX, maxY, minZ));
            vertices.Add(new Vector3(minX, maxY, maxZ));
            _meshUVs.Add(new Vector2(0, 0));
            _meshUVs.Add(new Vector2(depth, 0));
            _meshUVs.Add(new Vector2(depth, height));
            _meshUVs.Add(new Vector2(0, height));
            triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 2);
            
            // Top face (Y = maxY)
            baseIndex = vertices.Count;
            vertices.Add(new Vector3(minX, maxY, minZ));
            vertices.Add(new Vector3(maxX, maxY, minZ));
            vertices.Add(new Vector3(maxX, maxY, maxZ));
            vertices.Add(new Vector3(minX, maxY, maxZ));
            _meshUVs.Add(new Vector2(0, 0));
            _meshUVs.Add(new Vector2(width, 0));
            _meshUVs.Add(new Vector2(width, depth));
            _meshUVs.Add(new Vector2(0, depth));
            triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 2);
            
            // Bottom face (Y = minY)
            baseIndex = vertices.Count;
            vertices.Add(new Vector3(minX, minY, maxZ));
            vertices.Add(new Vector3(maxX, minY, maxZ));
            vertices.Add(new Vector3(maxX, minY, minZ));
            vertices.Add(new Vector3(minX, minY, minZ));
            _meshUVs.Add(new Vector2(0, 0));
            _meshUVs.Add(new Vector2(width, 0));
            _meshUVs.Add(new Vector2(width, depth));
            _meshUVs.Add(new Vector2(0, depth));
            triangles.Add(baseIndex + 0); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 1);
            triangles.Add(baseIndex + 1); triangles.Add(baseIndex + 3); triangles.Add(baseIndex + 2);
        }
        
        private Material CreateTileMaterial(TileStyle style)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") 
                         ?? Shader.Find("Standard");
            var mat = new Material(shader);
            
            if (style.Texture != null)
            {
                mat.mainTexture = style.Texture;
                mat.mainTextureScale = new Vector2(style.TextureTiling, style.TextureTiling);
            }
            mat.color = style.Color;
            mat.SetFloat("_Smoothness", style.Smoothness);
            
            return mat;
        }

        private TileStyle GetTileStyle(NodeDefinition nodeDef, NodeState nodeState)
        {
            return BoardSkins.Current.GetStyle(nodeState.CurrentType, nodeDef.IsLightTile);
        }

        private Vector3 GetTilePosition(Vector2 gridPosition)
        {
            float step = _tileSize + _tileSpacing;
            return new Vector3(
                gridPosition.x * step,
                0,
                gridPosition.y * step
            );
        }

        /// <summary>
        /// Get world position for a tile (for placing units).
        /// </summary>
        public Vector3 GetWorldPositionForNode(int nodeId)
        {
            if (_boardGraph == null) return Vector3.zero;
            var pos = _boardGraph.GetNodePosition(nodeId);
            return GetTilePosition(pos);
        }

        public float TileStep => _tileSize + _tileSpacing;

        private void RenderSpecialConnections(BoardGraph boardGraph)
        {
            // Render teleport connections
            foreach (var node in boardGraph.Definition.Nodes)
            {
                if (node.TeleportTargetId >= 0)
                {
                    var targetNode = boardGraph.Definition.Nodes[node.TeleportTargetId];
                    DrawConnection(node.Position, targetNode.Position, Color.blue);
                }
            }
        }

        private void DrawConnection(Vector2 from, Vector2 to, Color color)
        {
            if (_connectionPrefab != null)
            {
                var line = Instantiate(_connectionPrefab, transform);
                line.positionCount = 2;
                line.SetPosition(0, GetTilePosition(from) + Vector3.up * 0.1f);
                line.SetPosition(1, GetTilePosition(to) + Vector3.up * 0.1f);
                line.startColor = color;
                line.endColor = color;
            }
        }

        public void ClearBoard()
        {
            foreach (var tile in _tiles.Values)
            {
                if (tile != null)
                    Destroy(tile.gameObject);
            }
            _tiles.Clear();
            _highlightedMoves.Clear();
            _highlightedAttacks.Clear();
            _highlightedHover.Clear();
            _indicatedTiles.Clear();
        }

        #endregion

        #region Highlighting

        private Color GetSelectionColor(int ownerId)
        {
            return PlayerColors.Get(ownerId).SecondaryColor;
        }

        private Color GetHoverColor(int ownerId)
        {
            return PlayerColors.Get(ownerId).PrimaryColor;
        }

        /// <summary>
        /// Highlight valid move targets with player-specific color.
        /// </summary>
        public void HighlightValidMoves(List<int> nodeIds, int ownerId = 0)
        {
            ClearHighlights();

            Color highlightColor = GetSelectionColor(ownerId);

            foreach (var nodeId in nodeIds)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.SetSelectionHighlight(true, highlightColor);
                    _highlightedMoves.Add(nodeId);
                }
            }
        }

        /// <summary>
        /// Highlight attack targets (different color).
        /// </summary>
        public void HighlightAttackTargets(List<int> nodeIds)
        {
            foreach (var nodeId in nodeIds)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.SetSelectionHighlight(true, _attackHighlightColor);
                    _highlightedAttacks.Add(nodeId);
                }
            }
        }

        /// <summary>
        /// Highlight hover preview moves (more transparent, coexists with selection).
        /// </summary>
        public void HighlightHoverMoves(List<int> nodeIds, int ownerId = 0)
        {
            ClearHoverHighlights();

            Color hoverColor = GetHoverColor(ownerId);

            foreach (var nodeId in nodeIds)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.SetHoverHighlight(true, hoverColor);
                    _highlightedHover.Add(nodeId);
                }
            }
        }

        /// <summary>
        /// Show categorized move indicators for hover (circle for move, ring for capture, crosshair for ranged) in secondary color.
        /// Hover has PRIORITY over selection - it will override selection indicators.
        /// </summary>
        public void ShowCategorizedHoverMoves(MoveTargets moves, int ownerId = 0)
        {
            ClearHoverHighlights();

            Color hoverColor = GetHoverColor(ownerId);
            
            // Apply markers from MoveTargets - each list adds its corresponding marker
            ApplyMarkersFromMoveTargets(moves, hoverColor, _highlightedHover);
        }

        /// <summary>
        /// Show categorized move indicators (circle for move, ring for capture, crosshair for ranged).
        /// </summary>
        public void ShowCategorizedMoves(MoveTargets moves, int ownerId = 0)
        {
            ClearHighlights();
            ClearMoveIndicators();

            Color indicatorColor = GetSelectionColor(ownerId);
            
            // Store selection state for restoring after hover
            _currentSelectionMoves = moves;
            _currentSelectionColor = indicatorColor;

            // Apply markers from MoveTargets - each list adds its corresponding marker
            ApplyMarkersFromMoveTargets(moves, indicatorColor, _indicatedTiles);
        }

        /// <summary>
        /// Apply markers to tiles based on MoveTargets categories.
        /// Each category adds its marker independently - tiles can have multiple markers.
        /// </summary>
        private void ApplyMarkersFromMoveTargets(MoveTargets moves, Color color, HashSet<int> trackingSet)
        {
            // MoveOnly and Both can move to empty squares
            foreach (var nodeId in moves.MoveOnly)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.SetMarker(TileMarkers.Move, color);
                    trackingSet.Add(nodeId);
                }
            }
            
            foreach (var nodeId in moves.Both)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.SetMarker(TileMarkers.Move, color);
                    tile.SetMarker(TileMarkers.Capture, color);
                    trackingSet.Add(nodeId);
                }
            }

            // CaptureOnly can only capture
            foreach (var nodeId in moves.CaptureOnly)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.SetMarker(TileMarkers.Capture, color);
                    trackingSet.Add(nodeId);
                }
            }

            // RangedCapture is independent - can be combined with Move
            foreach (var nodeId in moves.RangedCapture)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.SetMarker(TileMarkers.RangedCapture, color);
                    trackingSet.Add(nodeId);
                }
            }
        }

        /// <summary>
        /// Clear all move indicators.
        /// </summary>
        public void ClearMoveIndicators()
        {
            foreach (var nodeId in _indicatedTiles)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.ClearAllMarkers();
                }
            }
            _indicatedTiles.Clear();
        }

        /// <summary>
        /// Clear selection highlights only.
        /// </summary>
        public void ClearHighlights()
        {
            foreach (var nodeId in _highlightedMoves)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.SetSelectionHighlight(false, Color.white);
                }
            }
            _highlightedMoves.Clear();

            foreach (var nodeId in _highlightedAttacks)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.SetSelectionHighlight(false, Color.white);
                }
            }
            _highlightedAttacks.Clear();

            ClearMoveIndicators();
            
            // Clear stored selection state
            _currentSelectionMoves = null;
        }

        /// <summary>
        /// Clear hover highlights only and restore selection indicators.
        /// </summary>
        public void ClearHoverHighlights()
        {
            foreach (var nodeId in _highlightedHover)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.SetHoverHighlight(false, Color.white);
                    tile.ClearAllMarkers();
                }
            }
            _highlightedHover.Clear();
            
            // Restore selection indicators that may have been overwritten by hover
            RestoreSelectionIndicators();
        }

        private void RestoreSelectionIndicators()
        {
            if (_currentSelectionMoves == null) return;

            // Re-apply markers using the same logic as ShowCategorizedMoves
            var tempSet = new HashSet<int>();
            ApplyMarkersFromMoveTargets(_currentSelectionMoves, _currentSelectionColor, tempSet);
        }

        #endregion

        #region Updates

        /// <summary>
        /// Update a single tile's appearance (when board changes during game).
        /// </summary>
        public void UpdateTile(int nodeId, NodeState newState)
        {
            var nodeDef = _boardGraph.Definition.Nodes[nodeId];
            
            // Handle Abyss: destroy the tile if it exists
            if (newState.CurrentType == NodeType.Abyss)
            {
                if (_tiles.TryGetValue(nodeId, out var existingTile))
                {
                    Destroy(existingTile.gameObject);
                    _tiles.Remove(nodeId);
                }
                return;
            }
            
            // Handle tile that was Abyss but now is something else: create it
            if (!_tiles.ContainsKey(nodeId))
            {
                CreateTile(nodeDef, newState);
                return;
            }
            
            // Normal update
            if (_tiles.TryGetValue(nodeId, out var tile))
            {
                var style = GetTileStyle(nodeDef, newState);
                tile.UpdateState(newState, style);
            }
        }

        #endregion
    }

    /// <summary>
    /// Simple click handler for placeholder tiles.
    /// </summary>
    public class TileClickHandler : MonoBehaviour
    {
        public int TileId;
        public System.Action<int> OnClicked;

        // Click handling moved to InputHandler (OnMouseDown uses old Input system)
    }
}

