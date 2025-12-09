using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using BizarreChess.Core.Board;
using BizarreChess.Core.Units;
using BizarreChess.Core.Rules;
using BizarreChess.Core.Armies;
using BizarreChess.Core.Factories;
using BizarreChess.Core.Items;

namespace BizarreChess.Networking
{
    /// <summary>
    /// Networked game state - synchronized across all clients.
    /// Server is authoritative for all game logic.
    /// </summary>
    public class NetworkedGameState : NetworkBehaviour
    {
        [Header("Configuration")]
        [SerializeField] private BoardDefinition _boardDefinition;

        // Network variables (synchronized automatically)
        public NetworkVariable<int> CurrentTurn = new NetworkVariable<int>(1);
        public NetworkVariable<int> CurrentPlayerId = new NetworkVariable<int>(0);
        public NetworkVariable<GamePhaseNetwork> Phase = new NetworkVariable<GamePhaseNetwork>(GamePhaseNetwork.WaitingForPlayers);
        public NetworkVariable<int> WinnerId = new NetworkVariable<int>(-1);
        
        // Board seed for synchronized random generation (server-authoritative)
        public NetworkVariable<int> BoardSeed = new NetworkVariable<int>(0);

        // Local state (server builds this, clients receive via RPCs)
        private ChessSetup _chessSetup;
        private GameState _gameState;
        private BoardGraph _boardGraph;
        private MoveValidator _moveValidator;

        // Events
        public System.Action<int, int, int, bool> OnUnitMoved; // unitId, fromNode, toNode, isRangedCapture
        public System.Action<int> OnUnitCaptured; // unitId
        public System.Action<int, int, int> OnCaptureBlocked; // attackerUnitId, defenderUnitId, fromNode (attacker bounces back)
        public System.Action OnTurnChanged;
        public System.Action<int> OnGameEnded; // winnerId (-1 for draw)
        public System.Action OnGameStarted; // Called when game begins
        
        // Item events
        public System.Action<int, string, int> OnItemPickedUp; // unitId, itemId, nodeId
        public System.Action<string, int> OnItemDropped; // itemId, nodeId

        // Player mapping (clientId -> playerId)
        private Dictionary<ulong, int> _clientToPlayer = new Dictionary<ulong, int>();
        
        // Pending players waiting for game to start (used for randomization)
        private List<ulong> _pendingPlayers = new List<ulong>();

        #region Initialization

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Subscribe to network variable changes (clients)
            CurrentTurn.OnValueChanged += (old, newVal) => OnTurnChanged?.Invoke();
            CurrentPlayerId.OnValueChanged += (old, newVal) => OnTurnChanged?.Invoke();
            Phase.OnValueChanged += (old, newVal) => {
                Debug.Log($"[NetworkedGameState] Phase changed: {old} -> {newVal}");
                if (newVal == GamePhaseNetwork.Playing)
                    OnTurnChanged?.Invoke();
            };
            WinnerId.OnValueChanged += (old, newVal) => {
                if (newVal >= -1 && Phase.Value == GamePhaseNetwork.Ended)
                    OnGameEnded?.Invoke(newVal);
            };

            if (IsServer)
            {
                // Server generates the random seed for the board
                BoardSeed.Value = Random.Range(1, int.MaxValue);
                Debug.Log($"[NetworkedGameState] Server generated board seed: {BoardSeed.Value}");
                
                // Server initializes the chess setup with the seed
                InitializeChessSetup(BoardSeed.Value);
                
                // Server initializes the board graph
                InitializeBoard();
                
                // Subscribe to client connection events
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
                
                // Register the host as player 0
                OnPlayerJoined(NetworkManager.Singleton.LocalClientId);
            }
            
            Debug.Log($"[NetworkedGameState] Spawned. IsServer: {IsServer}, IsClient: {IsClient}");
        }
        
        /// <summary>
        /// Initialize the chess setup with a specific seed for reproducible board generation.
        /// </summary>
        private void InitializeChessSetup(int seed)
        {
            if (_boardDefinition != null)
            {
                // Use inspector-defined board (no randomness)
                _chessSetup = ChessFactory.CreateSetup(board: _boardDefinition);
            }
            else
            {
                // Use default setup with synchronized seed for abyss generation
                _chessSetup = ChessFactory.CreateDefaultSetupWithSeed(seed);
            }
            Debug.Log($"[NetworkedGameState] Chess setup initialized with seed {seed}");
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            
            if (IsServer && NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
        }

        private void OnClientConnected(ulong clientId)
        {
            Debug.Log($"[NetworkedGameState] Client connected: {clientId}");
            // Don't re-add the host
            if (clientId != NetworkManager.Singleton.LocalClientId)
            {
                OnPlayerJoined(clientId);
            }
        }

        private void OnClientDisconnected(ulong clientId)
        {
            Debug.Log($"[NetworkedGameState] Client disconnected: {clientId}");
        }

        private void InitializeBoard()
        {
            _boardGraph = new BoardGraph(_chessSetup.Board);
            _moveValidator = new MoveValidator(_boardGraph, _chessSetup.Pieces);
        }

        #endregion

        #region Game Setup (Server)

        /// <summary>
        /// Called when a client connects - add them to pending list.
        /// Slots are randomized when both players are ready.
        /// </summary>
        public void OnPlayerJoined(ulong clientId)
        {
            if (!IsServer) return;
            if (_pendingPlayers.Contains(clientId) || _clientToPlayer.ContainsKey(clientId)) return;
            if (_pendingPlayers.Count >= 2) return; // Already have 2 players

            _pendingPlayers.Add(clientId);
            Debug.Log($"[NetworkedGameState] Player {clientId} joined, waiting for opponent... ({_pendingPlayers.Count}/2)");

            // Check if we can start (2 players ready)
            if (_pendingPlayers.Count >= 2)
            {
                AssignRandomSlots();
                StartGame();
            }
        }
        
        /// <summary>
        /// Randomly assign player slots (0 = white/first, 1 = black/second).
        /// </summary>
        private void AssignRandomSlots()
        {
            if (_pendingPlayers.Count < 2) return;
            
            // Randomize who gets white (slot 0) and who gets black (slot 1)
            bool swapSlots = Random.value > 0.5f;
            
            int slot0 = swapSlots ? 1 : 0;
            int slot1 = swapSlots ? 0 : 1;
            
            _clientToPlayer[_pendingPlayers[0]] = slot0;
            _clientToPlayer[_pendingPlayers[1]] = slot1;
            
            Debug.Log($"[NetworkedGameState] Slots randomized: Client {_pendingPlayers[0]} -> slot {slot0}, Client {_pendingPlayers[1]} -> slot {slot1}");
            
            // Notify each client of their assigned slot
            foreach (var kvp in _clientToPlayer)
            {
                AssignPlayerClientRpc(kvp.Value, new ClientRpcParams
                {
                    Send = new ClientRpcSendParams
                    {
                        TargetClientIds = new[] { kvp.Key }
                    }
                });
            }
            
            _pendingPlayers.Clear();
        }

        [ClientRpc]
        private void AssignPlayerClientRpc(int playerId, ClientRpcParams clientRpcParams = default)
        {
            Debug.Log($"[NetworkedGameState] Assigned as player {playerId}");
            // Store locally for this client
            LocalPlayerId = playerId;
        }

        public int LocalPlayerId { get; private set; } = -1;

        /// <summary>
        /// Start the game with both players ready.
        /// </summary>
        private void StartGame()
        {
            if (!IsServer) return;

            // Create player setups with their armies
            var playerSetups = new List<PlayerSetup>
            {
                new PlayerSetup { DisplayName = "Player 1", Army = _chessSetup.GetArmy(0) },
                new PlayerSetup { DisplayName = "Player 2", Army = _chessSetup.GetArmy(1) }
            };

            _gameState = new GameState();
            _gameState.Initialize(_chessSetup.Board, playerSetups);

            // Add items from setup
            AddItemsFromSetup(_chessSetup.Items);

            // Update network variables
            Phase.Value = GamePhaseNetwork.Playing;
            CurrentTurn.Value = _gameState.TurnNumber;
            CurrentPlayerId.Value = _gameState.CurrentPlayerId;

            // Send initial state to all clients
            SyncInitialStateClientRpc();
        }

        /// <summary>
        /// Add items from the chess setup to the game state.
        /// </summary>
        private void AddItemsFromSetup(List<Item> items)
        {
            if (items == null || _gameState == null) return;

            foreach (var item in items)
            {
                var clonedItem = item.Clone();
                _gameState.AddItem(clonedItem);
                Debug.Log($"[NetworkedGameState] Added item '{clonedItem.DisplayName}' at node {clonedItem.NodeId}");
            }
        }

        [ClientRpc]
        private void SyncInitialStateClientRpc()
        {
            Debug.Log("[NetworkedGameState] Game started! Notifying listeners...");
            
            // Initialize local game state for clients too
            if (!IsServer)
            {
                // Client must use the same seed as the server to generate identical board
                int seed = BoardSeed.Value;
                Debug.Log($"[NetworkedGameState] Client using server's board seed: {seed}");
                
                // Initialize chess setup with server's seed
                InitializeChessSetup(seed);
                
                var playerSetups = new List<PlayerSetup>
                {
                    new PlayerSetup { DisplayName = "Player 1", Army = _chessSetup.GetArmy(0) },
                    new PlayerSetup { DisplayName = "Player 2", Army = _chessSetup.GetArmy(1) }
                };
                
                _gameState = new GameState();
                _gameState.Initialize(_chessSetup.Board, playerSetups);
                _boardGraph = new BoardGraph(_chessSetup.Board);
                _moveValidator = new MoveValidator(_boardGraph, _chessSetup.Pieces);

                // Add items from setup (same as server)
                AddItemsFromSetup(_chessSetup.Items);
            }
            
            OnGameStarted?.Invoke();
        }

        #endregion

        #region Player Actions (Client -> Server)

        /// <summary>
        /// Client requests to move a unit.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestMoveServerRpc(int unitId, int targetNode, ServerRpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            
            if (!_clientToPlayer.TryGetValue(clientId, out int playerId))
            {
                Debug.LogWarning($"[NetworkedGameState] Unknown client {clientId} tried to move");
                return;
            }

            // Use centralized move execution (same logic as offline mode)
            var result = _gameState.TryExecuteFullMove(unitId, targetNode, _moveValidator, playerId);
            
            if (!result.Success)
            {
                SendErrorToClient(result.Error, clientId);
                return;
            }

            Debug.Log($"[NetworkedGameState] Move executed: Unit {unitId} from {result.FromNode} to {result.ToNode}, isRangedCapture={result.IsRangedCapture}, captureBlocked={result.CaptureBlocked}");

            // Check if capture was blocked by forcefield
            if (result.CaptureBlocked && result.ForcefieldConsumedUnitId.HasValue)
            {
                // Broadcast capture blocked - attacker bounces back
                BroadcastCaptureBlockedClientRpc(unitId, result.ForcefieldConsumedUnitId.Value, result.FromNode);
            }
            else
            {
                // Normal move - notify all clients
                BroadcastMoveClientRpc(unitId, result.FromNode, result.ToNode, result.IsRangedCapture);

                if (result.IsCapture && result.CapturedUnitId.HasValue)
                {
                    BroadcastCaptureClientRpc(result.CapturedUnitId.Value);
                    
                    // Broadcast item drop if captured unit had an item
                    if (!string.IsNullOrEmpty(result.DroppedItemId) && result.DroppedItemNodeId.HasValue)
                    {
                        BroadcastItemDroppedClientRpc(result.DroppedItemId, result.DroppedItemNodeId.Value);
                    }
                }
            }

            // Update network state
            if (result.GameEnded)
            {
                Phase.Value = GamePhaseNetwork.Ended;
                WinnerId.Value = result.WinnerId ?? -1;
            }
            else
            {
                // Update network variables
                CurrentTurn.Value = result.NewTurnNumber;
                CurrentPlayerId.Value = result.NewCurrentPlayerId;
                
                // Notify clients to sync their turn state
                BroadcastTurnEndClientRpc();
                
                Debug.Log($"[NetworkedGameState] Turn ended. Now Turn {CurrentTurn.Value}, Player {CurrentPlayerId.Value}'s turn");
            }
        }

        /// <summary>
        /// Client requests to end their turn.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestEndTurnServerRpc(ServerRpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            
            if (!_clientToPlayer.TryGetValue(clientId, out int playerId))
                return;

            if (playerId != _gameState.CurrentPlayerId)
            {
                SendErrorToClient("Not your turn", clientId);
                return;
            }

            // End turn
            _gameState.EndTurn();

            // Update network variables
            CurrentTurn.Value = _gameState.TurnNumber;
            CurrentPlayerId.Value = _gameState.CurrentPlayerId;

            BroadcastTurnEndClientRpc();
        }

        /// <summary>
        /// Client requests to resign.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestResignServerRpc(ServerRpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            
            if (!_clientToPlayer.TryGetValue(clientId, out int playerId))
                return;

            // Other player wins
            int winnerId = playerId == 0 ? 1 : 0;
            
            _gameState.Phase = GamePhase.Ended;
            _gameState.WinnerId = winnerId;
            _gameState.EndReason = GameEndReason.Resignation;

            Phase.Value = GamePhaseNetwork.Ended;
            WinnerId.Value = winnerId;
        }

        /// <summary>
        /// Client requests to pick up an item.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void RequestItemPickupServerRpc(int unitId, ServerRpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            
            if (!_clientToPlayer.TryGetValue(clientId, out int playerId))
            {
                Debug.LogWarning($"[NetworkedGameState] Unknown client {clientId} tried to pick up item");
                return;
            }

            // Use centralized pickup execution
            var result = _gameState.TryExecuteItemPickup(unitId, playerId);
            
            if (!result.Success)
            {
                SendErrorToClient(result.Error, clientId);
                return;
            }

            Debug.Log($"[NetworkedGameState] Item pickup executed: Unit {unitId} picked up {result.ItemId}");

            // Broadcast to all clients
            BroadcastItemPickedUpClientRpc(unitId, result.ItemId, result.NodeId);

            // Update network variables
            CurrentTurn.Value = result.NewTurnNumber;
            CurrentPlayerId.Value = result.NewCurrentPlayerId;
            
            BroadcastTurnEndClientRpc();
        }

        #endregion

        #region Drag Synchronization

        // Events for drag synchronization
        public System.Action<int> OnDragStarted; // unitId
        public System.Action<int, Vector3> OnDragUpdated; // unitId, position
        public System.Action<int, bool, bool> OnDragEnded; // unitId, success, stayInPlace

        /// <summary>
        /// Client notifies server that they started dragging a piece.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void NotifyDragStartServerRpc(int unitId, ServerRpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            
            // Validate it's this player's turn and unit
            if (!_clientToPlayer.TryGetValue(clientId, out int playerId))
                return;
            if (playerId != _gameState.CurrentPlayerId)
                return;
            
            var unit = _gameState.GetUnit(unitId);
            if (unit == null || unit.OwnerId != playerId)
                return;
            
            // Broadcast to other clients
            BroadcastDragStartClientRpc(unitId, clientId);
        }

        /// <summary>
        /// Client sends updated drag position to server.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void UpdateDragPositionServerRpc(int unitId, Vector3 position, ServerRpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            
            // Basic validation
            if (!_clientToPlayer.TryGetValue(clientId, out int playerId))
                return;
            if (playerId != _gameState.CurrentPlayerId)
                return;
            
            // Broadcast to other clients
            BroadcastDragPositionClientRpc(unitId, position, clientId);
        }

        /// <summary>
        /// Client notifies server that they ended dragging.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        public void NotifyDragEndServerRpc(int unitId, bool success, bool stayInPlace = false, ServerRpcParams rpcParams = default)
        {
            ulong clientId = rpcParams.Receive.SenderClientId;
            
            // Broadcast to other clients
            BroadcastDragEndClientRpc(unitId, success, stayInPlace, clientId);
        }

        [ClientRpc]
        private void BroadcastDragStartClientRpc(int unitId, ulong senderClientId)
        {
            // Don't notify the sender
            if (NetworkManager.Singleton.LocalClientId == senderClientId)
                return;
            
            OnDragStarted?.Invoke(unitId);
        }

        [ClientRpc]
        private void BroadcastDragPositionClientRpc(int unitId, Vector3 position, ulong senderClientId)
        {
            // Don't notify the sender
            if (NetworkManager.Singleton.LocalClientId == senderClientId)
                return;
            
            OnDragUpdated?.Invoke(unitId, position);
        }

        [ClientRpc]
        private void BroadcastDragEndClientRpc(int unitId, bool success, bool stayInPlace, ulong senderClientId)
        {
            // Don't notify the sender
            if (NetworkManager.Singleton.LocalClientId == senderClientId)
                return;
            
            OnDragEnded?.Invoke(unitId, success, stayInPlace);
        }

        #endregion

        #region Server -> Client Broadcasts

        [ClientRpc]
        private void BroadcastMoveClientRpc(int unitId, int fromNode, int toNode, bool isRangedCapture = false)
        {
            Debug.Log($"[NetworkedGameState] BroadcastMove received: Unit {unitId} from {fromNode} to {toNode}, isRangedCapture={isRangedCapture}");
            
            // Update local game state on clients (server already updated)
            if (!IsServer && _gameState != null)
            {
                var unit = _gameState.GetUnit(unitId);
                if (unit != null)
                {
                    // Only update position if NOT a ranged capture
                    if (!isRangedCapture)
                    {
                        unit.CurrentNodeId = toNode;
                    }
                    unit.HasMovedThisTurn = true;
                    unit.HasEverMoved = true;
                }
            }
            
            OnUnitMoved?.Invoke(unitId, fromNode, toNode, isRangedCapture);
        }

        [ClientRpc]
        private void BroadcastCaptureClientRpc(int unitId)
        {
            OnUnitCaptured?.Invoke(unitId);
        }

        [ClientRpc]
        private void BroadcastCaptureBlockedClientRpc(int attackerUnitId, int defenderUnitId, int fromNode)
        {
            Debug.Log($"[NetworkedGameState] Capture blocked! Attacker {attackerUnitId} bounces back to {fromNode}, defender {defenderUnitId}'s forcefield consumed");
            
            // Update local game state on clients - remove forcefield from defender
            if (!IsServer && _gameState != null)
            {
                var defender = _gameState.GetUnit(defenderUnitId);
                if (defender != null)
                {
                    defender.RemoveSkill("Forcefield");
                }
                
                // Mark attacker as having acted this turn
                var attacker = _gameState.GetUnit(attackerUnitId);
                if (attacker != null)
                {
                    attacker.HasMovedThisTurn = true;
                    attacker.HasActedThisTurn = true;
                }
            }
            
            OnCaptureBlocked?.Invoke(attackerUnitId, defenderUnitId, fromNode);
        }

        [ClientRpc]
        private void BroadcastTurnEndClientRpc()
        {
            Debug.Log($"[NetworkedGameState] Turn end broadcast received. Current player: {CurrentPlayerId.Value}");
            
            // Sync local game state on clients
            if (!IsServer && _gameState != null)
            {
                _gameState.EndTurn();
            }
            
            OnTurnChanged?.Invoke();
        }

        [ClientRpc]
        private void BroadcastItemPickedUpClientRpc(int unitId, string itemId, int nodeId)
        {
            Debug.Log($"[NetworkedGameState] Item picked up: Unit {unitId} picked up {itemId} at node {nodeId}");
            
            // Update local game state on clients (server already updated)
            if (!IsServer && _gameState != null)
            {
                var unit = _gameState.GetUnit(unitId);
                var item = _gameState.GetItem(itemId);
                if (unit != null && item != null)
                {
                    _gameState.RemoveItem(item);
                    unit.PickUpItem(item);
                    unit.HasActedThisTurn = true;
                }
            }
            
            OnItemPickedUp?.Invoke(unitId, itemId, nodeId);
        }

        [ClientRpc]
        private void BroadcastItemDroppedClientRpc(string itemId, int nodeId)
        {
            Debug.Log($"[NetworkedGameState] Item dropped: {itemId} at node {nodeId}");
            
            // Note: Local game state is updated in move execution, this is just for visual sync
            OnItemDropped?.Invoke(itemId, nodeId);
        }

        private void SendErrorToClient(string error, ulong clientId)
        {
            NotifyErrorClientRpc(error, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            });
        }

        [ClientRpc]
        private void NotifyErrorClientRpc(string error, ClientRpcParams clientRpcParams = default)
        {
            Debug.LogWarning($"[NetworkedGameState] Server error: {error}");
        }

        #endregion

        #region Queries (for UI/Presentation)

        /// <summary>
        /// Get valid moves for a unit (client-side prediction or server validation).
        /// </summary>
        public List<int> GetValidMovesForUnit(int unitId)
        {
            return GetCategorizedMovesForUnit(unitId)?.GetAll() ?? new List<int>();
        }

        /// <summary>
        /// Get categorized valid moves for a unit (move-only, capture-only, both).
        /// </summary>
        public MoveTargets GetCategorizedMovesForUnit(int unitId)
        {
            if (_gameState == null || _moveValidator == null)
                return new MoveTargets();

            var unit = _gameState.GetUnit(unitId);
            if (unit == null)
                return new MoveTargets();

            return _moveValidator.GetCategorizedMovesForUnit(unit, _gameState.Units);
        }

        /// <summary>
        /// Check if it's the local player's turn.
        /// </summary>
        public bool IsMyTurn()
        {
            return LocalPlayerId == CurrentPlayerId.Value;
        }

        /// <summary>
        /// Get all units (for rendering).
        /// </summary>
        public List<UnitState> GetAllUnits()
        {
            return _gameState?.GetAliveUnits() ?? new List<UnitState>();
        }

        /// <summary>
        /// Get the board graph (for rendering).
        /// </summary>
        public BoardGraph GetBoardGraph()
        {
            return _boardGraph;
        }

        /// <summary>
        /// Get piece definitions.
        /// </summary>
        public Dictionary<string, UnitDefinition> GetPieces()
        {
            return _chessSetup?.Pieces;
        }

        /// <summary>
        /// Get the game state (for item rendering and queries).
        /// </summary>
        public GameState GetGameState()
        {
            return _gameState;
        }

        #endregion
    }

    /// <summary>
    /// Network-friendly game phase enum.
    /// </summary>
    public enum GamePhaseNetwork : byte
    {
        WaitingForPlayers,
        Playing,
        Ended
    }
}

