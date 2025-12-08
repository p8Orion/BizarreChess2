using UnityEngine;
using BizarreChess.Core.Board;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Type of move indicator to display on a tile.
    /// </summary>
    public enum MoveIndicatorType
    {
        None,
        MoveOnly,      // Solid circle (can move but not capture)
        CaptureOnly,   // Ring (can capture but not move to empty)
        Both           // Circle inside ring (can both move and capture)
    }

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
        
        // Move indicator objects (created dynamically)
        private GameObject _moveCircle;
        private GameObject _captureRing;
        private MoveIndicatorType _currentIndicator = MoveIndicatorType.None;

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

        #region Move Indicators

        /// <summary>
        /// Show move indicator (circle for move, ring for capture, both for normal).
        /// </summary>
        public void SetMoveIndicator(MoveIndicatorType type, Color color)
        {
            if (_currentIndicator == type && type == MoveIndicatorType.None)
                return;

            _currentIndicator = type;

            // Create indicators if needed
            EnsureIndicatorsCreated();

            // Show/hide based on type
            bool showCircle = type == MoveIndicatorType.MoveOnly || type == MoveIndicatorType.Both;
            bool showRing = type == MoveIndicatorType.CaptureOnly || type == MoveIndicatorType.Both;

            if (_moveCircle != null)
            {
                _moveCircle.SetActive(showCircle);
                if (showCircle)
                {
                    var renderer = _moveCircle.GetComponent<MeshRenderer>();
                    if (renderer != null)
                        ApplyColorToMaterial(renderer.material, color);
                }
            }

            if (_captureRing != null)
            {
                _captureRing.SetActive(showRing);
                if (showRing)
                {
                    // Same color as circle: select = primary, hover = secondary
                    var renderer = _captureRing.GetComponent<MeshRenderer>();
                    if (renderer != null)
                        ApplyColorToMaterial(renderer.material, color);
                }
            }
        }

        /// <summary>
        /// Clear all move indicators.
        /// </summary>
        public void ClearMoveIndicator()
        {
            SetMoveIndicator(MoveIndicatorType.None, Color.white);
        }

        private void EnsureIndicatorsCreated()
        {
            if (_moveCircle == null)
            {
                // Circle radius matches inner radius of ring so they fit together
                _moveCircle = CreateCircleIndicator("MoveCircle", 0.34f, 0f);
            }
            if (_captureRing == null)
            {
                _captureRing = CreateRingIndicator("CaptureRing", 0.45f, 0.35f);
            }
        }

        private GameObject CreateCircleIndicator(string name, float radius, float unused)
        {
            var go = new GameObject(name);
            
            // Create quad mesh for decal
            var meshFilter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();
            
            meshFilter.mesh = CreateQuadMesh();
            meshRenderer.material = CreateDecalMaterial(CreateCircleTexture(128));
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            
            // Position just above tile surface
            Vector3 tileWorldPos = transform.position;
            float tileTopY = tileWorldPos.y + transform.lossyScale.y * 0.5f + 0.01f;
            go.transform.position = new Vector3(tileWorldPos.x, tileTopY, tileWorldPos.z);
            go.transform.rotation = Quaternion.Euler(90, 0, 0); // Face up
            go.transform.localScale = new Vector3(radius * 2f, radius * 2f, 1f);
            
            // Parent while keeping world transform
            go.transform.SetParent(transform, worldPositionStays: true);

            go.SetActive(false);
            return go;
        }

        private GameObject CreateRingIndicator(string name, float outerRadius, float innerRadius)
        {
            var go = new GameObject(name);
            
            // Create quad mesh for decal
            var meshFilter = go.AddComponent<MeshFilter>();
            var meshRenderer = go.AddComponent<MeshRenderer>();
            
            meshFilter.mesh = CreateQuadMesh();
            meshRenderer.material = CreateDecalMaterial(CreateRingTexture(128, innerRadius / outerRadius));
            meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
            
            // Position just above tile surface
            Vector3 tileWorldPos = transform.position;
            float tileTopY = tileWorldPos.y + transform.lossyScale.y * 0.5f + 0.01f;
            go.transform.position = new Vector3(tileWorldPos.x, tileTopY, tileWorldPos.z);
            go.transform.rotation = Quaternion.Euler(90, 0, 0); // Face up
            go.transform.localScale = new Vector3(outerRadius * 2f, outerRadius * 2f, 1f);
            
            // Parent while keeping world transform
            go.transform.SetParent(transform, worldPositionStays: true);

            go.SetActive(false);
            return go;
        }

        private Mesh CreateQuadMesh()
        {
            var mesh = new Mesh();
            
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0)
            };
            
            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };
            
            mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
            mesh.normals = new Vector3[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            
            return mesh;
        }

        private Texture2D CreateCircleTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float radius = size / 2f - 2f;
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    
                    if (dist <= radius)
                    {
                        // Soft edge - 60% transparency
                        float alpha = Mathf.Clamp01((radius - dist) / 3f);
                        tex.SetPixel(x, y, new Color(1, 1, 1, alpha * 0.6f));
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        private Texture2D CreateRingTexture(int size, float innerRatio)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            float center = size / 2f;
            float outerRadius = size / 2f - 2f;
            float innerRadius = outerRadius * innerRatio;
            
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    
                    if (dist <= outerRadius && dist >= innerRadius)
                    {
                        // Soft edges on both inner and outer - 60% transparency
                        float outerAlpha = Mathf.Clamp01((outerRadius - dist) / 3f);
                        float innerAlpha = Mathf.Clamp01((dist - innerRadius) / 3f);
                        float alpha = Mathf.Min(outerAlpha, innerAlpha);
                        tex.SetPixel(x, y, new Color(1, 1, 1, alpha * 0.6f));
                    }
                    else
                    {
                        tex.SetPixel(x, y, Color.clear);
                    }
                }
            }
            
            tex.Apply();
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        private Material CreateDecalMaterial(Texture2D texture)
        {
            // Use transparent/unlit shader
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
                shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            
            var mat = new Material(shader);
            mat.mainTexture = texture;
            mat.color = Color.white;
            
            // Render on top of tiles
            mat.renderQueue = 3000;
            
            return mat;
        }

        private void ApplyColorToMaterial(Material mat, Color color)
        {
            mat.color = color;
        }

        #endregion

        // Click handling moved to InputHandler (OnMouseDown uses old Input system)
    }
}

