using UnityEngine;
using UnityEngine.EventSystems;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Handles input using raycasting (compatible with new Input System).
    /// Supports both click-to-select and drag-and-drop for moving pieces.
    /// Attach to Main Camera or a dedicated Input GameObject.
    /// </summary>
    public class InputHandler : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private LayerMask _interactableLayers = -1;
        
        [Header("Drag Settings")]
        [SerializeField] private float _dragThreshold = 10f; // Pixels before considering it a drag
        [SerializeField] private float _dragPlaneHeight = 0.5f; // Y height for drag plane

        private BoardRenderer _boardRenderer;
        private GameManager _gameManager;
        
        // Drag state
        private bool _isDragging;
        private bool _isPotentialDrag; // Mouse is down but hasn't moved enough to be a drag
        private UnitRenderer _draggedUnit;
        private Vector2 _dragStartScreenPos;
        private Vector3 _dragStartWorldPos;
        private int _draggedUnitOriginalNode;

        private void Start()
        {
            if (_camera == null)
                _camera = Camera.main;

            _boardRenderer = FindFirstObjectByType<BoardRenderer>();
            _gameManager = FindFirstObjectByType<GameManager>();
        }

        private void Update()
        {
            if (IsPointerOverUI())
                return;
            
            // Handle drag and drop
            if (IsPointerPressedThisFrame())
            {
                HandlePointerDown();
            }
            else if (IsPointerHeld())
            {
                HandlePointerHeld();
            }
            else if (IsPointerReleasedThisFrame())
            {
                HandlePointerUp();
            }
        }

        #region Input Detection

        private bool IsPointerPressedThisFrame()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                return true;

            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
                return true;

            return false;
        }

        private bool IsPointerHeld()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
                return true;

            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
                return true;

            return false;
        }

        private bool IsPointerReleasedThisFrame()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.wasReleasedThisFrame)
                return true;

            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasReleasedThisFrame)
                return true;

            return false;
        }

        private Vector2 GetPointerPosition()
        {
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null)
                return mouse.position.ReadValue();

            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null)
                return touch.primaryTouch.position.ReadValue();

            return Vector2.zero;
        }

        private bool IsPointerOverUI()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        #endregion

        #region Drag and Drop

        private void HandlePointerDown()
        {
            Vector2 pointerPos = GetPointerPosition();
            Ray ray = _camera.ScreenPointToRay(pointerPos);

            if (Physics.Raycast(ray, out RaycastHit hit, 100f, _interactableLayers))
            {
                var unit = hit.collider.GetComponent<UnitRenderer>();
                if (unit != null && CanDragUnit(unit))
                {
                    // Start potential drag
                    _isPotentialDrag = true;
                    _draggedUnit = unit;
                    _dragStartScreenPos = pointerPos;
                    _dragStartWorldPos = unit.transform.position;
                    _draggedUnitOriginalNode = GetUnitCurrentNode(unit);
                    return;
                }
            }
            
            // Not a draggable unit - treat as regular click
            HandleClick();
        }

        private void HandlePointerHeld()
        {
            if (!_isPotentialDrag && !_isDragging) return;
            
            Vector2 pointerPos = GetPointerPosition();
            
            // Check if we've moved enough to start dragging
            if (_isPotentialDrag && !_isDragging)
            {
                float distance = Vector2.Distance(pointerPos, _dragStartScreenPos);
                if (distance >= _dragThreshold)
                {
                    StartDrag();
                }
            }
            
            // Update drag position
            if (_isDragging && _draggedUnit != null)
            {
                Vector3 worldPos = ScreenToWorldOnDragPlane(pointerPos);
                _draggedUnit.UpdateDragPosition(worldPos);
                
                // Notify GameManager for network sync
                _gameManager?.OnUnitDragUpdate(_draggedUnit.UnitId, worldPos);
            }
        }

        private void HandlePointerUp()
        {
            if (_isDragging)
            {
                EndDrag();
            }
            else if (_isPotentialDrag)
            {
                // Was a potential drag but didn't move enough - treat as click
                _isPotentialDrag = false;
                if (_draggedUnit != null)
                {
                    _draggedUnit.OnClicked?.Invoke();
                }
                _draggedUnit = null;
            }
        }

        private void StartDrag()
        {
            _isPotentialDrag = false;
            _isDragging = true;
            
            if (_draggedUnit != null)
            {
                _draggedUnit.StartDrag();
                
                // Notify GameManager to show valid moves and handle selection
                _gameManager?.OnUnitDragStarted(_draggedUnit.UnitId);
            }
        }

        private void EndDrag()
        {
            _isDragging = false;
            _isPotentialDrag = false;
            
            if (_draggedUnit == null) return;
            
            // Find target node under pointer
            Vector2 pointerPos = GetPointerPosition();
            int? targetNode = GetNodeUnderPointer(pointerPos);
            
            // Notify GameManager to handle the drop
            bool success = _gameManager?.OnUnitDragEnded(_draggedUnit.UnitId, targetNode, _draggedUnitOriginalNode) ?? false;
            
            // End drag visual (will animate back if not successful)
            _draggedUnit.EndDrag(success);
            
            _draggedUnit = null;
        }

        private bool CanDragUnit(UnitRenderer unit)
        {
            return _gameManager?.CanDragUnit(unit.UnitId) ?? false;
        }

        private int GetUnitCurrentNode(UnitRenderer unit)
        {
            return _gameManager?.GetUnitCurrentNode(unit.UnitId) ?? -1;
        }

        private int? GetNodeUnderPointer(Vector2 screenPos)
        {
            Ray ray = _camera.ScreenPointToRay(screenPos);
            
            // Use RaycastAll to pass through the dragged piece
            RaycastHit[] hits = Physics.RaycastAll(ray, 100f, _interactableLayers);
            
            // Sort by distance
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            
            foreach (var hit in hits)
            {
                // Skip the dragged unit
                var unit = hit.collider.GetComponent<UnitRenderer>();
                if (unit != null && unit == _draggedUnit)
                    continue;
                
                // If we hit another unit, get the node it's standing on (for captures)
                if (unit != null)
                {
                    int nodeId = _gameManager?.GetUnitCurrentNode(unit.UnitId) ?? -1;
                    if (nodeId >= 0)
                        return nodeId;
                }
                
                // Check tile
                var tile = hit.collider.GetComponent<TileRenderer>();
                if (tile != null)
                    return tile.NodeId;
                
                // Check placeholder tile
                var clickHandler = hit.collider.GetComponent<TileClickHandler>();
                if (clickHandler != null)
                    return clickHandler.TileId;
            }
            
            return null;
        }

        private Vector3 ScreenToWorldOnDragPlane(Vector2 screenPos)
        {
            Ray ray = _camera.ScreenPointToRay(screenPos);
            Plane dragPlane = new Plane(Vector3.up, new Vector3(0, _dragPlaneHeight, 0));
            
            if (dragPlane.Raycast(ray, out float distance))
            {
                return ray.GetPoint(distance);
            }
            
            return Vector3.zero;
        }

        #endregion

        #region Click Handling (fallback for tiles and non-draggable units)

        private void HandleClick()
        {
            Vector2 pointerPos = GetPointerPosition();
            Ray ray = _camera.ScreenPointToRay(pointerPos);

            if (Physics.Raycast(ray, out RaycastHit hit, 100f, _interactableLayers))
            {
                // Check if we hit a tile
                var tile = hit.collider.GetComponent<TileRenderer>();
                if (tile != null)
                {
                    _boardRenderer?.OnTileClicked?.Invoke(tile.NodeId);
                    return;
                }

                // Check if we hit a unit (non-draggable - enemy unit or not our turn)
                var unit = hit.collider.GetComponent<UnitRenderer>();
                if (unit != null)
                {
                    unit.OnClicked?.Invoke();
                    return;
                }

                // Check for TileClickHandler (placeholder tiles)
                var clickHandler = hit.collider.GetComponent<TileClickHandler>();
                if (clickHandler != null)
                {
                    _boardRenderer?.OnTileClicked?.Invoke(clickHandler.TileId);
                    return;
                }
            }
        }

        #endregion
    }
}

