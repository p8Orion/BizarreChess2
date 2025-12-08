using UnityEngine;
using BizarreChess.Core.Board;
using BizarreChess.Core.Board;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Renders a single tile on the board.
    /// Supports two layers of highlight: selection (opaque) and hover (transparent).
    /// </summary>
    public class TileRenderer : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private SpriteRenderer _highlightRenderer;
        [SerializeField] private TMPro.TextMeshPro _debugText;

        public int NodeId { get; private set; }
        public System.Action OnClicked;

        private Color _baseColor;
        private TileStyle _currentStyle;
        private Renderer _placeholderRenderer;
        private bool _isPlaceholder;
        
        // Dual highlight state
        private bool _isSelectionHighlighted;
        private Color _selectionColor;
        private bool _isHoverHighlighted;
        private Color _hoverColor;

        public void Initialize(NodeDefinition nodeDef, NodeState nodeState, TileStyle style, float size)
        {
            NodeId = nodeDef.Id;
            _currentStyle = style;
            _baseColor = style.Color;

            if (_spriteRenderer != null)
            {
                _spriteRenderer.color = style.Color;
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

        public void InitializePlaceholder(int nodeId, Renderer renderer, TileStyle style)
        {
            NodeId = nodeId;
            _placeholderRenderer = renderer;
            _isPlaceholder = true;
            _currentStyle = style;
            _baseColor = style.Color;
        }

        public void UpdateState(NodeState state, TileStyle style)
        {
            _currentStyle = style;
            _baseColor = style?.Color ?? Color.magenta;
            
            if (_isPlaceholder && _placeholderRenderer != null)
            {
                ApplyStyleToMaterial(_placeholderRenderer.material, style);
            }
            else if (_spriteRenderer != null)
            {
                _spriteRenderer.color = _baseColor;
            }

            if (_debugText != null)
            {
                _debugText.text = GetNodeTypeSymbol(state.CurrentType);
            }

            UpdateVisualForNodeType(state);
        }
        
        private void ApplyStyleToMaterial(Material mat, TileStyle style)
        {
            if (style == null) return;
            
            if (style.Texture != null)
            {
                mat.mainTexture = style.Texture;
                mat.mainTextureScale = new Vector2(style.TextureTiling, style.TextureTiling);
            }
            else
            {
                mat.mainTexture = null;
            }
            mat.color = style.Color;
            
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", style.Smoothness);
        }

        /// <summary>
        /// Set selection highlight (stronger, for selected piece moves).
        /// </summary>
        public void SetSelectionHighlight(bool highlighted, Color highlightColor)
        {
            _isSelectionHighlighted = highlighted;
            _selectionColor = highlightColor;
            UpdateCombinedHighlight();
        }

        /// <summary>
        /// Set hover highlight (weaker, for preview on hover).
        /// </summary>
        public void SetHoverHighlight(bool highlighted, Color highlightColor)
        {
            _isHoverHighlighted = highlighted;
            _hoverColor = highlightColor;
            UpdateCombinedHighlight();
        }

        /// <summary>
        /// Legacy method for compatibility.
        /// </summary>
        public void SetHighlight(bool highlighted, Color highlightColor)
        {
            SetSelectionHighlight(highlighted, highlightColor);
        }

        private void UpdateCombinedHighlight()
        {
            // Priority: Selection > Hover > Base
            // But we want to show both if they're different tiles
            // For the same tile, selection takes precedence visually but we blend
            
            Color finalColor = _baseColor;
            bool anyHighlight = _isSelectionHighlighted || _isHoverHighlighted;
            
            if (_isSelectionHighlighted && _isHoverHighlighted)
            {
                // Both active - blend selection color (stronger) with hover (weaker)
                // Selection takes visual priority
                finalColor = Color.Lerp(_baseColor, _selectionColor, 0.6f);
            }
            else if (_isSelectionHighlighted)
            {
                finalColor = Color.Lerp(_baseColor, _selectionColor, 0.5f);
            }
            else if (_isHoverHighlighted)
            {
                finalColor = Color.Lerp(_baseColor, _hoverColor, 0.35f);
            }
            
            // Apply to renderer
            if (_isPlaceholder && _placeholderRenderer != null)
            {
                _placeholderRenderer.material.color = finalColor;
            }
            else if (_highlightRenderer != null)
            {
                _highlightRenderer.enabled = anyHighlight;
                if (anyHighlight)
                {
                    _highlightRenderer.color = _isSelectionHighlighted ? _selectionColor : _hoverColor;
                }
            }
            else if (_spriteRenderer != null)
            {
                _spriteRenderer.color = finalColor;
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
                NodeType.Abyss => "▼",
                _ => ""
            };
        }

        // Click handling moved to InputHandler (OnMouseDown uses old Input system)
    }
}

