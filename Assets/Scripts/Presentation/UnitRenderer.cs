using UnityEngine;
using TMPro;
using BizarreChess.Core.Units;
using BizarreChess.Core.Player;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Renders a single unit on the board.
    /// Supports Token2D and DisplacementMap rendering modes.
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

            UpdateVisuals();
        }

        public void UpdateState(UnitState state)
        {
            _currentState = state;
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            // Get player colors from centralized PlayerColors
            var colorScheme = PlayerColors.Get(_currentState.OwnerId);
            Color primaryColor = colorScheme.PrimaryColor;
            Color secondaryColor = colorScheme.SecondaryColor;
            Color outlineColor = _currentState.OwnerId == 0 ? Color.black : Color.white;

            // 3D mesh rendering (Token2D or DisplacementMap)
            if (_meshRenderer != null && _meshRenderer.material != null)
            {
                var mat = _meshRenderer.material;
                
                if (_renderMode == PieceRenderMode.DisplacementMap)
                {
                    // For displacement mode, set shader properties for color replacement
                    mat.SetColor("_PrimaryColor", primaryColor);
                    mat.SetColor("_SecondaryColor", secondaryColor);
                }
                else
                {
                    // For Token2D, just set the main color (texture handles the rest)
                    mat.color = primaryColor;
                }
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

            // Update health bar
            if (_healthBar != null && _healthFill != null)
            {
                float healthPercent = (float)_currentState.CurrentHealth / _currentState.MaxHealth;
                _healthFill.localScale = new Vector3(healthPercent, 1, 1);
                _healthBar.SetActive(healthPercent < 1f);
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

            // Visual feedback - scale up slightly when selected
            // Token2D has no scale applied, DisplacementMap has 0.8 scale from generator
            float baseScale = _renderMode == PieceRenderMode.DisplacementMap ? 0.8f : 1f;
            transform.localScale = Vector3.one * baseScale * (selected ? 1.15f : 1f);
            
            // Add emission glow when selected (works for both render modes)
            if (_meshRenderer != null && _meshRenderer.material != null)
            {
                if (selected)
                {
                    _meshRenderer.material.EnableKeyword("_EMISSION");
                    // Use selection color from centralized PlayerColors
                    var colorScheme = PlayerColors.Get(_currentState.OwnerId);
                    Color emissionColor = colorScheme.SelectHighlight * 0.8f;
                    _meshRenderer.material.SetColor("_EmissionColor", emissionColor);
                }
                else
                {
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
        }
        
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
        }
        
        /// <summary>
        /// Updates the piece position while being dragged.
        /// </summary>
        public void UpdateDragPosition(Vector3 worldPosition)
        {
            if (!_isDragging) return;
            
            // Apply lifted Y position
            Vector3 dragPos = new Vector3(worldPosition.x, _dragLiftHeight, worldPosition.z);
            transform.position = dragPos;
        }
        
        /// <summary>
        /// Called when the drag ends.
        /// </summary>
        /// <param name="success">If true, the move was valid. If false, return to original position.</param>
        public void EndDrag(bool success)
        {
            if (!_isDragging) return;
            
            _isDragging = false;
            
            if (success)
            {
                // Move was successful - scale will be restored via MoveTo animation
                transform.localScale = _originalScale;
            }
            else
            {
                // Move failed - animate back to original position
                _isReturning = true;
                _startPosition = transform.position;
                _targetPosition = _originalPosition;
                _moveProgress = 0f;
            }
            
            Debug.Log($"[UnitRenderer] EndDrag: Unit {UnitId}, success={success}");
        }
        
        /// <summary>
        /// Called when a remote player's drag ends.
        /// </summary>
        public void EndDragRemote(bool success)
        {
            if (!_isDragging) return;
            
            _isDragging = false;
            
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
            
            float baseScale = _renderMode == PieceRenderMode.DisplacementMap ? 0.8f : 1f;
            float targetScale = baseScale * _dragScale;
            float currentScale = Mathf.Lerp(baseScale, targetScale, _currentLiftProgress);
            transform.localScale = Vector3.one * currentScale;
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
                float baseScale = _renderMode == PieceRenderMode.DisplacementMap ? 0.8f : 1f;
                float currentScale = Mathf.Lerp(baseScale * _dragScale, baseScale, t);
                transform.localScale = Vector3.one * currentScale;
            }
        }
        
        /// <summary>
        /// Gets whether this unit is currently being dragged.
        /// </summary>
        public bool IsDragging => _isDragging;
        
        #endregion

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

        // Click handling moved to InputHandler (OnMouseDown uses old Input system)
    }
}

