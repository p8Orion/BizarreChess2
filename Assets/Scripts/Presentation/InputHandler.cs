using UnityEngine;
using UnityEngine.EventSystems;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Handles input using raycasting (compatible with new Input System).
    /// Attach to Main Camera or a dedicated Input GameObject.
    /// </summary>
    public class InputHandler : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private LayerMask _interactableLayers = -1;

        private BoardRenderer _boardRenderer;
        private GameManager _gameManager;

        private void Start()
        {
            if (_camera == null)
                _camera = Camera.main;

            _boardRenderer = FindFirstObjectByType<BoardRenderer>();
            _gameManager = FindFirstObjectByType<GameManager>();
        }

        private void Update()
        {
            // Check for click/tap
            if (IsClickThisFrame() && !IsPointerOverUI())
            {
                HandleClick();
            }
        }

        private bool IsClickThisFrame()
        {
            // New Input System only
            var mouse = UnityEngine.InputSystem.Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                return true;

            var touch = UnityEngine.InputSystem.Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
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

                // Check if we hit a unit
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
    }
}

