using UnityEngine;
using BizarreChess.Core.Graph;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Renders a single tile on the board.
    /// </summary>
    public class TileRenderer : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private SpriteRenderer _highlightRenderer;
        [SerializeField] private TMPro.TextMeshPro _debugText;

        public int NodeId { get; private set; }
        public System.Action OnClicked;

        private Color _baseColor;
        private Renderer _placeholderRenderer;
        private bool _isPlaceholder;

        public void Initialize(NodeDefinition nodeDef, NodeState nodeState, Color color, float size)
        {
            NodeId = nodeDef.Id;
            _baseColor = color;

            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = color;
                transform.localScale = Vector3.one * size;
            }

            if (_highlightRenderer != null)
            {
                _highlightRenderer.enabled = false;
            }

            if (_debugText != null)
            {
                _debugText.text = GetNodeTypeSymbol(nodeState.CurrentType);
            }

            UpdateVisualForNodeType(nodeState);
        }

        public void InitializePlaceholder(int nodeId, Renderer renderer)
        {
            NodeId = nodeId;
            _placeholderRenderer = renderer;
            _isPlaceholder = true;
            _baseColor = renderer.material.color;
        }

        public void UpdateState(NodeState state, Color color)
        {
            _baseColor = color;
            
            if (_isPlaceholder && _placeholderRenderer != null)
            {
                _placeholderRenderer.material.color = color;
            }
            else if (_spriteRenderer != null)
            {
                _spriteRenderer.color = color;
            }

            if (_debugText != null)
            {
                _debugText.text = GetNodeTypeSymbol(state.CurrentType);
            }

            UpdateVisualForNodeType(state);
        }

        public void SetHighlight(bool highlighted, Color highlightColor)
        {
            if (_isPlaceholder && _placeholderRenderer != null)
            {
                _placeholderRenderer.material.color = highlighted 
                    ? Color.Lerp(_baseColor, highlightColor, 0.5f) 
                    : _baseColor;
            }
            else if (_highlightRenderer != null)
            {
                _highlightRenderer.enabled = highlighted;
                _highlightRenderer.color = highlightColor;
            }
            else if (_spriteRenderer != null)
            {
                _spriteRenderer.color = highlighted 
                    ? Color.Lerp(_baseColor, highlightColor, 0.5f) 
                    : _baseColor;
            }
        }

        private void UpdateVisualForNodeType(NodeState state)
        {
            // Add visual effects based on node type
            switch (state.CurrentType)
            {
                case NodeType.Teleport:
                    // Could add particle effect, glow, etc.
                    break;
                case NodeType.Unstable:
                    // Could add shake animation
                    break;
            }
        }

        private string GetNodeTypeSymbol(NodeType type)
        {
            return type switch
            {
                NodeType.Normal => "",
                NodeType.Impassable => "X",
                NodeType.Boost => "↑",
                NodeType.Trap => "!",
                NodeType.Teleport => "◎",
                NodeType.Destroyed => "░",
                NodeType.Unstable => "~",
                _ => ""
            };
        }

        // Click handling moved to InputHandler (OnMouseDown uses old Input system)
    }
}

