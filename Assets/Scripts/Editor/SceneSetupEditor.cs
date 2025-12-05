using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using BizarreChess.Presentation;
using BizarreChess.Networking;

namespace BizarreChess.Editor
{
    /// <summary>
    /// Editor tools for setting up Bizarre Chess scenes.
    /// </summary>
    public static class SceneSetupEditor
    {
        [MenuItem("Bizarre Chess/Setup Current Scene")]
        public static void SetupCurrentScene()
        {
            SetupCamera();
            SetupLighting();
            SetupNetworking();
            SetupGameObjects();
            SetupUI();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            Debug.Log("[Bizarre Chess] Scene setup complete! Save the scene.");
        }

        [MenuItem("Bizarre Chess/Create New Game Scene")]
        public static void CreateNewGameScene()
        {
            // Create new scene
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            
            SetupCurrentScene();
            
            // Save scene
            string path = EditorUtility.SaveFilePanelInProject(
                "Save Game Scene",
                "BizarreChess",
                "unity",
                "Save the new game scene"
            );

            if (!string.IsNullOrEmpty(path))
            {
                EditorSceneManager.SaveScene(scene, path);
            }
        }

        private static void SetupCamera()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var camGO = new GameObject("Main Camera");
                cam = camGO.AddComponent<Camera>();
                camGO.AddComponent<AudioListener>();
                camGO.tag = "MainCamera";
            }

            // Position to view 8x8 board
            cam.transform.position = new Vector3(3.5f, 12f, -2f);
            cam.transform.rotation = Quaternion.Euler(60f, 0f, 0f);
            cam.fieldOfView = 60f;
            cam.backgroundColor = new Color(0.15f, 0.15f, 0.2f);
            cam.clearFlags = CameraClearFlags.SolidColor;
        }

        private static void SetupLighting()
        {
            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            if (lights.Length == 0)
            {
                var lightGO = new GameObject("Directional Light");
                var light = lightGO.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1f;
                light.shadows = LightShadows.Soft;
                lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }
        }

        private static void SetupNetworking()
        {
            // Network Manager
            var netManager = Object.FindFirstObjectByType<NetworkManager>();
            if (netManager == null)
            {
                var netGO = new GameObject("NetworkManager");
                netManager = netGO.AddComponent<NetworkManager>();
                
                // Add transport and assign it to NetworkManager
                var transport = netGO.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                netManager.NetworkConfig.NetworkTransport = transport;
                
                // Add our manager
                netGO.AddComponent<GameNetworkManager>();
            }
            else
            {
                // Ensure existing NetworkManager has transport configured
                var transport = netManager.GetComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                if (transport == null)
                {
                    transport = netManager.gameObject.AddComponent<Unity.Netcode.Transports.UTP.UnityTransport>();
                }
                if (netManager.NetworkConfig.NetworkTransport == null)
                {
                    netManager.NetworkConfig.NetworkTransport = transport;
                }
            }

            // Networked Game State (needs to be registered as prefab)
            var netState = Object.FindFirstObjectByType<NetworkedGameState>();
            if (netState == null)
            {
                var stateGO = new GameObject("NetworkedGameState");
                stateGO.AddComponent<NetworkObject>();
                stateGO.AddComponent<NetworkedGameState>();
            }
        }

        private static void SetupGameObjects()
        {
            // Game Manager
            var gameManager = Object.FindFirstObjectByType<GameManager>();
            if (gameManager == null)
            {
                var gmGO = new GameObject("GameManager");
                gameManager = gmGO.AddComponent<GameManager>();
            }

            // Board Renderer
            var boardRenderer = Object.FindFirstObjectByType<BoardRenderer>();
            if (boardRenderer == null)
            {
                var boardGO = new GameObject("BoardRenderer");
                boardRenderer = boardGO.AddComponent<BoardRenderer>();
            }

            // Units Container
            var container = GameObject.Find("UnitsContainer");
            if (container == null)
            {
                container = new GameObject("UnitsContainer");
            }

            // Wire up references via SerializedObject
            var gmSO = new SerializedObject(gameManager);
            gmSO.FindProperty("_boardRenderer").objectReferenceValue = boardRenderer;
            gmSO.FindProperty("_unitsContainer").objectReferenceValue = container.transform;
            gmSO.ApplyModifiedProperties();
        }

        private static void SetupUI()
        {
            // Canvas
            var canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                var canvasGO = new GameObject("UI Canvas");
                canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                
                var scaler = canvasGO.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.matchWidthOrHeight = 0.5f;
                
                canvasGO.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            }

            // Game UI
            var gameUI = Object.FindFirstObjectByType<GameUI>();
            if (gameUI == null)
            {
                canvas.gameObject.AddComponent<GameUI>();
            }

            // Event System
            var eventSystem = Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem == null)
            {
                var esGO = new GameObject("EventSystem");
                esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
                // Use new Input System UI module
                esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
        }

        [MenuItem("Bizarre Chess/Validate Scene")]
        public static void ValidateScene()
        {
            int issues = 0;

            if (Object.FindFirstObjectByType<GameManager>() == null)
            {
                Debug.LogError("[Validation] Missing GameManager!");
                issues++;
            }

            if (Object.FindFirstObjectByType<BoardRenderer>() == null)
            {
                Debug.LogError("[Validation] Missing BoardRenderer!");
                issues++;
            }

            if (Object.FindFirstObjectByType<NetworkManager>() == null)
            {
                Debug.LogWarning("[Validation] Missing NetworkManager - multiplayer won't work!");
                issues++;
            }

            if (Camera.main == null)
            {
                Debug.LogError("[Validation] Missing Main Camera!");
                issues++;
            }

            if (issues == 0)
            {
                Debug.Log("[Validation] Scene is valid! ✓");
            }
            else
            {
                Debug.LogWarning($"[Validation] Found {issues} issue(s). Run 'Bizarre Chess/Setup Current Scene' to fix.");
            }
        }
    }
}

