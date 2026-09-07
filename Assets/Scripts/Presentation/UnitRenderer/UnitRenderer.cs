using UnityEngine;
using TMPro;
using BizarreChess.Core.Units;
using BizarreChess.Core.Player;
using BizarreChess.Core.Skills;
using BizarreChess.Core.Items;
using BizarreChess.Presentation;

namespace BizarreChess.Presentation.UnitRenderer
{
    /// <summary>
    /// Renders a single unit on the board.
    /// Supports Token2D, RevolutionVolume, and ImportedMesh rendering modes.
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
        [SerializeField] private float _meleeStopShort = 0.5f;
        
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
        [SerializeField] private float _forcefieldScale = 1.12f;
        [SerializeField] private float _forcefieldVerticalOffset = 0.22f;

        public int UnitId { get; private set; }
        public System.Action OnClicked;

        private UnitState _currentState;
        private UnitDefinition _definition;
        private Vector3 _startPosition;
        private Vector3 _targetPosition;
        private Vector3 _segmentEnd;
        private bool _isMoving;
        private bool _isSelected;
        private float _moveProgress;
        private float _segmentDuration;
        private enum MeleePhase { None, Approach, Attack, Finish }
        private MeleePhase _meleePhase;
        private System.Action _onMeleeStrike;
        private bool _meleeStrikeFired;
        private bool _meleeUsingPunch;
        private float _meleePunchEndTime;
        private System.Action _onAttackComplete;
        private bool _awaitingAttackComplete;
        private bool _awaitingImportedAttack;
        private bool _isDying;
        private PieceRenderMode _renderMode;
        private Renderer[] _pieceRenderers;
        private Color[][] _importedOriginalColors;
        private ImportedPieceClipPlayer _importedClips;
        private const float ImportedPlayerBlend = 0.5f;
        
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
            _currentDragYaw = transform.eulerAngles.y;

            // Auto-find components if not assigned (for dynamically created units)
            CachePieceRenderers();
            if (_unicodeText == null)
                _unicodeText = GetComponentInChildren<TextMeshPro>();
            if (_spriteRenderer == null)
                _spriteRenderer = GetComponent<SpriteRenderer>();

            // Both render modes use 3D meshes, position them on the board
            bool hasMesh = _meshRenderer != null || _renderMode == PieceRenderMode.ImportedMesh;
            if (hasMesh)
            {
                transform.position = position; // 3D pieces sit on the board
            }
            else
            {
                transform.position = position + Vector3.up * 0.5f; // 2D fallback hovers above
            }

            transform.localScale = Vector3.one * GetVisualScale();

            if (_renderMode == PieceRenderMode.ImportedMesh)
            {
                ImportedMeshGenerator.SnapVisualToGround(transform, position.y);
                _importedClips = ImportedPieceClipPlayer.TryCreate(transform, definition.ImportedModel);
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
                
                Color shaderColor = new Color(_forcefieldColor.r, _forcefieldColor.g, _forcefieldColor.b, 0.22f);
                _forcefieldMaterial.SetColor("_Color", shaderColor);
                
                // Tighter rim, less filled-in cloud
                _forcefieldMaterial.SetFloat("_FresnelPower", 3.2f);
                _forcefieldMaterial.SetFloat("_NoiseScale", 6.0f);
                _forcefieldMaterial.SetFloat("_NoiseSpeed", 0.25f);
                _forcefieldMaterial.SetFloat("_PulseIntensity", 0.2f);
                _forcefieldMaterial.SetFloat("_EdgeGlow", 0.65f);
                
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
            Shader fallbackShader = Shader.Find("Standard")
                ?? Shader.Find("Universal Render Pipeline/Lit");
            
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
                
                Color forcefieldColorWithAlpha = new Color(_forcefieldColor.r, _forcefieldColor.g, _forcefieldColor.b, 0.18f);
                _forcefieldMaterial.color = forcefieldColorWithAlpha;
                
                _forcefieldMaterial.EnableKeyword("_EMISSION");
                _forcefieldMaterial.SetColor("_EmissionColor", _forcefieldColor * 0.22f);
                
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

            // 3D mesh rendering (Token2D, RevolutionVolume, or ImportedMesh)
            bool hasPieceMesh = false;
            if (_renderMode == PieceRenderMode.ImportedMesh)
            {
                hasPieceMesh = ApplyImportedPlayerBlend(colorScheme.PrimaryColor);
            }
            else
            {
                ForEachPieceRenderer(renderer =>
                {
                    if (renderer.material == null)
                        return;
                    ApplyTextureToMaterial(renderer.material, colorScheme);
                    hasPieceMesh = true;
                });
            }

            // 2D Unicode text fallback (when no mesh)
            if (!hasPieceMesh && _unicodeText != null)
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
            // Token2D has no scale applied, 3D volumes use 0.8 from their generators
            transform.localScale = Vector3.one * GetVisualScale();
            
            // Add emission glow when selected (works for all mesh render modes)
            if (selected)
            {
                var colorScheme = PlayerColors.Get(_currentState.OwnerId);
                SetPieceEmission(colorScheme.SecondaryColor * 0.8f);
            }
            else
            {
                ClearPieceEmission();
            }
        }

        /// <summary>
        /// Mark this unit as a capturable target (enemy that can be captured).
        /// </summary>
        public void SetCapturable(bool capturable, int attackerOwnerId, bool isHover = false)
        {
            if (capturable)
            {
                var colorScheme = PlayerColors.Get(attackerOwnerId);
                Color emissionColor = isHover
                    ? colorScheme.PrimaryColor * 0.6f
                    : colorScheme.SecondaryColor * 0.6f;
                SetPieceEmission(emissionColor);
            }
            else if (!_isSelected)
            {
                ClearPieceEmission();
            }
        }

        public void MoveTo(Vector3 newPosition, bool playAttack = false, System.Action onMeleeStrike = null)
        {
            _startPosition = transform.position;
            bool hasMesh = _meshRenderer != null || _renderMode == PieceRenderMode.ImportedMesh;
            _targetPosition = hasMesh ? newPosition : newPosition + Vector3.up * 0.5f;
            _originalPosition = _targetPosition;
            _isMoving = true;
            _onMeleeStrike = onMeleeStrike;
            _meleeStrikeFired = false;
            _meleeUsingPunch = false;

            if (playAttack)
            {
                Vector3 approach = GetApproachPosition(_startPosition, _targetPosition, _meleeStopShort);
                _meleePhase = MeleePhase.Approach;
                BeginTravelSegment(_startPosition, approach);
                _importedClips?.PlayMove();
            }
            else
            {
                _meleePhase = MeleePhase.None;
                BeginTravelSegment(_startPosition, _targetPosition);
                _importedClips?.PlayMove();
            }

            Debug.Log($"[UnitRenderer] MoveTo: from {_startPosition} to {_targetPosition}, attack={playAttack}");
        }

        public void SetOnMeleeStrike(System.Action callback)
        {
            if (_meleeStrikeFired)
            {
                callback?.Invoke();
                return;
            }

            _onMeleeStrike = callback;
        }

        private void BeginTravelSegment(Vector3 from, Vector3 to)
        {
            _startPosition = from;
            _segmentEnd = to;
            _moveProgress = 0f;
            float dist = Vector3.Distance(
                new Vector3(from.x, 0f, from.z),
                new Vector3(to.x, 0f, to.z));
            _segmentDuration = _meleePhase == MeleePhase.None
                ? 1f / Mathf.Max(_moveSpeed, 0.01f)
                : Mathf.Max(dist / Mathf.Max(_moveSpeed, 0.01f), 0.04f);
        }

        private static Vector3 GetApproachPosition(Vector3 start, Vector3 target, float stopShort)
        {
            Vector3 delta = target - start;
            float dist = new Vector3(delta.x, 0f, delta.z).magnitude;
            if (dist <= 0.001f)
                return start;

            float stop = Mathf.Min(Mathf.Max(stopShort, 0f), dist * 0.5f);
            float t = (dist - stop) / dist;
            return Vector3.Lerp(start, target, t);
        }

        private void UpdateTravel()
        {
            if (_meleePhase == MeleePhase.Attack)
                return;

            _moveProgress += Time.deltaTime / Mathf.Max(_segmentDuration, 0.0001f);
            if (_moveProgress >= 1f)
            {
                transform.position = _segmentEnd;
                if (_meleePhase == MeleePhase.Approach)
                    BeginMeleeAttack();
                else
                    CompleteTravel();
                return;
            }

            Vector3 currentPos = Vector3.Lerp(_startPosition, _segmentEnd, _moveProgress);
            if (_importedClips == null || !_importedClips.IsPlaying)
                currentPos.y += Mathf.Sin(_moveProgress * Mathf.PI) * _bounceHeight;
            transform.position = currentPos;
        }

        private void BeginMeleeAttack()
        {
            _meleePhase = MeleePhase.Attack;

            if (_importedClips != null && _importedClips.HasAttackClip)
            {
                _importedClips.PlayAttack();
                return;
            }

            _meleeUsingPunch = true;
            _meleePunchEndTime = Time.time + 0.2f;
            StartCoroutine(AttackAnimationCoroutine());
        }

        private bool IsMeleeAttackFinished()
        {
            if (_meleeUsingPunch)
                return Time.time >= _meleePunchEndTime;

            return _importedClips == null || !_importedClips.IsPlaying;
        }

        private void BeginMeleeFinish()
        {
            FireMeleeStrike();
            _meleePhase = MeleePhase.Finish;
            _meleeUsingPunch = false;
            BeginTravelSegment(transform.position, _targetPosition);
            _importedClips?.PlayMove();
        }

        private void CompleteTravel()
        {
            transform.position = _targetPosition;
            _isMoving = false;
            _meleePhase = MeleePhase.None;
            _importedClips?.NotifyTravelFinished();
            Debug.Log($"[UnitRenderer] Movement complete at {_targetPosition}");
        }

        private void FireMeleeStrike()
        {
            if (_meleeStrikeFired)
                return;

            _meleeStrikeFired = true;
            _onMeleeStrike?.Invoke();
            _onMeleeStrike = null;
        }

        private void Update()
        {
            if (_isDying)
                return;

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
            
            if (_isMoving)
                UpdateTravel();

            _importedClips?.Tick(Time.deltaTime);

            if (_isMoving && _meleePhase == MeleePhase.Attack && IsMeleeAttackFinished())
                BeginMeleeFinish();

            if (_awaitingImportedAttack && (_importedClips == null || !_importedClips.IsPlaying))
                FinishAttackComplete();
            
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
            _meleePhase = MeleePhase.None;
            _onMeleeStrike = null;
            _importedClips?.Stop();
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
            _meleePhase = MeleePhase.None;
            _onMeleeStrike = null;
            _importedClips?.Stop();
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
            
            float baseScale = GetVisualScale();
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
                float baseScale = GetVisualScale();
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

        private float GetVisualScale()
        {
            return _renderMode == PieceRenderMode.Token2D ? 1f : 0.8f;
        }

        private void CachePieceRenderers()
        {
            if (_meshRenderer == null)
                _meshRenderer = GetComponent<MeshRenderer>() ?? GetComponentInChildren<MeshRenderer>();

            var renderers = GetComponentsInChildren<Renderer>(true);
            int count = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (IsPieceRenderer(renderers[i]))
                    count++;
            }

            _pieceRenderers = new Renderer[count];
            int write = 0;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!IsPieceRenderer(renderers[i]))
                    continue;
                _pieceRenderers[write++] = renderers[i];
            }

            CacheImportedOriginalColors();
        }

        private static bool IsPieceRenderer(Renderer renderer)
        {
            return renderer != null
                && renderer is not ParticleSystemRenderer
                && renderer is not SpriteRenderer
                && renderer is not LineRenderer;
        }

        private void CacheImportedOriginalColors()
        {
            _importedOriginalColors = null;
            if (_renderMode != PieceRenderMode.ImportedMesh || _pieceRenderers == null)
                return;

            _importedOriginalColors = new Color[_pieceRenderers.Length][];
            for (int i = 0; i < _pieceRenderers.Length; i++)
            {
                var renderer = _pieceRenderers[i];
                if (renderer == null)
                {
                    _importedOriginalColors[i] = System.Array.Empty<Color>();
                    continue;
                }

                var mats = renderer.materials;
                var colors = new Color[mats.Length];
                for (int j = 0; j < mats.Length; j++)
                    colors[j] = GetMaterialColor(mats[j]);
                _importedOriginalColors[i] = colors;
            }
        }

        private bool ApplyImportedPlayerBlend(Color playerColor)
        {
            if (_pieceRenderers == null || _importedOriginalColors == null)
                return false;

            bool hasMesh = false;
            for (int i = 0; i < _pieceRenderers.Length; i++)
            {
                var renderer = _pieceRenderers[i];
                if (renderer == null || renderer == _forcefieldRenderer || renderer == _heldItemRenderer)
                    continue;

                var mats = renderer.materials;
                var originals = i < _importedOriginalColors.Length ? _importedOriginalColors[i] : null;
                if (originals == null)
                    continue;

                for (int j = 0; j < mats.Length && j < originals.Length; j++)
                {
                    var mat = mats[j];
                    if (mat == null)
                        continue;

                    hasMesh = true;
                    if (IsGoldMaterial(mat, originals[j]))
                    {
                        SetMaterialColor(mat, originals[j]);
                        continue;
                    }

                    Color blended = Color.Lerp(originals[j], playerColor, ImportedPlayerBlend);
                    blended.a = originals[j].a;
                    SetMaterialColor(mat, blended);
                }
            }

            return hasMesh;
        }

        private static bool IsGoldMaterial(Material mat, Color color)
        {
            if (mat != null)
            {
                string name = mat.name.ToLowerInvariant();
                if (name.Contains("gold") || name.Contains("dorado") || name.Contains("gilt")
                    || name.Contains("brass") || name.Contains("oro"))
                    return true;
            }

            Color.RGBToHSV(color, out float h, out float s, out float v);
            bool goldHue = h >= 0.07f && h <= 0.18f && s >= 0.3f && v >= 0.4f;
            if (goldHue)
                return true;

            float metallic = 0f;
            if (mat != null && mat.HasProperty("_Metallic"))
                metallic = mat.GetFloat("_Metallic");

            return metallic > 0.5f && h >= 0.06f && h <= 0.20f && v >= 0.35f;
        }

        private static Color GetMaterialColor(Material mat)
        {
            if (mat == null)
                return Color.white;
            if (mat.HasProperty("_BaseColor"))
                return mat.GetColor("_BaseColor");
            if (mat.HasProperty("_Color"))
                return mat.GetColor("_Color");
            return mat.color;
        }

        private static void SetMaterialColor(Material mat, Color color)
        {
            if (mat == null)
                return;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            mat.color = color;
        }

        private void ForEachPieceRenderer(System.Action<Renderer> action)
        {
            if (_pieceRenderers == null)
                return;

            for (int i = 0; i < _pieceRenderers.Length; i++)
            {
                var renderer = _pieceRenderers[i];
                if (renderer == null || renderer == _forcefieldRenderer || renderer == _heldItemRenderer)
                    continue;
                action(renderer);
            }
        }

        private void SetPieceEmission(Color emissionColor)
        {
            ForEachPieceRenderer(renderer =>
            {
                if (renderer.material == null) return;
                renderer.material.EnableKeyword("_EMISSION");
                renderer.material.SetColor("_EmissionColor", emissionColor);
            });
        }

        private void ClearPieceEmission()
        {
            ForEachPieceRenderer(renderer =>
            {
                if (renderer.material == null) return;
                renderer.material.DisableKeyword("_EMISSION");
                renderer.material.SetColor("_EmissionColor", Color.black);
            });
        }

        private void SetAlpha(float alpha)
        {
            ForEachPieceRenderer(renderer =>
            {
                if (renderer.material == null) return;
                var color = renderer.material.color;
                color.a = alpha;
                renderer.material.color = color;
            });
            
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

        public void PlayAttackAnimation(System.Action onComplete = null)
        {
            _onAttackComplete = onComplete;
            _awaitingAttackComplete = true;

            if (_importedClips != null && _importedClips.HasAttackClip)
            {
                _awaitingImportedAttack = true;
                _importedClips.PlayAttack();
                return;
            }

            _awaitingImportedAttack = false;
            StartCoroutine(AttackThenComplete());
        }

        public void SetOnAttackComplete(System.Action callback)
        {
            if (!_awaitingAttackComplete)
            {
                callback?.Invoke();
                return;
            }

            _onAttackComplete = callback;
        }

        private System.Collections.IEnumerator AttackThenComplete()
        {
            yield return AttackAnimationCoroutine();
            FinishAttackComplete();
        }

        private void FinishAttackComplete()
        {
            if (!_awaitingAttackComplete)
                return;

            _awaitingAttackComplete = false;
            _awaitingImportedAttack = false;
            var callback = _onAttackComplete;
            _onAttackComplete = null;
            callback?.Invoke();
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
            if (_isDying)
                return;

            _isDying = true;
            _isMoving = false;
            _meleePhase = MeleePhase.None;
            _importedClips?.Stop();

            var collider = GetComponent<Collider>();
            if (collider != null)
                collider.enabled = false;

            StartCoroutine(DeathAnimationCoroutine());
        }

        private System.Collections.IEnumerator DeathAnimationCoroutine()
        {
            Vector3 startPos = transform.position;
            Quaternion startRot = transform.rotation;

            Vector3 facing = transform.forward;
            facing.y = 0f;
            if (facing.sqrMagnitude < 0.0001f)
                facing = Vector3.forward;
            facing.Normalize();

            Vector3 fallDir = -facing;
            Vector3 right = Vector3.Cross(Vector3.up, facing);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            right.Normalize();

            float side = Random.value < 0.5f ? -1f : 1f;
            float rollDeg = Random.Range(28f, 78f);
            float height = EstimateVisualHeight();

            Quaternion afterTip = Quaternion.AngleAxis(86f, right) * startRot;
            Quaternion endRot = Quaternion.AngleAxis(rollDeg * side, fallDir) * afterTip;

            Vector3 liePos = startPos + fallDir * (height * 0.42f);
            liePos.y = startPos.y + 0.02f;
            Vector3 endPos = liePos + right * side * Random.Range(0.14f, 0.34f);

            float fallDur = 0.38f;
            float rollDur = 0.34f;
            float fadeDur = 0.42f;

            float elapsed = 0f;
            while (elapsed < fallDur)
            {
                float u = elapsed / fallDur;
                float ease = u * u;
                transform.rotation = Quaternion.Slerp(startRot, afterTip, ease);
                transform.position = Vector3.Lerp(startPos, liePos, ease);
                elapsed += Time.deltaTime;
                yield return null;
            }

            transform.rotation = afterTip;
            transform.position = liePos;

            elapsed = 0f;
            while (elapsed < rollDur)
            {
                float u = Mathf.SmoothStep(0f, 1f, elapsed / rollDur);
                transform.rotation = Quaternion.Slerp(afterTip, endRot, u);
                transform.position = Vector3.Lerp(liePos, endPos, u);
                elapsed += Time.deltaTime;
                yield return null;
            }

            transform.rotation = endRot;
            transform.position = endPos;

            elapsed = 0f;
            while (elapsed < fadeDur)
            {
                SetAlpha(1f - elapsed / fadeDur);
                elapsed += Time.deltaTime;
                yield return null;
            }

            gameObject.SetActive(false);
        }

        private float EstimateVisualHeight()
        {
            if (ImportedMeshGenerator.TryGetVisualWorldBounds(transform, out Bounds bounds) && bounds.size.y > 0.15f)
                return bounds.size.y;

            var box = GetComponent<BoxCollider>();
            if (box != null)
                return Mathf.Max(box.size.y * transform.lossyScale.y, 0.35f);

            return 0.7f;
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

