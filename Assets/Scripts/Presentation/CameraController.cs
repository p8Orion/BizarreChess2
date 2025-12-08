using UnityEngine;
using UnityEngine.InputSystem;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Controls camera rotation around the board center using right-click drag.
    /// </summary>
    public class CameraController : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Vector3 _targetPoint = new Vector3(3.5f, 0f, 3.5f);
        
        [Header("Rotation Settings")]
        [SerializeField] private float _rotationSpeed = 3f;
        [SerializeField] private float _minVerticalAngle = 20f;
        [SerializeField] private float _maxVerticalAngle = 85f;
        
        [Header("Zoom Settings")]
        [SerializeField] private float _zoomSpeed = 5f;
        [SerializeField] private float _minDistance = 5f;
        [SerializeField] private float _maxDistance = 25f;
        
        [Header("Smoothing")]
        [SerializeField] private float _smoothTime = 0.1f;

        private float _currentYaw;
        private float _currentPitch;
        private float _targetYaw;
        private float _targetPitch;
        private float _currentDistance;
        private float _targetDistance;
        
        private float _yawVelocity;
        private float _pitchVelocity;
        private float _distanceVelocity;
        
        private Vector2 _lastMousePosition;
        private bool _isDragging;

        /// <summary>
        /// Automatically adds CameraController to the main camera when the game starts.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoAttachToCamera()
        {
            var mainCam = Camera.main;
            if (mainCam != null && mainCam.GetComponent<CameraController>() == null)
            {
                mainCam.gameObject.AddComponent<CameraController>();
                Debug.Log("[CameraController] Auto-attached to Main Camera");
            }
        }

        private void Start()
        {
            InitializeFromCurrentPosition();
        }

        private void InitializeFromCurrentPosition()
        {
            // Calculate current orbital position relative to target
            Vector3 offset = transform.position - _targetPoint;
            _currentDistance = offset.magnitude;
            _targetDistance = _currentDistance;
            
            // Calculate current angles
            _currentYaw = Mathf.Atan2(offset.x, offset.z) * Mathf.Rad2Deg;
            _currentPitch = Mathf.Asin(offset.y / _currentDistance) * Mathf.Rad2Deg;
            
            _targetYaw = _currentYaw;
            _targetPitch = _currentPitch;
        }

        private void Update()
        {
            HandleInput();
            UpdateCameraPosition();
        }

        private void HandleInput()
        {
            var mouse = Mouse.current;
            if (mouse == null) return;

            Vector2 mousePos = mouse.position.ReadValue();
            
            if (mouse.rightButton.wasPressedThisFrame)
            {
                _isDragging = true;
                _lastMousePosition = mousePos;
            }
            else if (mouse.rightButton.wasReleasedThisFrame)
            {
                _isDragging = false;
            }

            if (_isDragging && mouse.rightButton.isPressed)
            {
                Vector2 delta = mousePos - _lastMousePosition;
                _lastMousePosition = mousePos;

                // Horizontal movement rotates around Y axis (yaw)
                _targetYaw += delta.x * _rotationSpeed * 0.1f;
                
                // Vertical movement changes pitch (limited)
                _targetPitch -= delta.y * _rotationSpeed * 0.1f;
                _targetPitch = Mathf.Clamp(_targetPitch, _minVerticalAngle, _maxVerticalAngle);
            }
            
            // Zoom with scroll wheel
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _targetDistance -= scroll * _zoomSpeed * 0.1f;
                _targetDistance = Mathf.Clamp(_targetDistance, _minDistance, _maxDistance);
            }
        }

        private void UpdateCameraPosition()
        {
            // Smooth the rotation and zoom
            _currentYaw = Mathf.SmoothDamp(_currentYaw, _targetYaw, ref _yawVelocity, _smoothTime);
            _currentPitch = Mathf.SmoothDamp(_currentPitch, _targetPitch, ref _pitchVelocity, _smoothTime);
            _currentDistance = Mathf.SmoothDamp(_currentDistance, _targetDistance, ref _distanceVelocity, _smoothTime);

            // Convert spherical coordinates to cartesian
            float yawRad = _currentYaw * Mathf.Deg2Rad;
            float pitchRad = _currentPitch * Mathf.Deg2Rad;
            
            Vector3 offset = new Vector3(
                Mathf.Sin(yawRad) * Mathf.Cos(pitchRad),
                Mathf.Sin(pitchRad),
                Mathf.Cos(yawRad) * Mathf.Cos(pitchRad)
            ) * _currentDistance;

            transform.position = _targetPoint + offset;
            transform.LookAt(_targetPoint);
        }

        /// <summary>
        /// Sets the target point for the camera to orbit around.
        /// </summary>
        public void SetTargetPoint(Vector3 target)
        {
            _targetPoint = target;
            InitializeFromCurrentPosition();
        }

        /// <summary>
        /// Resets the camera to the initial view (looking from the south).
        /// </summary>
        public void ResetView()
        {
            _targetYaw = 0f;
            _targetPitch = 60f;
            _targetDistance = 14f;
        }
        
        /// <summary>
        /// Sets the camera view based on the player's side.
        /// Player 0 (white/first) views from south, seeing their pieces at bottom.
        /// Player 1 (black/second) views from north, seeing their pieces at bottom.
        /// </summary>
        /// <param name="playerId">The local player's ID (0 or 1)</param>
        /// <param name="instant">If true, snaps instantly without smooth transition</param>
        public void SetPlayerView(int playerId, bool instant = false)
        {
            // Player 0 (white): yaw = 180 (camera in -Z, looking toward +Z, pieces in low Z appear at bottom)
            // Player 1 (black): yaw = 0 (camera in +Z, looking toward -Z, pieces in high Z appear at bottom)
            float targetYaw = playerId == 0 ? 180f : 0f;
            
            _targetYaw = targetYaw;
            _targetPitch = 60f;
            _targetDistance = 14f;
            
            if (instant)
            {
                _currentYaw = _targetYaw;
                _currentPitch = _targetPitch;
                _currentDistance = _targetDistance;
                UpdateCameraPosition();
            }
            
            Debug.Log($"[CameraController] Set view for player {playerId} (yaw: {targetYaw})");
        }
    }
}

