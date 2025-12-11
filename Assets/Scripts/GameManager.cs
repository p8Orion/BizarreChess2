using System.Collections.Generic;
using UnityEngine;
using BizarreChess.Core.Board;
using BizarreChess.Core.Units;
using BizarreChess.Core.Rules;
using BizarreChess.Core.Factories;
using BizarreChess.Core.Items;
using BizarreChess.Networking;
using BizarreChess.Persistence;
using BizarreChess.Presentation;
using UnitRendererType = BizarreChess.Presentation.UnitRenderer.UnitRenderer;
using Token2DMeshGenerator = BizarreChess.Presentation.UnitRenderer.Token2DMeshGenerator;
using RevolutionMeshGenerator = BizarreChess.Presentation.UnitRenderer.RevolutionMeshGenerator;

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
        [SerializeField] private UnitRendererType _unitPrefab;

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

            if (_itemsContainer == null)
            {
                var container = GameObject.Find("ItemsContainer");
                if (container != null)
                    _itemsContainer = container.transform;
                else
                {
                    container = new GameObject("ItemsContainer");
                    _itemsContainer = container.transform;
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
        private Dictionary<int, UnitRendererType> _unitRenderers = new Dictionary<int, UnitRendererType>();
        private Dictionary<string, ItemRenderer> _itemRenderers = new Dictionary<string, ItemRenderer>();
        private Transform _itemsContainer;

        // Selection
        private int? _selectedUnitId;
        private List<int> _validMoves = new List<int>();
        private MoveTargets _selectedMoves; // Categorized moves for current selection
        private List<int> _capturableUnitIds = new List<int>();
        
        // Hover state
        private int? _hoveredUnitId;
        private List<int> _hoverMoves = new List<int>();
        private List<int> _hoverCapturableUnitIds = new List<int>();
        
        // Drag state
        private bool _isDragging;
        private int? _draggingUnitId;
        private float _lastDragSyncTime;
        private const float DragSyncInterval = 0.25f; // Sync every 250ms

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
            // Authentication complete

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

            
            // Create classic setup
            var setup = ChessFactory.CreateDefaultSetup();
            _unitDefinitions = setup.Pieces;


            // Initialize board
            _boardGraph = new BoardGraph(setup.Board);
            _moveValidator = new MoveValidator(_boardGraph, _unitDefinitions);


            // Initialize game state
            _gameState = new GameState();
            var playerSetups = new List<PlayerSetup>
            {
                new PlayerSetup { DisplayName = "Player 1", Army = setup.GetArmy(0) },
                new PlayerSetup { DisplayName = "Player 2", Army = setup.GetArmy(1) }
            };
            _gameState.Initialize(setup.Board, playerSetups);


            // Add items from setup
            AddItemsFromSetup(setup.Items);

            // Render
            if (_boardRenderer != null)
            {
                RenderBoard();
                RenderUnits();
                RenderItems();
                _boardRenderer.OnTileClicked += OnTileClicked;

            }
            else
            {

            }


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
            

            _networkedGameState.OnUnitMoved += OnNetworkUnitMoved;
            _networkedGameState.OnUnitCaptured += OnNetworkUnitCaptured;
            _networkedGameState.OnCaptureBlocked += OnNetworkCaptureBlocked;
            _networkedGameState.OnTurnChanged += OnNetworkTurnChanged;
            _networkedGameState.OnGameEnded += OnNetworkGameEnded;
            _networkedGameState.OnGameStarted += OnNetworkGameStarted;
            
            // Drag synchronization events
            _networkedGameState.OnDragStarted += OnRemoteDragStarted;
            _networkedGameState.OnDragUpdated += OnRemoteDragUpdate;
            _networkedGameState.OnDragEnded += OnRemoteDragEnded;
            
            // Item events
            _networkedGameState.OnItemPickedUp += OnNetworkItemPickedUp;
            _networkedGameState.OnItemDropped += OnNetworkItemDropped;
        }

        private System.Collections.IEnumerator WaitForNetworkedGameState()
        {
            while (_networkedGameState == null)
            {
                yield return new WaitForSeconds(0.5f);
                _networkedGameState = FindFirstObjectByType<NetworkedGameState>();
                
                if (_networkedGameState != null)
                {

                    SubscribeToNetworkedGameState();
                }
            }
        }

        private void OnHostStarted()
        {

        }

        private void OnClientConnected()
        {

            
            // Initialize rendering once we know the board
            var board = _networkedGameState.GetBoardGraph();
            if (board != null)
            {
                _boardGraph = board;
                RenderBoard();
            }
        }

        private void OnNetworkUnitMoved(int unitId, int fromNode, int toNode, bool isRangedCapture)
        {

            
            // Only move visually if NOT a ranged capture (ranged attackers stay in place)
            if (!isRangedCapture && _unitRenderers.TryGetValue(unitId, out var renderer))
            {
                var position = GetWorldPosition(toNode);

                renderer.MoveTo(position);
            }
            else if (isRangedCapture)
            {

            }
            else
            {

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

        private void OnNetworkCaptureBlocked(int attackerUnitId, int defenderUnitId, int fromNode)
        {

            
            // Play forcefield break animation on the defender
            if (_unitRenderers.TryGetValue(defenderUnitId, out var defenderRenderer))
            {
                defenderRenderer.PlayForcefieldBreakAnimation();
            }
            
            // Attacker bounces back to original position visually
            if (_unitRenderers.TryGetValue(attackerUnitId, out var attackerRenderer))
            {
                attackerRenderer.MoveTo(GetWorldPosition(fromNode));
            }
            
            ClearSelection();
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

        }

        private void OnNetworkItemPickedUp(int unitId, string itemId, int nodeId)
        {

            
            // Visual feedback
            DestroyItemRenderer(itemId, animate: true);

            // Update unit visual (forcefield will be auto-detected)
            if (_unitRenderers.TryGetValue(unitId, out var renderer))
            {
                var unit = _gameState?.GetUnit(unitId);
                if (unit != null)
                {
                    renderer.UpdateState(unit);
                }
            }

            ClearSelection();
        }

        private void OnNetworkItemDropped(string itemId, int nodeId)
        {

            
            // Get the item from game state and spawn renderer
            var item = _gameState?.GetItem(itemId);
            if (item != null)
            {
                SpawnDroppedItemRenderer(item);
            }
        }

        private bool _networkGameInitialized = false;
        
        private void OnNetworkGameStarted()
        {
            // Prevent double initialization
            if (_networkGameInitialized)
            {

                return;
            }
            _networkGameInitialized = true;
            

            
            // Get game data from NetworkedGameState
            _boardGraph = _networkedGameState.GetBoardGraph();
            _unitDefinitions = _networkedGameState.GetPieces();
            
            // Initialize validator
            if (_boardGraph != null)
            {
                _moveValidator = new MoveValidator(_boardGraph, _unitDefinitions);
            }
            
            // Get game state reference for items
            _gameState = _networkedGameState.GetGameState();

            // Render
            if (_boardRenderer != null && _boardGraph != null)
            {
                RenderBoard();
                
                // Render units from networked state
                var units = _networkedGameState.GetAllUnits();

                
                foreach (var unit in units)
                {
                    SpawnUnitRenderer(unit);
                }

                // Render items
                RenderItems();
                
                _boardRenderer.OnTileClicked -= OnTileClicked; // Unsub first to prevent doubles
                _boardRenderer.OnTileClicked += OnTileClicked;

            }
            else
            {

            }
            
            // Position camera based on which player we are
            PositionCameraForPlayer(_networkedGameState.LocalPlayerId);
        }
        
        /// <summary>
        /// Position the camera to view from the player's side of the board.
        /// </summary>
        private void PositionCameraForPlayer(int playerId)
        {
            var cameraController = Camera.main?.GetComponent<CameraController>();
            if (cameraController != null)
            {
                cameraController.SetPlayerView(playerId, instant: true);

            }
            else
            {

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

                return;
            }
            
            if (!_unitDefinitions.TryGetValue(unit.DefinitionId, out var definition))
                return;

            UnitRendererType renderer;
            
            if (_unitPrefab != null)
            {
                renderer = Instantiate(_unitPrefab, _unitsContainer);
            }
            else
            {
                // Create piece based on render mode
                int playerId = unit.OwnerId;
                GameObject pieceGO;

                switch (definition.RenderMode)
                {
                    case PieceRenderMode.Token2D:
                        pieceGO = Token2DMeshGenerator.CreateTokenObject(
                            definition.TokenTexture, 
                            playerId
                        );
                        break;

                    case PieceRenderMode.RevolutionVolume:
                        pieceGO = RevolutionMeshGenerator.CreateRevolutionObject(
                            definition.RevolutionTexture,
                            playerId,
                            definition.PieceHeight
                        );
                        break;

                    default:
                        // Fallback to Token2D with no texture
                        pieceGO = Token2DMeshGenerator.CreateTokenObject(null, playerId);
                        break;
                }

                pieceGO.name = $"Unit_{unit.UnitId}_{unit.DefinitionId}";
                pieceGO.transform.SetParent(_unitsContainer);
                
                renderer = pieceGO.AddComponent<UnitRendererType>();
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

        #region Item Rendering

        /// <summary>
        /// Add items from the setup to the game state.
        /// </summary>
        private void AddItemsFromSetup(List<Item> items)
        {
            if (items == null) return;

            foreach (var item in items)
            {
                // Clone items so each game has independent instances
                var clonedItem = item.Clone();
                _gameState.AddItem(clonedItem);

            }
        }

        /// <summary>
        /// Render all items on the board.
        /// </summary>
        private void RenderItems()
        {
            // Clear existing renderers
            foreach (var renderer in _itemRenderers.Values)
            {
                if (renderer != null)
                    Destroy(renderer.gameObject);
            }
            _itemRenderers.Clear();

            // Render all items
            foreach (var item in _gameState.Items)
            {
                SpawnItemRenderer(item);
            }
        }

        /// <summary>
        /// Spawn a renderer for an item.
        /// </summary>
        private void SpawnItemRenderer(Item item)
        {
            if (_itemRenderers.ContainsKey(item.Id))
            {

                return;
            }

            var itemGO = new GameObject($"Item_{item.Id}_{item.DisplayName}");
            itemGO.transform.SetParent(_itemsContainer);

            var renderer = itemGO.AddComponent<ItemRenderer>();
            var position = GetWorldPosition(item.NodeId);
            renderer.Initialize(item, position);
            renderer.OnClicked += () => OnItemClicked(item.Id);

            _itemRenderers[item.Id] = renderer;
        }

        /// <summary>
        /// Spawn a renderer for a newly dropped item with animation.
        /// </summary>
        private void SpawnDroppedItemRenderer(Item item)
        {
            SpawnItemRenderer(item);
            
            if (_itemRenderers.TryGetValue(item.Id, out var renderer))
            {
                renderer.PlaySpawnAnimation();
            }
        }

        /// <summary>
        /// Remove an item renderer.
        /// </summary>
        private void DestroyItemRenderer(string itemId, bool animate = true)
        {
            if (_itemRenderers.TryGetValue(itemId, out var renderer))
            {
                if (animate)
                {
                    renderer.PlayPickupAnimation();
                }
                else
                {
                    Destroy(renderer.gameObject);
                }
                _itemRenderers.Remove(itemId);
            }
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
                if (unit == null) return;

                // If we have a selected unit and clicked on an enemy, try to capture
                if (_selectedUnitId.HasValue && unit.OwnerId != _gameState.CurrentPlayerId)
                {
                    int targetNode = unit.CurrentNodeId;
                    if (_validMoves.Contains(targetNode))
                    {
                        ExecuteMove(_selectedUnitId.Value, targetNode);
                        return;
                    }
                }

                // Select or deselect our own unit
                if (unit.OwnerId == _gameState.CurrentPlayerId)
                {
                    // If clicking on already selected unit, deselect
                    if (_selectedUnitId.HasValue && _selectedUnitId.Value == unitId)
                    {
                        ClearSelection();
                    }
                    else
                    {
                        SelectUnit(unitId);
                    }
                }
            }
            else
            {
                // Network mode
                if (!_networkedGameState.IsMyTurn()) return;

                var units = _networkedGameState.GetAllUnits();
                var unit = units.Find(u => u.UnitId == unitId);
                if (unit == null) return;

                // If we have a selected unit and clicked on an enemy, try to capture
                if (_selectedUnitId.HasValue && unit.OwnerId != _networkedGameState.LocalPlayerId)
                {
                    int targetNode = unit.CurrentNodeId;
                    if (_validMoves.Contains(targetNode))
                    {
                        _networkedGameState.RequestMoveServerRpc(_selectedUnitId.Value, targetNode);
                        ClearSelection();
                        return;
                    }
                }

                // Select or deselect our own unit
                if (unit.OwnerId == _networkedGameState.LocalPlayerId)
                {
                    // If clicking on already selected unit, deselect
                    if (_selectedUnitId.HasValue && _selectedUnitId.Value == unitId)
                    {
                        ClearSelection();
                    }
                    else
                    {
                        SelectUnit(unitId);
                    }
                }
            }
        }

        private void OnItemClicked(string itemId)
        {
            if (_offlineMode)
            {
                HandleOfflineItemClick(itemId);
            }
            else
            {
                HandleNetworkItemClick(itemId);
            }
        }

        private void HandleOfflineItemClick(string itemId)
        {

            
            var item = _gameState.GetItem(itemId);
            if (item == null)
            {

                return;
            }



            // Check if we have a unit selected that can pick up this item
            if (_selectedUnitId.HasValue)
            {
                var unit = _gameState.GetUnit(_selectedUnitId.Value);


                
                if (unit != null && 
                    unit.OwnerId == _gameState.CurrentPlayerId &&
                    unit.CurrentNodeId == item.NodeId &&
                    unit.CanPickUpItem &&
                    unit.CanAct)
                {
                    // Execute pickup

                    ExecuteItemPickup(_selectedUnitId.Value);
                    return;
                }
                else
                {

                }
            }
            else
            {

            }

            // If clicking on item without valid unit selected, check if there's our unit on that tile
            var unitAtNode = _gameState.GetUnitAtNode(item.NodeId);

            if (unitAtNode != null && 
                unitAtNode.OwnerId == _gameState.CurrentPlayerId &&
                unitAtNode.CanPickUpItem &&
                unitAtNode.CanAct)
            {
                // Select the unit first to show it can pick up the item
                SelectUnit(unitAtNode.UnitId);
            }
        }

        private void HandleNetworkItemClick(string itemId)
        {
            if (!_networkedGameState.IsMyTurn())
            {

                return;
            }

            // Similar to offline but use network RPC
            var units = _networkedGameState.GetAllUnits();
            
            if (_selectedUnitId.HasValue)
            {
                var unit = units.Find(u => u.UnitId == _selectedUnitId.Value);
                var item = _gameState.GetItem(itemId);
                
                if (unit != null && item != null &&
                    unit.OwnerId == _networkedGameState.LocalPlayerId &&
                    unit.CurrentNodeId == item.NodeId &&
                    unit.CanPickUpItem &&
                    unit.CanAct)
                {
                    _networkedGameState.RequestItemPickupServerRpc(_selectedUnitId.Value);
                    ClearSelection();
                    return;
                }
            }
        }

        private void ExecuteItemPickup(int unitId)
        {
            var result = _gameState.TryExecuteItemPickup(unitId, _gameState.CurrentPlayerId);
            
            if (!result.Success)
            {

                return;
            }



            // Visual feedback
            DestroyItemRenderer(result.ItemId, animate: true);

            // Update unit visual (forcefield will be auto-detected by UnitRenderer.UpdateState)
            if (_unitRenderers.TryGetValue(unitId, out var renderer))
            {
                var unit = _gameState.GetUnit(unitId);
                if (unit != null)
                {
                    renderer.UpdateState(unit);
                }
            }

            ClearSelection();

        }

        private void SelectUnit(int unitId)
        {
            ClearSelection();

            _selectedUnitId = unitId;

            // Get the unit to know its owner
            int ownerId = 0;
            MoveTargets categorizedMoves = null;
            
            if (_offlineMode)
            {
                var unit = _gameState.GetUnit(unitId);
                if (unit != null)
                {
                    ownerId = unit.OwnerId;
                    categorizedMoves = _moveValidator.GetCategorizedMovesForUnit(unit, _gameState.Units);
                    _validMoves = categorizedMoves.GetAll();
                }
            }
            else
            {
                var units = _networkedGameState.GetAllUnits();
                var unit = units.Find(u => u.UnitId == unitId);
                if (unit != null)
                {
                    ownerId = unit.OwnerId;
                }
                categorizedMoves = _networkedGameState.GetCategorizedMovesForUnit(unitId);
                _validMoves = categorizedMoves?.GetAll() ?? new List<int>();
            }

            // Store categorized moves for validation during drag
            _selectedMoves = categorizedMoves;

            // Highlight unit
            if (_unitRenderers.TryGetValue(unitId, out var renderer))
            {
                renderer.SetSelected(true);
            }

            // Show categorized move indicators (circle/ring)
            if (categorizedMoves != null)
            {
                _boardRenderer?.ShowCategorizedMoves(categorizedMoves, ownerId);
            }

            // Mark capturable enemy units
            MarkCapturableUnits(ownerId);

            // Highlight pickable items
            HighlightPickableItems(unitId);

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

            // Clear capturable unit highlights
            ClearCapturableMarks();

            // Clear item highlights
            ClearItemHighlights();

            _selectedUnitId = null;
            _validMoves.Clear();
            _selectedMoves = null;
            _boardRenderer?.ClearHighlights();

            OnSelectionCleared?.Invoke();
        }

        private void HighlightPickableItems(int unitId)
        {
            ClearItemHighlights();

            UnitState unit = null;
            if (_offlineMode)
            {
                unit = _gameState.GetUnit(unitId);
            }
            else
            {
                var units = _networkedGameState.GetAllUnits();
                unit = units.Find(u => u.UnitId == unitId);
            }

            if (unit == null || !unit.CanPickUpItem || !unit.CanAct) return;

            // Find item at unit's position
            var item = _gameState.GetItemAtNode(unit.CurrentNodeId);
            if (item != null && _itemRenderers.TryGetValue(item.Id, out var renderer))
            {
                renderer.SetHighlighted(true);
            }
        }

        private void ClearItemHighlights()
        {
            foreach (var renderer in _itemRenderers.Values)
            {
                renderer?.SetHighlighted(false);
            }
        }

        private void MarkCapturableUnits(int attackerOwnerId)
        {
            ClearCapturableMarks();

            // Find enemy units on valid move targets
            List<UnitState> allUnits = _offlineMode 
                ? _gameState?.Units 
                : _networkedGameState?.GetAllUnits();

            if (allUnits == null) return;

            foreach (var unit in allUnits)
            {
                if (!unit.IsAlive) continue;
                if (unit.OwnerId == attackerOwnerId) continue; // Skip own units

                // Check if this enemy is on a valid capture square
                if (_validMoves.Contains(unit.CurrentNodeId))
                {
                    if (_unitRenderers.TryGetValue(unit.UnitId, out var renderer))
                    {
                        renderer.SetCapturable(true, attackerOwnerId);
                        _capturableUnitIds.Add(unit.UnitId);
                    }
                }
            }
        }

        private void ClearCapturableMarks()
        {
            foreach (var unitId in _capturableUnitIds)
            {
                if (_unitRenderers.TryGetValue(unitId, out var renderer))
                {
                    renderer.SetCapturable(false, 0);
                }
            }
            _capturableUnitIds.Clear();
        }

        #endregion

        #region Hover Preview

        /// <summary>
        /// Called when mouse enters a unit (any unit, not just own pieces).
        /// Shows preview of valid moves.
        /// </summary>
        public void OnUnitHoverEnter(int unitId)
        {
            // Don't show hover preview for the currently selected unit
            if (_selectedUnitId.HasValue && _selectedUnitId.Value == unitId)
                return;
            
            _hoveredUnitId = unitId;
            
            // Get unit info and calculate valid moves
            int ownerId = 0;
            MoveTargets categorizedMoves = null;
            
            if (_offlineMode)
            {
                var unit = _gameState?.GetUnit(unitId);
                if (unit != null)
                {
                    ownerId = unit.OwnerId;
                    categorizedMoves = _moveValidator.GetCategorizedMovesForUnit(unit, _gameState.Units);
                    _hoverMoves = categorizedMoves.GetAll();
                }
            }
            else
            {
                var units = _networkedGameState?.GetAllUnits();
                var unit = units?.Find(u => u.UnitId == unitId);
                if (unit != null)
                {
                    ownerId = unit.OwnerId;
                }
                categorizedMoves = _networkedGameState?.GetCategorizedMovesForUnit(unitId);
                _hoverMoves = categorizedMoves?.GetAll() ?? new List<int>();
            }
            
            // Show hover with categorized indicators (circle/ring/crosshair) in secondary color
            if (categorizedMoves != null)
            {
                _boardRenderer?.ShowCategorizedHoverMoves(categorizedMoves, ownerId);
                
                // Highlight capturable units on hover
                MarkHoverCapturableUnits(categorizedMoves, ownerId);
            }
        }

        /// <summary>
        /// Called when mouse exits a unit.
        /// </summary>
        public void OnUnitHoverExit(int unitId)
        {
            if (_hoveredUnitId.HasValue && _hoveredUnitId.Value == unitId)
            {
                _hoveredUnitId = null;
                _hoverMoves.Clear();
                _boardRenderer?.ClearHoverHighlights();
                ClearHoverCapturableMarks();
            }
        }

        /// <summary>
        /// Highlight units that can be captured based on hover preview.
        /// </summary>
        private void MarkHoverCapturableUnits(MoveTargets moves, int attackerOwnerId)
        {
            ClearHoverCapturableMarks();

            List<UnitState> allUnits = _offlineMode 
                ? _gameState?.Units 
                : _networkedGameState?.GetAllUnits();

            if (allUnits == null) return;

            // Combine all capture targets (CaptureOnly, Both, RangedCapture)
            var captureTargets = new HashSet<int>();
            foreach (var nodeId in moves.CaptureOnly) captureTargets.Add(nodeId);
            foreach (var nodeId in moves.Both) captureTargets.Add(nodeId);
            foreach (var nodeId in moves.RangedCapture) captureTargets.Add(nodeId);

            foreach (var unit in allUnits)
            {
                if (!unit.IsAlive) continue;
                if (unit.OwnerId == attackerOwnerId) continue;

                // Check if this enemy is on a valid capture square
                if (captureTargets.Contains(unit.CurrentNodeId))
                {
                    if (_unitRenderers.TryGetValue(unit.UnitId, out var renderer))
                    {
                        renderer.SetCapturable(true, attackerOwnerId, isHover: true);
                        _hoverCapturableUnitIds.Add(unit.UnitId);
                    }
                }
            }
        }

        /// <summary>
        /// Clear hover capturable marks (but not selection capturable marks).
        /// </summary>
        private void ClearHoverCapturableMarks()
        {
            foreach (var unitId in _hoverCapturableUnitIds)
            {
                // Only clear if not also in selection capturable list
                if (!_capturableUnitIds.Contains(unitId))
                {
                    if (_unitRenderers.TryGetValue(unitId, out var renderer))
                    {
                        renderer.SetCapturable(false, 0);
                    }
                }
            }
            _hoverCapturableUnitIds.Clear();
        }

        #endregion

        #region Drag and Drop

        /// <summary>
        /// Check if a unit can be dragged by the current player.
        /// </summary>
        public bool CanDragUnit(int unitId)
        {
            if (_offlineMode)
            {
                var unit = _gameState?.GetUnit(unitId);
                return unit != null && unit.OwnerId == _gameState.CurrentPlayerId;
            }
            else
            {
                if (_networkedGameState == null || !_networkedGameState.IsMyTurn())
                    return false;
                
                var units = _networkedGameState.GetAllUnits();
                var unit = units.Find(u => u.UnitId == unitId);
                return unit != null && unit.OwnerId == _networkedGameState.LocalPlayerId;
            }
        }

        /// <summary>
        /// Get the current node of a unit.
        /// </summary>
        public int GetUnitCurrentNode(int unitId)
        {
            if (_offlineMode)
            {
                var unit = _gameState?.GetUnit(unitId);
                return unit?.CurrentNodeId ?? -1;
            }
            else
            {
                var units = _networkedGameState?.GetAllUnits();
                var unit = units?.Find(u => u.UnitId == unitId);
                return unit?.CurrentNodeId ?? -1;
            }
        }

        /// <summary>
        /// Called when a drag starts on a unit.
        /// </summary>
        public void OnUnitDragStarted(int unitId)
        {
            _isDragging = true;
            _draggingUnitId = unitId;
            
            // Select the unit (shows valid moves)
            SelectUnit(unitId);
            
            // Network sync: notify other players
            if (!_offlineMode && _networkedGameState != null)
            {
                _networkedGameState.NotifyDragStartServerRpc(unitId);
            }
            

        }

        /// <summary>
        /// Called during drag to update position.
        /// </summary>
        public void OnUnitDragUpdate(int unitId, Vector3 worldPosition)
        {
            if (!_isDragging || _draggingUnitId != unitId) return;
            
            // Throttle network sync
            if (!_offlineMode && _networkedGameState != null)
            {
                if (Time.time - _lastDragSyncTime >= DragSyncInterval)
                {
                    _lastDragSyncTime = Time.time;
                    _networkedGameState.UpdateDragPositionServerRpc(unitId, worldPosition);
                }
            }
        }

        /// <summary>
        /// Called when a drag ends.
        /// Returns true if the move was successful.
        /// </summary>
        /// <summary>
        /// Called when a drag ends.
        /// Returns (success, stayInPlace) - stayInPlace is true for ranged captures where unit doesn't move.
        /// </summary>
        public (bool success, bool stayInPlace) OnUnitDragEnded(int unitId, int? targetNodeId, int originalNodeId)
        {
            if (!_isDragging || _draggingUnitId != unitId)
            {
                return (false, false);
            }
            
            _isDragging = false;
            _draggingUnitId = null;
            
            bool success = false;
            bool stayInPlace = false;
            
            // Check if dropped on a valid move
            if (targetNodeId.HasValue && _validMoves.Contains(targetNodeId.Value))
            {
                int target = targetNodeId.Value;
                
                // Check if this is a capture-only or ranged-capture square
                bool isCaptureOnly = _selectedMoves?.CaptureOnly.Contains(target) ?? false;
                bool isRangedCapture = _selectedMoves?.IsRangedCapture(target) ?? false;
                bool canMoveToEmpty = _selectedMoves?.MoveOnly.Contains(target) ?? false;
                
                // For capture-only and ranged-capture squares, verify there's an enemy
                // UNLESS the node is also in MoveOnly (e.g., Crossbowman adjacent diagonals)
                if ((isCaptureOnly || isRangedCapture) && !canMoveToEmpty)
                {
                    bool hasEnemy = _offlineMode
                        ? _gameState?.Units.Exists(u => u.IsAlive && u.CurrentNodeId == target && u.OwnerId != _gameState.CurrentPlayerId) ?? false
                        : _networkedGameState?.GetAllUnits().Exists(u => u.IsAlive && u.CurrentNodeId == target && u.OwnerId != _networkedGameState.CurrentPlayerId.Value) ?? false;
                    
                    if (!hasEnemy)
                    {
                        // Can't move to capture-only square without enemy
                        success = false;
                    }
                    else if (_offlineMode)
                    {
                        ExecuteMove(unitId, target);
                        success = true;
                        stayInPlace = isRangedCapture; // Ranged capture = unit stays in place
                    }
                    else
                    {
                        _networkedGameState?.RequestMoveServerRpc(unitId, target);
                        success = true;
                        stayInPlace = isRangedCapture;
                    }
                }
                else
                {
                    // Normal move
                    if (_offlineMode)
                    {
                        ExecuteMove(unitId, target);
                        success = true;
                    }
                    else
                    {
                        _networkedGameState?.RequestMoveServerRpc(unitId, target);
                        success = true;
                    }
                }
            }
            
            // Network sync: notify other players
            if (!_offlineMode && _networkedGameState != null)
            {
                _networkedGameState.NotifyDragEndServerRpc(unitId, success, stayInPlace);
            }
            
            // Clear selection if move failed
            if (!success)
            {
                ClearSelection();
            }
            
            return (success, stayInPlace);
        }

        /// <summary>
        /// Called from network when another player starts dragging.
        /// </summary>
        public void OnRemoteDragStarted(int unitId)
        {
            if (_unitRenderers.TryGetValue(unitId, out var renderer))
            {
                renderer.StartDragRemote();
            }
        }

        /// <summary>
        /// Called from network when another player is dragging.
        /// </summary>
        public void OnRemoteDragUpdate(int unitId, Vector3 position)
        {
            if (_unitRenderers.TryGetValue(unitId, out var renderer))
            {
                renderer.UpdateDragPosition(position);
            }
        }

        /// <summary>
        /// Called from network when another player ends dragging.
        /// </summary>
        public void OnRemoteDragEnded(int unitId, bool success, bool stayInPlace)
        {
            if (_unitRenderers.TryGetValue(unitId, out var renderer))
            {
                // EndDragRemote(true) = don't snap back (piece will be moved by MoveTo)
                // EndDragRemote(false) = snap back to original position
                // For ranged captures: success but stayInPlace, so we need to snap back
                bool shouldMoveToNewPosition = success && !stayInPlace;
                renderer.EndDragRemote(shouldMoveToNewPosition);
            }
        }

        #endregion

        #region Game Actions

        private void ExecuteMove(int unitId, int targetNode)
        {
            // Use centralized move execution (same logic as network mode)
            var result = _gameState.TryExecuteFullMove(unitId, targetNode, _moveValidator, _gameState.CurrentPlayerId);
            
            if (!result.Success)
            {

                return;
            }

            // Handle Forcefield blocking the capture
            if (result.CaptureBlocked && result.ForcefieldConsumedUnitId.HasValue)
            {
                // Play forcefield break animation on the defender
                if (_unitRenderers.TryGetValue(result.ForcefieldConsumedUnitId.Value, out var defenderRenderer))
                {
                    defenderRenderer.PlayForcefieldBreakAnimation();
                }
                
                // Attacker bounces back to original position visually
                if (_unitRenderers.TryGetValue(unitId, out var attackerRenderer))
                {
                    attackerRenderer.MoveTo(GetWorldPosition(result.FromNode));
                }
                

            }
            else
            {
                // Handle capture visually (only if not blocked)
                if (result.IsCapture && result.CapturedUnitId.HasValue)
                {
                    if (_unitRenderers.TryGetValue(result.CapturedUnitId.Value, out var capturedRenderer))
                    {
                        capturedRenderer.PlayDeathAnimation();
                    }
                }

                // Handle item drop from captured unit
                if (!string.IsNullOrEmpty(result.DroppedItemId) && result.DroppedItemNodeId.HasValue)
                {
                    var droppedItem = _gameState.GetItem(result.DroppedItemId);
                    if (droppedItem != null)
                    {
                        SpawnDroppedItemRenderer(droppedItem);

                    }
                }

                // Animate - only move visually if NOT a ranged capture and NOT blocked
                if (!result.IsRangedCapture && _unitRenderers.TryGetValue(unitId, out var renderer))
                {
                    renderer.MoveTo(GetWorldPosition(result.ToNode));
                }
            }

            // Check if game ended
            if (result.GameEnded)
            {
                HandleGameEnd();
            }

            ClearSelection();

        }

        private void HandleGameEnd()
        {
            string message = _gameState.WinnerId.HasValue
                ? $"Player {_gameState.WinnerId.Value} wins by {_gameState.EndReason}!"
                : $"Game ended in {_gameState.EndReason}";
            

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
        /// Host a multiplayer game using Unity Relay.
        /// </summary>
        public async void HostGameWithRelay()
        {
            _offlineMode = false;
            SetupNetworkCallbacks();
            await _networkManager?.StartHostWithRelayAsync();
        }

        /// <summary>
        /// Join a multiplayer game using Unity Relay join code.
        /// </summary>
        public async void JoinGameWithRelay(string joinCode)
        {
            _offlineMode = false;
            SetupNetworkCallbacks();
            await _networkManager?.StartClientWithRelayAsync(joinCode);
        }

        /// <summary>
        /// Get the current profile service.
        /// </summary>
        public IProfileService GetProfileService() => _profileService;

        #endregion
    }
}

