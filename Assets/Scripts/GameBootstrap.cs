using UnityEngine;
using Unity.Netcode;
using BizarreChess.Core.Factories;
using BizarreChess.Networking;
using BizarreChess.Presentation;

namespace BizarreChess
{
    /// <summary>
    /// Bootstraps the game scene with all required components.
    /// Attach this to an empty GameObject in your scene.
    /// </summary>
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Scene Setup")]
        [SerializeField] private bool _autoCreateComponents = true;
        
        [Header("Camera")]
        [SerializeField] private float _cameraHeight = 12f;
        [SerializeField] private float _cameraAngle = 60f;

        [Header("Board Visual")]
        [SerializeField] private Material _lightTileMaterial;
        [SerializeField] private Material _darkTileMaterial;

        private void Awake()
        {
            if (_autoCreateComponents)
            {
                SetupScene();
            }
        }

        [ContextMenu("Setup Scene")]
        public void SetupScene()
        {
            SetupCamera();
            SetupLighting();
            SetupNetworking();
            SetupGameManager();
            SetupUI();

            Debug.Log("[GameBootstrap] Scene setup complete!");
        }

        private void SetupCamera()
        {
            var mainCam = Camera.main;
            if (mainCam == null)
            {
                var camGO = new GameObject("Main Camera");
                mainCam = camGO.AddComponent<Camera>();
                camGO.AddComponent<AudioListener>();
                camGO.tag = "MainCamera";
            }

            // Position camera to view 8x8 board centered at origin
            float boardCenter = 3.5f; // Center of 0-7 grid
            mainCam.transform.position = new Vector3(boardCenter, _cameraHeight, boardCenter - 5f);
            mainCam.transform.rotation = Quaternion.Euler(_cameraAngle, 0, 0);
            mainCam.orthographic = false;
            mainCam.fieldOfView = 60f;
            mainCam.backgroundColor = new Color(0.15f, 0.15f, 0.2f);
        }

        private void SetupLighting()
        {
            var existingLight = FindFirstObjectByType<Light>();
            if (existingLight == null)
            {
                var lightGO = new GameObject("Directional Light");
                var light = lightGO.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1f;
                light.color = Color.white;
                lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);
            }
        }

        private void SetupNetworking()
        {
            // Find or create NetworkManager
            var networkManager = FindFirstObjectByType<NetworkManager>();
            if (networkManager == null)
            {
                var netGO = new GameObject("NetworkManager");
                networkManager = netGO.AddComponent<NetworkManager>();
                
                // Add Unity Transport
                var transport = netGO.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                networkManager.NetworkConfig = new NetworkConfig();
                
                // Set transport
                typeof(NetworkManager)
                    .GetProperty("NetworkConfig")
                    .GetValue(networkManager);
            }

            // Find or create GameNetworkManager
            var gameNetManager = FindFirstObjectByType<GameNetworkManager>();
            if (gameNetManager == null)
            {
                var existingNetGO = networkManager.gameObject;
                gameNetManager = existingNetGO.AddComponent<GameNetworkManager>();
            }

            // Find or create NetworkedGameState
            var networkedState = FindFirstObjectByType<NetworkedGameState>();
            if (networkedState == null)
            {
                var stateGO = new GameObject("NetworkedGameState");
                networkedState = stateGO.AddComponent<NetworkedGameState>();
                
                // Register as network prefab (needs to be done via NetworkManager)
                var netObj = stateGO.AddComponent<NetworkObject>();
            }
        }

        private void SetupGameManager()
        {
            var gameManager = FindFirstObjectByType<GameManager>();
            if (gameManager == null)
            {
                var gmGO = new GameObject("GameManager");
                gameManager = gmGO.AddComponent<GameManager>();
            }

            // Setup board renderer
            var boardRenderer = FindFirstObjectByType<BoardRenderer>();
            if (boardRenderer == null)
            {
                var boardGO = new GameObject("BoardRenderer");
                boardRenderer = boardGO.AddComponent<BoardRenderer>();
                boardGO.transform.position = Vector3.zero;
            }

            // Setup units container
            var unitsContainer = GameObject.Find("UnitsContainer");
            if (unitsContainer == null)
            {
                unitsContainer = new GameObject("UnitsContainer");
                unitsContainer.transform.position = Vector3.zero;
            }
        }

        private void SetupUI()
        {
            // Find or create UI Canvas
            var canvas = FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGO = new GameObject("UI Canvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }

            // Add GameUI if not present
            var gameUI = FindFirstObjectByType<GameUI>();
            if (gameUI == null)
            {
                gameUI = canvas.gameObject.AddComponent<GameUI>();
            }
        }

        /// <summary>
        /// Creates all required ScriptableObject data assets at runtime.
        /// </summary>
        public static ClassicChessSetup CreateGameData()
        {
            return ClassicChessFactory.CreateCompleteSetup();
        }
    }
}

