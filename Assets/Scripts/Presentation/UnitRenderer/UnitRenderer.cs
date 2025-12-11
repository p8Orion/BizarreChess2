using UnityEngine;
using TMPro;
using BizarreChess.Core.Units;
using BizarreChess.Core.Player;
using BizarreChess.Core.Skills;
using BizarreChess.Core.Items;

namespace BizarreChess.Presentation.UnitRenderer
{
    /// <summary>
    /// Renders a single unit on the board.
    /// Supports Token2D and RevolutionVolume rendering modes.
    /// </summary>
    public class UnitRenderer : MonoBehaviour
    {
        [Header("Visual Components")]
        [SerializeField] private SpriteRenderer _spriteRenderer;
        [SerializeField] private TextMeshPro _unicodeText;
        [SerializeField] private MeshRenderer _meshRenderer;
        [SerializeField] private SpriteRenderer _selectionIndicator;
        [SerializeField] private GameObject _healthBar;
        [SerializeField] private Transform _healthFill;

        [Header("Colors (read from PlayerColors)")]
        [SerializeField] private Color _damagedColor = Color.red;

        [Header("Animation")]
        [SerializeField] private float _moveSpeed = 5f;
        [SerializeField] private float _bounceHeight = 0.2f;
        
        [Header("Drag Settings")]
        [SerializeField] private float _dragLiftHeight = 0.5f;
        [SerializeField] private float _dragScale = 1.2f;
        [SerializeField] private float _dragLiftSpeed = 10f;
        [SerializeField] private float _returnSpeed = 8f;
        
        [Header("Drag Rotation")]
        [SerializeField] private float _dragRotationSpeed = 12f;
        [SerializeField] private float _dragTiltAngle = 15f;
        [SerializeField] private float _minDragDistanceForRotation = 0.05f;
        
        [Header("Forcefield Settings")]
        [SerializeField] private float _forcefieldScale = 1.4f;
        [SerializeField] private float _forcefieldVerticalOffset = 0.3f;

        public int UnitId { get; private set; }
        public System.Action OnClicked;

        private UnitState _currentState;
        private UnitDefinition _definition;
        private Vector3 _startPosition;
        private Vector3 _targetPosition;
        private bool _isMoving;
        private bool _isSelected;
        private float _moveProgress;
        private PieceRenderMode _renderMode;
        
        // Drag state
        private bool _isDragging;
        private bool _isReturning;
        private Vector3 _originalPosition;
        private Vector3 _originalScale;
        private float _currentLiftProgress;
        
        // Drag rotation state
        private Vector3 _lastDragPosition;
        private float _currentDragYaw;      // Y rotation (facing direction)
        private float _currentDragTilt;     // Forward tilt during drag
        private float _targetDragYaw;
        private bool _hasValidDragDirection;
        
        // Forcefield state
        private bool _hasForcefield;
        private Color _forcefieldColor;
        private GameObject _forcefieldObject;
        private MeshRenderer _forcefieldRenderer;
        private Material _forcefieldMaterial;
        private static Shader _forcefieldShader;

        // Held item state
        private bool _hasHeldItem;
        private GameObject _heldItemObject;
        private MeshRenderer _heldItemRenderer;
        private Material _heldItemMaterial;
        private Item _displayedItem;

        public void Initialize(UnitState state, UnitDefinition definition, Vector3 position)
        {
            UnitId = state.UnitId;
            _currentState = state;
            _definition = definition;
            _targetPosition = position;
            _renderMode = definition.RenderMode;

            // Auto-find components if not assigned (for dynamically created units)
            if (_meshRenderer == null)
                _meshRenderer = GetComponent<MeshRenderer>();
            if (_unicodeText == null)
                _unicodeText = GetComponentInChildren<TextMeshPro>();
            if (_spriteRenderer == null)
                _spriteRenderer = GetComponent<SpriteRenderer>();

            // Both render modes use 3D meshes, position them on the board
            bool hasMesh = _meshRenderer != null;
            if (hasMesh)
            {
                transform.position = position; // 3D pieces sit on the board
            }
            else
            {
                transform.position = position + Vector3.up * 0.5f; // 2D fallback hovers above
            }

            // Check for active forcefield skill
            CheckForcefieldStatus();
            CheckHeldItemStatus();

            UpdateVisuals();
        }
        
        /// <summary>
        /// Check if the unit has an active Forcefield skill.
        /// </summary>
        private void CheckForcefieldStatus()
        {
            var forcefield = _currentState.Skills?.Find(s => s is ForcefieldSkill) as ForcefieldSkill;
            bool shouldHaveForcefield = forcefield != null && forcefield.IsActive;
            
            if (shouldHaveForcefield && !_hasForcefield)
            {
                // Create forcefield visual
                var colorScheme = PlayerColors.Get(_currentState.OwnerId);
                _forcefieldColor = colorScheme.SecondaryColor;
                CreateForcefieldObject();
            }
            else if (!shouldHaveForcefield && _hasForcefield)
            {
                // Remove forcefield visual
                DestroyForcefieldObject();
            }
            
            _hasForcefield = shouldHaveForcefield;
        }
        
        /// <summary>
        /// Create the cloud-like forcefield sphere around the unit.
        /// </summary>
        private void CreateForcefieldObject()
        {
            if (_forcefieldObject != null) return;
            
            // Load shader if not cached
            if (_forcefieldShader == null)
            {
                _forcefieldShader = Shader.Find("BizarreChess/Forcefield");
            }
            
            // Create sphere
            _forcefieldObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _forcefieldObject.name = "Forcefield";
            _forcefieldObject.transform.SetParent(transform);
            _forcefieldObject.transform.localPosition = Vector3.up * _forcefieldVerticalOffset;
            _forcefieldObject.transform.localScale = Vector3.one * _forcefieldScale;
            
            // Remove collider (we don't want it to interfere)
            var collider = _forcefieldObject.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            
            // Setup material
            _forcefieldRenderer = _forcefieldObject.GetComponent<MeshRenderer>();
            
            if (_forcefieldShader != null && _forcefieldShader.isSupported)
            {
                _forcefieldMaterial = new Material(_forcefieldShader);
                
                // Configure the custom forcefield shader with proper parameters
                // Color with good alpha for visibility
                Color shaderColor = new Color(_forcefieldColor.r, _forcefieldColor.g, _forcefieldColor.b, 0.5f);
                _forcefieldMaterial.SetColor("_Color", shaderColor);
                
                // Fresnel - makes edges brighter
                _forcefieldMaterial.SetFloat("_FresnelPower", 2.0f);
                
                // Noise - cloud effect
                _forcefieldMaterial.SetFloat("_NoiseScale", 8.0f);
                _forcefieldMaterial.SetFloat("_NoiseSpeed", 0.5f);
                
                // Pulse - breathing animation
                _forcefieldMaterial.SetFloat("_PulseIntensity", 0.8f);
                
                // Edge glow
                _forcefieldMaterial.SetFloat("_EdgeGlow", 1.2f);
                
                // Ensure proper render queue for transparency
                _forcefieldMaterial.renderQueue = 3000;
                
                _forcefieldRenderer.material = _forcefieldMaterial;
            }
            else
            {
                // Fallback: create a simple transparent glowing sphere
                CreateFallbackForcefieldMaterial();
            }
        }
        
        /// <summary>
        /// Create a fallback material when the custom shader is not available.
        /// </summary>
        private void CreateFallbackForcefieldMaterial()
        {
            // Try Standard shader first
            Shader fallbackShader = Shader.Find("Standard");
            if (fallbackShader == null)
            {
                fallbackShader = Shader.Find("Universal Render Pipeline/Lit");
            }
            
            if (fallbackShader != null)
            {
                _forcefieldMaterial = new Material(fallbackShader);
                
                // Configure for transparency
                _forcefieldMaterial.SetFloat("_Mode", 3); // Transparent mode for Standard shader
                _forcefieldMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                _forcefieldMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                _forcefieldMaterial.SetInt("_ZWrite", 0);
                _forcefieldMaterial.DisableKeyword("_ALPHATEST_ON");
                _forcefieldMaterial.EnableKeyword("_ALPHABLEND_ON");
                _forcefieldMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                _forcefieldMaterial.renderQueue = 3000;
                
                Color forcefieldColorWithAlpha = new Color(_forcefieldColor.r, _forcefieldColor.g, _forcefieldColor.b, 0.4f);
                _forcefieldMaterial.color = forcefieldColorWithAlpha;
                
                // Add emission for glow effect
                _forcefieldMaterial.EnableKeyword("_EMISSION");
                _forcefieldMaterial.SetColor("_EmissionColor", _forcefieldColor * 0.6f);
                
                _forcefieldRenderer.material = _forcefieldMaterial;
            }
        }
        
        /// <summary>
        /// Destroy the forcefield visual object.
        /// </summary>
        private void DestroyForcefieldObject()
        {
            if (_forcefieldObject != null)
            {
                Destroy(_forcefieldObject);
                _forcefieldObject = null;
                _forcefieldRenderer = null;
            }
            
            if (_forcefieldMaterial != null)
            {
                Destroy(_forcefieldMaterial);
                _forcefieldMaterial = null;
            }
        }

        #region Held Item Visual

        /// <summary>
        /// Check if the unit is holding an item and update the visual.
        /// </summary>
        private void CheckHeldItemStatus()
        {
            var heldItem = _currentState?.HeldItem;
            bool shouldShowItem = heldItem != null;

            if (shouldShowItem && (!_hasHeldItem || _displayedItem != heldItem))
            {
                // Create or update held item visual
                DestroyHeldItemObject();
                _displayedItem = heldItem;
                CreateHeldItemObject(heldItem);
            }
            else if (!shouldShowItem && _hasHeldItem)
            {
                // Remove held item visual
                DestroyHeldItemObject();
                _displayedItem = null;
            }

            _hasHeldItem = shouldShowItem;
        }

        /// <summary>
        /// Create a small visual representation of the held item next to the unit.
        /// </summary>
        private void CreateHeldItemObject(Item item)
        {
            if (_heldItemObject != null) return;

            // Create primitive based on item shape
            PrimitiveType primitiveType = item.Shape switch
            {
                ItemShape.Cube => PrimitiveType.Cube,
                ItemShape.Capsule => PrimitiveType.Capsule,
                ItemShape.Cylinder => PrimitiveType.Cylinder,
                _ => PrimitiveType.Sphere
            };

            _heldItemObject = GameObject.CreatePrimitive(primitiveType);
            _heldItemObject.name = $"HeldItem_{item.Id}";
            _heldItemObject.transform.SetParent(transform);

            // Position to the side and slightly above the unit
            _heldItemObject.transform.localPosition = new Vector3(0.4f, 0.8f, 0f);
            _heldItemObject.transform.localScale = Vector3.one * 0.12f;

            // Remove collider (we don't want to click the held item)
            var collider = _heldItemObject.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            // Set up material with item color
            _heldItemRenderer = _heldItemObject.GetComponent<MeshRenderer>();
            if (_heldItemRenderer != null)
            {
                _heldItemMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _heldItemMaterial.color = item.ItemColor;
                _heldItemMaterial.SetFloat("_Smoothness", 0.8f);
                
                // Add emission for visibility
                _heldItemMaterial.EnableKeyword("_EMISSION");
                _heldItemMaterial.SetColor("_EmissionColor", item.ItemColor * 0.3f);
                
                _heldItemRenderer.material = _heldItemMaterial;
            }
        }

        /// <summary>
        /// Destroy the held item visual.
        /// </summary>
        private void DestroyHeldItemObject()
        {
            if (_heldItemObject != null)
            {
                Destroy(_heldItemObject);
                _heldItemObject = null;
                _heldItemRenderer = null;
            }

            if (_heldItemMaterial != null)
            {
                Destroy(_heldItemMaterial);
                _heldItemMaterial = null;
            }
        }

        #endregion

        #region State Update

        public void UpdateState(UnitState state)
        {
            _currentState = state;
            CheckForcefieldStatus();
            CheckHeldItemStatus();
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            // Get player colors and textures from centralized PlayerColors
            var colorScheme = PlayerColors.Get(_currentState.OwnerId);
            Color primaryColor = colorScheme.PrimaryColor;
            Color secondaryColor = colorScheme.SecondaryColor;
            Color outlineColor = _currentState.OwnerId == 0 ? Color.black : Color.white;

            // 3D mesh rendering (Token2D or RevolutionVolume)
            if (_meshRenderer != null && _meshRenderer.material != null)
            {
                var mat = _meshRenderer.material;
                ApplyTextureToMaterial(mat, colorScheme);
            }
            // 2D Unicode text fallback (when no mesh)
            else if (_unicodeText != null)
            {
                char pieceChar = _definition.GetUnicode(_currentState.OwnerId);
                string displayText = ChessUnicode.GetPieceLetter(_definition.PieceType);
                
                if (_unicodeText.font != null && _unicodeText.font.HasCharacter(pieceChar))
                {
                    displayText = pieceChar.ToString();
                }
                
                _unicodeText.text = displayText;
                _unicodeText.fontSize = 5;
                _unicodeText.color = primaryColor;
                _unicodeText.outlineWidth = 0.15f;
                _unicodeText.outlineColor = outlineColor;
                _unicodeText.fontStyle = TMPro.FontStyles.Bold;
            }

            // Or use sprite if available
            if (_spriteRenderer != null && _definition.GetSprite(_currentState.OwnerId) != null)
            {
                _spriteRenderer.sprite = _definition.GetSprite(_currentState.OwnerId);
                _spriteRenderer.color = primaryColor;
                if (_unicodeText != null) _unicodeText.enabled = false;
            }

            // Health bar disabled - no combat system
            if (_healthBar != null)
            {
                _healthBar.SetActive(false);
            }

            // Selection indicator
            if (_selectionIndicator != null)
            {
                _selectionIndicator.enabled = _isSelected;
                _selectionIndicator.color = PlayerColors.Get(_currentState.OwnerId).SelectHighlight;
            }

            // Dead units fade out
            if (!_currentState.IsAlive)
            {
                SetAlpha(0.3f);
            }
        }

        public void SetSelected(bool selected)
        {
            _isSelected = selected;
            
            if (_selectionIndicator != null)
            {
                _selectionIndicator.enabled = selected;
            }

            // Keep consistent scale (no scale change on selection)
            // Token2D has no scale applied, RevolutionVolume has 0.8 scale from generator
            float baseScale = (_renderMode == PieceRenderMode.RevolutionVolume) ? 0.8f : 1f;
            transform.localScale = Vector3.one * baseScale;
            
            // Add emission glow when selected (works for both render modes)
            if (_meshRenderer != null && _meshRenderer.material != null)
            {
                if (selected)
                {
                    _meshRenderer.material.EnableKeyword("_EMISSION");
                    // Use SECONDARY color for selection highlight
                    var colorScheme = PlayerColors.Get(_currentState.OwnerId);
                    Color emissionColor = colorScheme.SecondaryColor * 0.8f;
                    _meshRenderer.material.SetColor("_EmissionColor", emissionColor);
                }
                else
                {
                    // Disable emission when not selected
                    _meshRenderer.material.DisableKeyword("_EMISSION");
                    _meshRenderer.material.SetColor("_EmissionColor", Color.black);
                }
            }
        }

        /// <summary>
        /// Mark this unit as a capturable target (enemy that can be captured).
        /// </summary>
        public void SetCapturable(bool capturable, int attackerOwnerId, bool isHover = false)
        {
            if (_meshRenderer != null && _meshRenderer.material != null)
            {
                if (capturable)
                {
                    _meshRenderer.material.EnableKeyword("_EMISSION");
                    // Use attacker's SECONDARY color for selection, PRIMARY for hover
                    var colorScheme = PlayerColors.Get(attackerOwnerId);
                    Color emissionColor = isHover 
                        ? colorScheme.PrimaryColor * 0.6f 
                        : colorScheme.SecondaryColor * 0.6f;
                    _meshRenderer.material.SetColor("_EmissionColor", emissionColor);
                }
                else if (!_isSelected)
                {
                    // Only disable if not currently selected
                    _meshRenderer.material.DisableKeyword("_EMISSION");
                    _meshRenderer.material.SetColor("_EmissionColor", Color.black);
                }
            }
        }

        public void MoveTo(Vector3 newPosition)
        {
            _startPosition = transform.position;
            // Both render modes use 3D meshes that sit on the board
            bool hasMesh = _meshRenderer != null;
            _targetPosition = hasMesh ? newPosition : newPosition + Vector3.up * 0.5f;
            _isMoving = true;
            _moveProgress = 0f;
            
            // Update original position for future drags
            _originalPosition = _targetPosition;
            
            Debug.Log($"[UnitRenderer] MoveTo: from {_startPosition} to {_targetPosition}");
        }

        private void Update()
        {
            // Handle returning to original position after failed drag
            if (_isReturning)
            {
                UpdateReturn();
                return;
            }
            
            // Handle lift animation during drag
            if (_isDragging)
            {
                UpdateDragLift();
                return;
            }
            
            // Handle normal movement animation
            if (_isMoving)
            {
                _moveProgress += Time.deltaTime * _moveSpeed;
                
                if (_moveProgress >= 1f)
                {
                    transform.position = _targetPosition;
                    _isMoving = false;
                    Debug.Log($"[UnitRenderer] Movement complete at {_targetPosition}");
                }
                else
                {
                    // Lerp from start to target position with bounce
                    Vector3 currentPos = Vector3.Lerp(_startPosition, _targetPosition, _moveProgress);
                    float bounce = Mathf.Sin(_moveProgress * Mathf.PI) * _bounceHeight;
                    currentPos.y += bounce;
                    transform.position = currentPos;
                }
            }
            
            // Forcefield handled by shader animation, no per-frame update needed

            // Animate held item (rotation and subtle bobbing)
            if (_heldItemObject != null)
            {
                _heldItemObject.transform.Rotate(Vector3.up, 90f * Time.deltaTime);
                float bob = Mathf.Sin(Time.time * 3f) * 0.02f;
                var localPos = _heldItemObject.transform.localPosition;
                localPos.y = 0.8f + bob;
                _heldItemObject.transform.localPosition = localPos;
            }
        }

        #endregion
        
        #region Drag and Drop
        
        /// <summary>
        /// Called when the player starts dragging this piece.
        /// </summary>
        public void StartDrag()
        {
            if (_isDragging) return;
            
            _isDragging = true;
            _isMoving = false;
            _isReturning = false;
            _originalPosition = transform.position;
            _originalScale = transform.localScale;
            _currentLiftProgress = 0f;
            
            // Initialize drag rotation state
            _lastDragPosition = transform.position;
            _currentDragYaw = transform.eulerAngles.y;
            _targetDragYaw = _currentDragYaw;
            _currentDragTilt = 0f;
            _hasValidDragDirection = false;
            
            Debug.Log($"[UnitRenderer] StartDrag: Unit {UnitId}");
        }
        
        /// <summary>
        /// Called when the player starts dragging this piece (remote player via network).
        /// </summary>
        public void StartDragRemote()
        {
            if (_isDragging) return;
            
            _isDragging = true;
            _isMoving = false;
            _isReturning = false;
            _originalPosition = transform.position;
            _originalScale = transform.localScale;
            _currentLiftProgress = 0f;
            
            // Initialize drag rotation state
            _lastDragPosition = transform.position;
            _currentDragYaw = transform.eulerAngles.y;
            _targetDragYaw = _currentDragYaw;
            _currentDragTilt = 0f;
            _hasValidDragDirection = false;
        }
        
        /// <summary>
        /// Updates the piece position while being dragged.
        /// </summary>
        public void UpdateDragPosition(Vector3 worldPosition)
        {
            if (!_isDragging) return;
            
            // Apply lifted Y position
            Vector3 dragPos = new Vector3(worldPosition.x, _dragLiftHeight, worldPosition.z);
            
            // Calculate drag direction for rotation
            Vector3 dragDelta = new Vector3(dragPos.x - _lastDragPosition.x, 0f, dragPos.z - _lastDragPosition.z);
            float dragDistance = dragDelta.magnitude;
            
            if (dragDistance > _minDragDistanceForRotation)
            {
                // Calculate the target yaw (Y rotation) to face the drag direction
                _targetDragYaw = Mathf.Atan2(dragDelta.x, dragDelta.z) * Mathf.Rad2Deg;
                _hasValidDragDirection = true;
                _lastDragPosition = dragPos;
            }
            
            transform.position = dragPos;
        }
        
        /// <summary>
        /// Called when the drag ends.
        /// </summary>
        /// <param name="success">If true, the move was valid. If false, return to original position.</param>
        public void EndDrag(bool success, bool stayInPlace = false)
        {
            if (!_isDragging) return;
            
            _isDragging = false;
            
            // Remove tilt but keep the Y rotation (facing direction)
            _currentDragTilt = 0f;
            transform.rotation = Quaternion.Euler(0f, _currentDragYaw, 0f);
            
            if (success && !stayInPlace)
            {
                // Move was successful - scale will be restored via MoveTo animation
                transform.localScale = _originalScale;
            }
            else
            {
                // Move failed OR ranged capture (stay in place) - animate back to original position
                _isReturning = true;
                _startPosition = transform.position;
                _targetPosition = _originalPosition;
                _moveProgress = 0f;
                transform.localScale = _originalScale;
            }
            
            Debug.Log($"[UnitRenderer] EndDrag: Unit {UnitId}, success={success}, stayInPlace={stayInPlace}");
        }
        
        /// <summary>
        /// Called when a remote player's drag ends.
        /// </summary>
        public void EndDragRemote(bool success)
        {
            if (!_isDragging) return;
            
            _isDragging = false;
            
            // Remove tilt but keep the Y rotation (facing direction)
            _currentDragTilt = 0f;
            transform.rotation = Quaternion.Euler(0f, _currentDragYaw, 0f);
            
            if (!success)
            {
                // Animate back
                _isReturning = true;
                _startPosition = transform.position;
                _targetPosition = _originalPosition;
                _moveProgress = 0f;
            }
            
            transform.localScale = _originalScale;
        }
        
        private void UpdateDragLift()
        {
            // Smoothly scale up during drag
            _currentLiftProgress = Mathf.MoveTowards(_currentLiftProgress, 1f, Time.deltaTime * _dragLiftSpeed);
            
            float baseScale = (_renderMode == PieceRenderMode.RevolutionVolume) ? 0.8f : 1f;
            float targetScale = baseScale * _dragScale;
            float currentScale = Mathf.Lerp(baseScale, targetScale, _currentLiftProgress);
            transform.localScale = Vector3.one * currentScale;
            
            // Smoothly rotate to face drag direction
            if (_hasValidDragDirection)
            {
                // Use SmoothDampAngle-like behavior for rotation
                _currentDragYaw = Mathf.LerpAngle(_currentDragYaw, _targetDragYaw, Time.deltaTime * _dragRotationSpeed);
                
                // Add forward tilt while dragging
                _currentDragTilt = Mathf.Lerp(_currentDragTilt, _dragTiltAngle, Time.deltaTime * _dragRotationSpeed);
            }
            
            // Apply rotation: Y rotation (facing) + X rotation (tilt forward)
            transform.rotation = Quaternion.Euler(_currentDragTilt, _currentDragYaw, 0f);
        }
        
        private void UpdateReturn()
        {
            _moveProgress += Time.deltaTime * _returnSpeed;
            
            if (_moveProgress >= 1f)
            {
                transform.position = _targetPosition;
                transform.localScale = _originalScale;
                _isReturning = false;
            }
            else
            {
                // Smooth lerp back with ease-out
                float t = 1f - Mathf.Pow(1f - _moveProgress, 2f);
                transform.position = Vector3.Lerp(_startPosition, _targetPosition, t);
                
                // Also restore scale
                float baseScale = (_renderMode == PieceRenderMode.RevolutionVolume) ? 0.8f : 1f;
                float currentScale = Mathf.Lerp(baseScale * _dragScale, baseScale, t);
                transform.localScale = Vector3.one * currentScale;
            }
            
            // Keep Y rotation (facing direction) consistent during return
            transform.rotation = Quaternion.Euler(0f, _currentDragYaw, 0f);
        }
        
        /// <summary>
        /// Gets whether this unit is currently being dragged.
        /// </summary>
        public bool IsDragging => _isDragging;
        
        #endregion

        /// <summary>
        /// Apply texture and material settings from player color scheme.
        /// </summary>
        private void ApplyTextureToMaterial(Material mat, PlayerColorScheme colorScheme)
        {
            if (colorScheme.PieceTexture != null)
            {
                mat.mainTexture = colorScheme.PieceTexture;
                mat.mainTextureScale = new Vector2(colorScheme.TextureTiling, colorScheme.TextureTiling);
                mat.color = colorScheme.PrimaryColor;
            }
            else
            {
                mat.mainTexture = null;
                mat.color = colorScheme.PrimaryColor;
            }
            
            // Apply smoothness
            if (mat.HasProperty("_Smoothness"))
            {
                mat.SetFloat("_Smoothness", colorScheme.Smoothness);
            }
            else if (mat.HasProperty("_Glossiness"))
            {
                mat.SetFloat("_Glossiness", colorScheme.Smoothness);
            }
        }

        private void SetAlpha(float alpha)
        {
            if (_meshRenderer != null && _meshRenderer.material != null)
            {
                var color = _meshRenderer.material.color;
                color.a = alpha;
                _meshRenderer.material.color = color;
            }
            
            if (_spriteRenderer != null)
            {
                var color = _spriteRenderer.color;
                color.a = alpha;
                _spriteRenderer.color = color;
            }

            if (_unicodeText != null)
            {
                var color = _unicodeText.color;
                color.a = alpha;
                _unicodeText.color = color;
            }
        }

        public void PlayAttackAnimation()
        {
            // Simple scale punch animation
            StartCoroutine(AttackAnimationCoroutine());
        }

        private System.Collections.IEnumerator AttackAnimationCoroutine()
        {
            Vector3 originalScale = Vector3.one;
            Vector3 punchScale = Vector3.one * 1.3f;

            float duration = 0.1f;
            float elapsed = 0f;

            // Scale up
            while (elapsed < duration)
            {
                transform.localScale = Vector3.Lerp(originalScale, punchScale, elapsed / duration);
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Scale back down
            elapsed = 0f;
            while (elapsed < duration)
            {
                transform.localScale = Vector3.Lerp(punchScale, originalScale, elapsed / duration);
                elapsed += Time.deltaTime;
                yield return null;
            }

            transform.localScale = originalScale;
        }

        public void PlayDeathAnimation()
        {
            StartCoroutine(DeathAnimationCoroutine());
        }

        private System.Collections.IEnumerator DeathAnimationCoroutine()
        {
            float duration = 0.5f;
            float elapsed = 0f;
            Vector3 startScale = transform.localScale;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
                SetAlpha(1f - t);
                elapsed += Time.deltaTime;
                yield return null;
            }

            gameObject.SetActive(false);
        }

        #region Forcefield

        /// <summary>
        /// Set the forcefield active state manually (for network sync or testing).
        /// </summary>
        public void SetForcefieldActive(bool active, int ownerId)
        {
            if (active && !_hasForcefield)
            {
                var colorScheme = PlayerColors.Get(ownerId);
                _forcefieldColor = colorScheme.SecondaryColor;
                CreateForcefieldObject();
            }
            else if (!active && _hasForcefield)
            {
                DestroyForcefieldObject();
            }
            
            _hasForcefield = active;
        }

        /// <summary>
        /// Play the animation when the forcefield blocks an attack and is consumed.
        /// </summary>
        public void PlayForcefieldBreakAnimation()
        {
            _hasForcefield = false;
            StartCoroutine(ForcefieldBreakCoroutine());
        }

        private System.Collections.IEnumerator ForcefieldBreakCoroutine()
        {
            if (_forcefieldObject == null || _forcefieldMaterial == null)
            {
                DestroyForcefieldObject();
                yield break;
            }

            float expandDuration = 0.2f;
            float fadeDuration = 0.3f;
            
            Vector3 originalScale = _forcefieldObject.transform.localScale;
            Vector3 expandedScale = originalScale * 1.8f;
            Color originalColor = _forcefieldMaterial.GetColor("_Color");
            
            // Phase 1: Expand and flash bright
            float elapsed = 0f;
            while (elapsed < expandDuration)
            {
                float t = elapsed / expandDuration;
                float easeOut = 1f - (1f - t) * (1f - t); // Ease out quad
                
                _forcefieldObject.transform.localScale = Vector3.Lerp(originalScale, expandedScale, easeOut);
                
                // Flash brighter
                Color flashColor = new Color(
                    Mathf.Min(originalColor.r * 2f, 1f),
                    Mathf.Min(originalColor.g * 2f, 1f),
                    Mathf.Min(originalColor.b * 2f, 1f),
                    originalColor.a * (1f + easeOut)
                );
                _forcefieldMaterial.SetColor("_Color", flashColor);
                
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Phase 2: Fade out while continuing to expand
            elapsed = 0f;
            Vector3 startScale = _forcefieldObject.transform.localScale;
            Vector3 finalScale = expandedScale * 1.3f;
            
            while (elapsed < fadeDuration)
            {
                float t = elapsed / fadeDuration;
                
                _forcefieldObject.transform.localScale = Vector3.Lerp(startScale, finalScale, t);
                
                // Fade alpha to 0
                Color fadeColor = new Color(
                    originalColor.r * 2f,
                    originalColor.g * 2f,
                    originalColor.b * 2f,
                    Mathf.Lerp(originalColor.a * 2f, 0f, t)
                );
                _forcefieldMaterial.SetColor("_Color", fadeColor);
                
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Destroy the forcefield object
            DestroyForcefieldObject();
            
            Debug.Log($"[UnitRenderer] Forcefield break animation complete for unit {UnitId}");
        }

        #endregion

        // Click handling moved to InputHandler (OnMouseDown uses old Input system)
    }
}

