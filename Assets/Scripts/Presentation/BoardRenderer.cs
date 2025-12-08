using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Board;
using BizarreChess.Core.Player;

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
            // Create cube with thickness instead of flat quad
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"Tile_{nodeDef.Id}";
            go.transform.SetParent(transform);
            
            // Position tile so top surface is at y=0 (tile extends downward)
            Vector3 basePos = GetTilePosition(nodeDef.Position);
            go.transform.localPosition = new Vector3(basePos.x, -_tileThickness / 2f, basePos.z);
            go.transform.localScale = new Vector3(_tileSize, _tileThickness, _tileSize);

            var renderer = go.GetComponent<Renderer>();
            var mat = CreateTileMaterial(style);
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
            tileRenderer.InitializePlaceholder(nodeDef.Id, renderer, style);

            _tiles[nodeDef.Id] = tileRenderer;
            
            // Add pyramid mesh for Impassable tiles (mountains)
            if (nodeState.CurrentType == NodeType.Impassable)
            {
                CreateMountainOnTile(go, style);
            }
        }

        /// <summary>
        /// Creates a pyramid (mountain) mesh on top of an Impassable tile.
        /// </summary>
        private void CreateMountainOnTile(GameObject parentTile, TileStyle style)
        {
            var mountainGo = new GameObject("Mountain");
            mountainGo.transform.SetParent(parentTile.transform);
            
            // Position pyramid on top of tile (tile top is at local y=0.5 due to cube scaling)
            mountainGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            mountainGo.transform.localRotation = Quaternion.identity;
            mountainGo.transform.localScale = Vector3.one; // Scale is baked into mesh
            
            var meshFilter = mountainGo.AddComponent<MeshFilter>();
            var meshRenderer = mountainGo.AddComponent<MeshRenderer>();
            
            // Create pyramid mesh sized relative to tile
            float pyramidBase = 0.7f;  // 70% of tile size (in local space)
            float pyramidHeight = 0.8f; // Height in local space
            meshFilter.mesh = CreatePyramidMesh(pyramidBase, pyramidHeight);
            
            // Use same material as tile but slightly darker for depth
            var mountainMat = CreateTileMaterial(style);
            mountainMat.color = style.Color * 0.85f;
            meshRenderer.material = mountainMat;
        }

        /// <summary>
        /// Creates a simple 4-sided pyramid mesh.
        /// </summary>
        private Mesh CreatePyramidMesh(float baseSize, float height)
        {
            var mesh = new Mesh();
            mesh.name = "Pyramid";
            
            float half = baseSize / 2f;
            
            // 5 vertices: 4 base corners + 1 apex
            Vector3[] vertices = new Vector3[]
            {
                // Base corners (y = 0)
                new Vector3(-half, 0, -half),  // 0: front-left
                new Vector3(half, 0, -half),   // 1: front-right
                new Vector3(half, 0, half),    // 2: back-right
                new Vector3(-half, 0, half),   // 3: back-left
                // Apex
                new Vector3(0, height, 0)      // 4: top
            };
            
            // 6 triangles: 4 sides + 2 for base quad
            int[] triangles = new int[]
            {
                // Front face
                0, 4, 1,
                // Right face
                1, 4, 2,
                // Back face
                2, 4, 3,
                // Left face
                3, 4, 0,
                // Base (two triangles)
                0, 1, 2,
                0, 2, 3
            };
            
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            
            return mesh;
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
        }

        #endregion

        #region Highlighting

        private Color GetSelectionColor(int ownerId)
        {
            return PlayerColors.Get(ownerId).SelectHighlight;
        }

        private Color GetHoverColor(int ownerId)
        {
            return PlayerColors.Get(ownerId).HoverHighlight;
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
        }

        /// <summary>
        /// Clear hover highlights only.
        /// </summary>
        public void ClearHoverHighlights()
        {
            foreach (var nodeId in _highlightedHover)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.SetHoverHighlight(false, Color.white);
                }
            }
            _highlightedHover.Clear();
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

