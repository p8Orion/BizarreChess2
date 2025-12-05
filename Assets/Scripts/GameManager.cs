using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Graph;
using BizarreChess.Core.Units;
using BizarreChess.Core.Rules;
using BizarreChess.Core.Factories;
using BizarreChess.Networking;
using BizarreChess.Persistence;
using BizarreChess.Presentation;

namespace BizarreChess
{
    /// <summary>
    /// Main game manager - connects all systems together.
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [Header("References (Auto-found if not set)")]
        [SerializeField] private BoardRenderer _boardRenderer;
        [SerializeField] private Transform _unitsContainer;
        [SerializeField] private UnitRenderer _unitPrefab;

        [Header("Network (Auto-found if not set)")]
        [SerializeField] private GameNetworkManager _networkManager;
        [SerializeField] private NetworkedGameState _networkedGameState;

        [Header("Configuration")]
        [SerializeField] private bool _offlineMode = true; // For testing without network

        private void FindRequiredComponents()
        {
            if (_boardRenderer == null)
                _boardRenderer = FindFirstObjectByType<BoardRenderer>();
            
            if (_unitsContainer == null)
            {
                var container = GameObject.Find("UnitsContainer");
                if (container != null)
                    _unitsContainer = container.transform;
                else
                {
                    container = new GameObject("UnitsContainer");
                    _unitsContainer = container.transform;
                }
            }

            if (_networkManager == null)
                _networkManager = FindFirstObjectByType<GameNetworkManager>();

            if (_networkedGameState == null)
                _networkedGameState = FindFirstObjectByType<NetworkedGameState>();

            // Ensure InputHandler exists
            var inputHandler = FindFirstObjectByType<Presentation.InputHandler>();
            if (inputHandler == null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    cam.gameObject.AddComponent<Presentation.InputHandler>();
                }
            }

            // Fix EventSystem - replace old StandaloneInputModule with new InputSystemUIInputModule
            FixEventSystemInputModule();
        }

        private void FixEventSystemInputModule()
        {
            var eventSystem = FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>();
            if (eventSystem == null)
            {
                var esGO = new GameObject("EventSystem");
                esGO.AddComponent<UnityEngine.EventSystems.EventSystem>();
                esGO.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
                return;
            }

            // Remove old input module if present
            var oldModule = eventSystem.GetComponent<UnityEngine.EventSystems.StandaloneInputModule>();
            if (oldModule != null)
            {
                Destroy(oldModule);
            }

            // Add new input module if not present
            var newModule = eventSystem.GetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            if (newModule == null)
            {
                eventSystem.gameObject.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
            }
        }

        // Core systems
        private BoardGraph _boardGraph;
        private GameState _gameState;
        private MoveValidator _moveValidator;
        private Dictionary<string, UnitDefinition> _unitDefinitions;

        // Rendering
        private Dictionary<int, UnitRenderer> _unitRenderers = new Dictionary<int, UnitRenderer>();

        // Selection
        private int? _selectedUnitId;
        private List<int> _validMoves = new List<int>();

        // Persistence
        private IProfileService _profileService;

        // Events
        public System.Action<int> OnUnitSelected;
        public System.Action OnSelectionCleared;
        public System.Action<int, int> OnGameEnded; // winnerId, localPlayerId

        #region Lifecycle

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // Initialize profile service
            _profileService = new MockProfileService();
        }

        private async void Start()
        {
            // Find components
            FindRequiredComponents();

            // Authenticate
            var authResult = await _profileService.Authenticate();
            if (authResult.Success)
            {
                Debug.Log($"[GameManager] Authenticated as {authResult.PlayerId}");
            }

            // Initialize unit definitions
            _unitDefinitions = ClassicChessFactory.CreateAllUnitDefinitions();

            if (_offlineMode)
            {
                StartOfflineGame();
            }
            else
            {
                SetupNetworkCallbacks();
            }
        }

        private void OnDestroy()
        {
            if (_boardRenderer != null)
                _boardRenderer.OnTileClicked -= OnTileClicked;
        }

        #endregion

        #region Offline Mode (Single Player / Local Testing)

        private void StartOfflineGame()
        {
            Debug.Log("[GameManager] Starting offline game...");
            
            // Create classic setup
            var setup = ClassicChessFactory.CreateCompleteSetup();
            _unitDefinitions = setup.UnitDefinitions;
            Debug.Log($"[GameManager] Created {_unitDefinitions.Count} unit definitions");

            // Initialize board
            _boardGraph = new BoardGraph(setup.Board);
            _moveValidator = new MoveValidator(_boardGraph, _unitDefinitions);
            Debug.Log($"[GameManager] Board has {setup.Board.Nodes.Count} nodes");

            // Initialize game state
            _gameState = new GameState();
            var playerSetups = new List<PlayerSetup>
            {
                new PlayerSetup { DisplayName = "Player 1", Army = setup.DefaultArmy },
                new PlayerSetup { DisplayName = "Player 2", Army = setup.DefaultArmy }
            };
            _gameState.Initialize(setup.Board, playerSetups);
            Debug.Log($"[GameManager] Game state has {_gameState.Units.Count} units");

            // Render
            if (_boardRenderer != null)
            {
                RenderBoard();
                RenderUnits();
                _boardRenderer.OnTileClicked += OnTileClicked;
                Debug.Log("[GameManager] Board and units rendered!");
            }
            else
            {
                Debug.LogError("[GameManager] BoardRenderer is NULL! Cannot render.");
            }

            Debug.Log("[GameManager] Offline game started!");
        }

        #endregion

        #region Network Mode

        private bool _networkCallbacksSetup = false;
        
        private void SetupNetworkCallbacks()
        {
            if (_networkCallbacksSetup) return;
            
            // Find NetworkedGameState if not set (it may be spawned later)
            if (_networkedGameState == null)
                _networkedGameState = FindFirstObjectByType<NetworkedGameState>();
                
            if (_networkedGameState != null)
            {
                SubscribeToNetworkedGameState();
            }
            else
            {
                Debug.LogWarning("[GameManager] NetworkedGameState not found yet, will retry...");
                StartCoroutine(WaitForNetworkedGameState());
            }

            if (_networkManager != null)
            {
                _networkManager.OnHostStarted += OnHostStarted;
                _networkManager.OnClientConnected += OnClientConnected;
            }
        }
        
        private void SubscribeToNetworkedGameState()
        {
            if (_networkCallbacksSetup || _networkedGameState == null) return;
            _networkCallbacksSetup = true;
            
            Debug.Log("[GameManager] Subscribing to NetworkedGameState events");
            _networkedGameState.OnUnitMoved += OnNetworkUnitMoved;
            _networkedGameState.OnUnitCaptured += OnNetworkUnitCaptured;
            _networkedGameState.OnTurnChanged += OnNetworkTurnChanged;
            _networkedGameState.OnGameEnded += OnNetworkGameEnded;
            _networkedGameState.OnGameStarted += OnNetworkGameStarted;
        }

        private System.Collections.IEnumerator WaitForNetworkedGameState()
        {
            while (_networkedGameState == null)
            {
                yield return new WaitForSeconds(0.5f);
                _networkedGameState = FindFirstObjectByType<NetworkedGameState>();
                
                if (_networkedGameState != null)
                {
                    Debug.Log("[GameManager] Found NetworkedGameState!");
                    SubscribeToNetworkedGameState();
                }
            }
        }

        private void OnHostStarted()
        {
            Debug.Log("[GameManager] Host started, waiting for opponent...");
        }

        private void OnClientConnected()
        {
            Debug.Log("[GameManager] Connected to game!");
            
            // Initialize rendering once we know the board
            var board = _networkedGameState.GetBoardGraph();
            if (board != null)
            {
                _boardGraph = board;
                RenderBoard();
            }
        }

        private void OnNetworkUnitMoved(int unitId, int fromNode, int toNode)
        {
            Debug.Log($"[GameManager] OnNetworkUnitMoved: Unit {unitId} from {fromNode} to {toNode}");
            
            if (_unitRenderers.TryGetValue(unitId, out var renderer))
            {
                var position = GetWorldPosition(toNode);
                Debug.Log($"[GameManager] Moving renderer to position {position}");
                renderer.MoveTo(position);
            }
            else
            {
                Debug.LogWarning($"[GameManager] No renderer found for unit {unitId}! Total renderers: {_unitRenderers.Count}");
            }
            ClearSelection();
        }

        private void OnNetworkUnitCaptured(int unitId)
        {
            if (_unitRenderers.TryGetValue(unitId, out var renderer))
            {
                renderer.PlayDeathAnimation();
            }
        }

        private void OnNetworkTurnChanged()
        {
            ClearSelection();
            // Update UI to show whose turn it is
        }

        private void OnNetworkGameEnded(int winnerId)
        {
            int localPlayerId = _networkedGameState.LocalPlayerId;
            OnGameEnded?.Invoke(winnerId, localPlayerId);
            Debug.Log(winnerId == localPlayerId ? "You won!" : (winnerId == -1 ? "Draw!" : "You lost!"));
        }

        private bool _networkGameInitialized = false;
        
        private void OnNetworkGameStarted()
        {
            // Prevent double initialization
            if (_networkGameInitialized)
            {
                Debug.LogWarning("[GameManager] Network game already initialized, skipping");
                return;
            }
            _networkGameInitialized = true;
            
            Debug.Log("[GameManager] Network game started! Rendering board and units...");
            
            // Get game data from NetworkedGameState
            _boardGraph = _networkedGameState.GetBoardGraph();
            _unitDefinitions = ClassicChessFactory.CreateAllUnitDefinitions();
            
            // Initialize validator
            if (_boardGraph != null)
            {
                _moveValidator = new MoveValidator(_boardGraph, _unitDefinitions);
            }
            
            // Render
            if (_boardRenderer != null && _boardGraph != null)
            {
                RenderBoard();
                
                // Render units from networked state
                var units = _networkedGameState.GetAllUnits();
                Debug.Log($"[GameManager] About to render {units.Count} units...");
                
                foreach (var unit in units)
                {
                    SpawnUnitRenderer(unit);
                }
                
                _boardRenderer.OnTileClicked -= OnTileClicked; // Unsub first to prevent doubles
                _boardRenderer.OnTileClicked += OnTileClicked;
                Debug.Log($"[GameManager] Rendered {_unitRenderers.Count} units!");
            }
            else
            {
                Debug.LogError("[GameManager] Cannot render - BoardRenderer or BoardGraph is null!");
            }
        }

        #endregion

        #region Rendering

        private void RenderBoard()
        {
            if (_boardRenderer == null || _boardGraph == null) return;
            _boardRenderer.RenderBoard(_boardGraph);
        }

        private void RenderUnits()
        {
            // Clear existing
            foreach (var renderer in _unitRenderers.Values)
            {
                if (renderer != null)
                    Destroy(renderer.gameObject);
            }
            _unitRenderers.Clear();

            // Render all units
            foreach (var unit in _gameState.GetAliveUnits())
            {
                SpawnUnitRenderer(unit);
            }
        }

        private void SpawnUnitRenderer(UnitState unit)
        {
            // Don't create duplicate renderers
            if (_unitRenderers.ContainsKey(unit.UnitId))
            {
                Debug.LogWarning($"[GameManager] Renderer already exists for unit {unit.UnitId}, skipping");
                return;
            }
            
            if (!_unitDefinitions.TryGetValue(unit.DefinitionId, out var definition))
                return;

            UnitRenderer renderer;
            
            if (_unitPrefab != null)
            {
                renderer = Instantiate(_unitPrefab, _unitsContainer);
            }
            else
            {
                // Create 3D chess piece
                bool isWhite = unit.OwnerId == 0;
                var pieceGO = ChessPieceMeshGenerator.CreatePieceObject(definition.PieceType, isWhite);
                pieceGO.name = $"Unit_{unit.UnitId}_{unit.DefinitionId}";
                pieceGO.transform.SetParent(_unitsContainer);
                
                renderer = pieceGO.AddComponent<UnitRenderer>();
            }

            var position = GetWorldPosition(unit.CurrentNodeId);
            renderer.Initialize(unit, definition, position);
            renderer.OnClicked += () => OnUnitClicked(unit.UnitId);

            _unitRenderers[unit.UnitId] = renderer;
        }

        private Vector3 GetWorldPosition(int nodeId)
        {
            // Use BoardRenderer's method to ensure positions match
            if (_boardRenderer != null)
            {
                return _boardRenderer.GetWorldPositionForNode(nodeId);
            }
            
            // Fallback if no BoardRenderer
            if (_boardGraph == null) return Vector3.zero;
            var pos = _boardGraph.GetNodePosition(nodeId);
            return new Vector3(pos.x * 1.05f, 0, pos.y * 1.05f);
        }

        #endregion

        #region Input Handling

        private void OnTileClicked(int nodeId)
        {
            if (_offlineMode)
            {
                HandleOfflineTileClick(nodeId);
            }
            else
            {
                HandleNetworkTileClick(nodeId);
            }
        }

        private void HandleOfflineTileClick(int nodeId)
        {
            // Check if clicking on valid move
            if (_selectedUnitId.HasValue && _validMoves.Contains(nodeId))
            {
                ExecuteMove(_selectedUnitId.Value, nodeId);
                return;
            }

            // Check if clicking on a unit
            var unitAtNode = _gameState.GetUnitAtNode(nodeId);
            if (unitAtNode != null && unitAtNode.OwnerId == _gameState.CurrentPlayerId)
            {
                SelectUnit(unitAtNode.UnitId);
            }
            else
            {
                ClearSelection();
            }
        }

        private void HandleNetworkTileClick(int nodeId)
        {
            if (!_networkedGameState.IsMyTurn())
            {
                Debug.Log("Not your turn!");
                return;
            }

            if (_selectedUnitId.HasValue && _validMoves.Contains(nodeId))
            {
                // Send move request to server
                _networkedGameState.RequestMoveServerRpc(_selectedUnitId.Value, nodeId);
                ClearSelection();
                return;
            }

            // Selection logic similar to offline
            var units = _networkedGameState.GetAllUnits();
            var unitAtNode = units.Find(u => u.CurrentNodeId == nodeId);
            
            if (unitAtNode != null && unitAtNode.OwnerId == _networkedGameState.LocalPlayerId)
            {
                SelectUnit(unitAtNode.UnitId);
            }
            else
            {
                ClearSelection();
            }
        }

        private void OnUnitClicked(int unitId)
        {
            if (_offlineMode)
            {
                var unit = _gameState.GetUnit(unitId);
                if (unit != null && unit.OwnerId == _gameState.CurrentPlayerId)
                {
                    SelectUnit(unitId);
                }
            }
            else
            {
                // Network mode - check if it's our unit
                if (_networkedGameState.IsMyTurn())
                {
                    SelectUnit(unitId);
                }
            }
        }

        private void SelectUnit(int unitId)
        {
            ClearSelection();

            _selectedUnitId = unitId;

            // Highlight unit
            if (_unitRenderers.TryGetValue(unitId, out var renderer))
            {
                renderer.SetSelected(true);
            }

            // Get valid moves
            if (_offlineMode)
            {
                var unit = _gameState.GetUnit(unitId);
                if (unit != null && _unitDefinitions.TryGetValue(unit.DefinitionId, out var def))
                {
                    _validMoves = _moveValidator.GetValidMovesForUnit(unit, def, _gameState.Units);
                }
            }
            else
            {
                _validMoves = _networkedGameState.GetValidMovesForUnit(unitId);
            }

            // Highlight valid moves
            _boardRenderer.HighlightValidMoves(_validMoves);

            OnUnitSelected?.Invoke(unitId);
        }

        private void ClearSelection()
        {
            if (_selectedUnitId.HasValue)
            {
                if (_unitRenderers.TryGetValue(_selectedUnitId.Value, out var renderer))
                {
                    renderer.SetSelected(false);
                }
            }

            _selectedUnitId = null;
            _validMoves.Clear();
            _boardRenderer?.ClearHighlights();

            OnSelectionCleared?.Invoke();
        }

        #endregion

        #region Game Actions

        private void ExecuteMove(int unitId, int targetNode)
        {
            var unit = _gameState.GetUnit(unitId);
            if (unit == null) return;

            // Validate
            var result = _moveValidator.ValidateMove(unit, targetNode, _gameState.Units, _gameState.CurrentPlayerId);
            if (!result.IsValid)
            {
                Debug.LogWarning($"Invalid move: {result.Error}");
                return;
            }

            // Handle capture visually
            if (result.IsCapture && result.CapturedUnitId.HasValue)
            {
                if (_unitRenderers.TryGetValue(result.CapturedUnitId.Value, out var capturedRenderer))
                {
                    capturedRenderer.PlayDeathAnimation();
                }
            }

            // Execute
            int fromNode = unit.CurrentNodeId;
            _gameState.ExecuteMove(unitId, targetNode, result.IsCapture, result.CapturedUnitId);

            // Animate
            if (_unitRenderers.TryGetValue(unitId, out var renderer))
            {
                renderer.MoveTo(GetWorldPosition(targetNode));
            }

            // Check win
            _gameState.CheckWinConditions(_moveValidator);

            if (_gameState.Phase == GamePhase.Ended)
            {
                HandleGameEnd();
            }

            ClearSelection();

            // End turn (in classic chess, move = end turn)
            _gameState.EndTurn();
            Debug.Log($"Turn {_gameState.TurnNumber}, Player {_gameState.CurrentPlayerId}'s turn");
        }

        private void HandleGameEnd()
        {
            string message = _gameState.WinnerId.HasValue
                ? $"Player {_gameState.WinnerId.Value} wins by {_gameState.EndReason}!"
                : $"Game ended in {_gameState.EndReason}";
            
            Debug.Log($"[GameManager] {message}");
            OnGameEnded?.Invoke(_gameState.WinnerId ?? -1, _gameState.CurrentPlayerId);
        }

        #endregion

        #region Public API

        /// <summary>
        /// Start a new offline game.
        /// </summary>
        public void NewOfflineGame()
        {
            _offlineMode = true;
            StartOfflineGame();
        }

        /// <summary>
        /// Host a multiplayer game.
        /// </summary>
        public void HostGame()
        {
            _offlineMode = false;
            SetupNetworkCallbacks();
            _networkManager?.StartHost();
        }

        /// <summary>
        /// Join a multiplayer game.
        /// </summary>
        public void JoinGame(string address)
        {
            _offlineMode = false;
            SetupNetworkCallbacks();
            _networkManager?.StartClient(address);
        }

        /// <summary>
        /// Get the current profile service.
        /// </summary>
        public IProfileService GetProfileService() => _profileService;

        #endregion
    }
}

