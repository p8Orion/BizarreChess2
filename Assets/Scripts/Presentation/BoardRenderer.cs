using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Graph;

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
        [SerializeField] private Color _lightTileColor = new Color(0.93f, 0.86f, 0.70f);
        [SerializeField] private Color _darkTileColor = new Color(0.55f, 0.36f, 0.24f);
        [SerializeField] private Color _highlightColor = new Color(0.5f, 1f, 0.5f, 0.5f);
        [SerializeField] private Color _attackHighlightColor = new Color(1f, 0.5f, 0.5f, 0.5f);
        [SerializeField] private Color _specialTileColor = new Color(1f, 0.84f, 0f, 0.5f);

        [Header("Layout")]
        [SerializeField] private float _tileSize = 1f;
        [SerializeField] private float _tileSpacing = 0.1f;

        private Dictionary<int, TileRenderer> _tiles = new Dictionary<int, TileRenderer>();
        private BoardGraph _boardGraph;
        private HashSet<int> _highlightedMoves = new HashSet<int>();
        private HashSet<int> _highlightedAttacks = new HashSet<int>();

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
            if (_tilePrefab == null)
            {
                CreatePlaceholderTile(nodeDef, nodeState);
                return;
            }

            var tile = Instantiate(_tilePrefab, transform);
            tile.Initialize(nodeDef, nodeState, GetTileColor(nodeDef, nodeState), _tileSize);
            tile.transform.localPosition = GetTilePosition(nodeDef.Position);
            tile.OnClicked += () => OnTileClicked?.Invoke(nodeDef.Id);

            _tiles[nodeDef.Id] = tile;
        }

        private void CreatePlaceholderTile(NodeDefinition nodeDef, NodeState nodeState)
        {
            // Create simple quad as placeholder
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = $"Tile_{nodeDef.Id}";
            go.transform.SetParent(transform);
            go.transform.localPosition = GetTilePosition(nodeDef.Position);
            go.transform.localScale = Vector3.one * _tileSize * 0.95f;
            go.transform.rotation = Quaternion.Euler(90, 0, 0);

            var renderer = go.GetComponent<Renderer>();
            renderer.material = new Material(Shader.Find("Unlit/Color"));
            renderer.material.color = GetTileColor(nodeDef, nodeState);

            // Add click handler
            var clickHandler = go.AddComponent<TileClickHandler>();
            clickHandler.TileId = nodeDef.Id;
            clickHandler.OnClicked += (id) => OnTileClicked?.Invoke(id);

            // Create a TileRenderer wrapper
            var tileRenderer = go.AddComponent<TileRenderer>();
            tileRenderer.InitializePlaceholder(nodeDef.Id, renderer);

            _tiles[nodeDef.Id] = tileRenderer;
        }

        private Color GetTileColor(NodeDefinition nodeDef, NodeState nodeState)
        {
            // Special tile types override base color
            switch (nodeState.CurrentType)
            {
                case NodeType.Destroyed:
                    return Color.black;
                case NodeType.Impassable:
                    return Color.gray;
                case NodeType.Boost:
                    return Color.green * 0.7f;
                case NodeType.Trap:
                    return Color.red * 0.7f;
                case NodeType.Teleport:
                    return Color.blue * 0.7f;
                case NodeType.Unstable:
                    return Color.yellow * 0.7f;
            }

            // Normal tiles use light/dark pattern
            return nodeDef.IsLightTile ? _lightTileColor : _darkTileColor;
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
        }

        #endregion

        #region Highlighting

        /// <summary>
        /// Highlight valid move targets.
        /// </summary>
        public void HighlightValidMoves(List<int> nodeIds)
        {
            ClearHighlights();

            foreach (var nodeId in nodeIds)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.SetHighlight(true, _highlightColor);
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
                    tile.SetHighlight(true, _attackHighlightColor);
                    _highlightedAttacks.Add(nodeId);
                }
            }
        }

        /// <summary>
        /// Clear all highlights.
        /// </summary>
        public void ClearHighlights()
        {
            foreach (var nodeId in _highlightedMoves)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.SetHighlight(false, Color.white);
                }
            }
            _highlightedMoves.Clear();

            foreach (var nodeId in _highlightedAttacks)
            {
                if (_tiles.TryGetValue(nodeId, out var tile))
                {
                    tile.SetHighlight(false, Color.white);
                }
            }
            _highlightedAttacks.Clear();
        }

        #endregion

        #region Updates

        /// <summary>
        /// Update a single tile's appearance (when board changes during game).
        /// </summary>
        public void UpdateTile(int nodeId, NodeState newState)
        {
            if (_tiles.TryGetValue(nodeId, out var tile))
            {
                var nodeDef = _boardGraph.Definition.Nodes[nodeId];
                tile.UpdateState(newState, GetTileColor(nodeDef, newState));
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

