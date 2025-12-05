using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using BizarreChess.Core.Graph;
using BizarreChess.Core.Units;
using BizarreChess.Core.Rules;
using BizarreChess.Core.Armies;
using BizarreChess.Core.Factories;

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

        // Local state (server builds this, clients receive via RPCs)
        private GameState _gameState;
        private BoardGraph _boardGraph;
        private MoveValidator _moveValidator;
        private Dictionary<string, UnitDefinition> _unitDefinitions;

        // Events
        public System.Action<int, int, int> OnUnitMoved; // unitId, fromNode, toNode
        public System.Action<int> OnUnitCaptured; // unitId
        public System.Action OnTurnChanged;
        public System.Action<int> OnGameEnded; // winnerId (-1 for draw)
        public System.Action OnGameStarted; // Called when game begins

        // Player mapping (clientId -> playerId)
        private Dictionary<ulong, int> _clientToPlayer = new Dictionary<ulong, int>();

        #region Initialization

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            // Initialize unit definitions
            _unitDefinitions = ClassicChessFactory.CreateAllUnitDefinitions();

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
                // Server initializes the board
                InitializeBoard();
                
                // Subscribe to client connection events
                NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
                
                // Register the host as player 0
                OnPlayerJoined(NetworkManager.Singleton.LocalClientId);
            }
            
            Debug.Log($"[NetworkedGameState] Spawned. IsServer: {IsServer}, IsClient: {IsClient}");
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
            if (_boardDefinition == null)
            {
                // Use classic board as default
                _boardDefinition = ClassicChessFactory.CreateClassicBoard();
            }

            _boardGraph = new BoardGraph(_boardDefinition);
            _moveValidator = new MoveValidator(_boardGraph, _unitDefinitions);
        }

        #endregion

        #region Game Setup (Server)

        /// <summary>
        /// Called when a client connects - assign them a player slot.
        /// </summary>
        public void OnPlayerJoined(ulong clientId)
        {
            if (!IsServer) return;

            int playerSlot = _clientToPlayer.Count;
            if (playerSlot >= 2) return; // Already have 2 players

            _clientToPlayer[clientId] = playerSlot;
            Debug.Log($"[NetworkedGameState] Player {clientId} assigned to slot {playerSlot}");

            // Notify client of their player ID
            AssignPlayerClientRpc(playerSlot, new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new[] { clientId }
                }
            });

            // Check if we can start
            if (_clientToPlayer.Count >= 2)
            {
                StartGame();
            }
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

            // Create armies for both players
            var classicArmy = ClassicChessFactory.CreateClassicArmy(_unitDefinitions);

            var playerSetups = new List<PlayerSetup>
            {
                new PlayerSetup { DisplayName = "Player 1", Army = classicArmy },
                new PlayerSetup { DisplayName = "Player 2", Army = classicArmy }
            };

            _gameState = new GameState();
            _gameState.Initialize(_boardDefinition, playerSetups);

            // Update network variables
            Phase.Value = GamePhaseNetwork.Playing;
            CurrentTurn.Value = _gameState.TurnNumber;
            CurrentPlayerId.Value = _gameState.CurrentPlayerId;

            // Send initial state to all clients
            SyncInitialStateClientRpc();
        }

        [ClientRpc]
        private void SyncInitialStateClientRpc()
        {
            Debug.Log("[NetworkedGameState] Game started! Notifying listeners...");
            
            // Initialize local game state for clients too
            if (!IsServer)
            {
                var classicArmy = ClassicChessFactory.CreateClassicArmy(_unitDefinitions);
                var playerSetups = new List<PlayerSetup>
                {
                    new PlayerSetup { DisplayName = "Player 1", Army = classicArmy },
                    new PlayerSetup { DisplayName = "Player 2", Army = classicArmy }
                };
                
                _gameState = new GameState();
                _gameState.Initialize(_boardDefinition ?? ClassicChessFactory.CreateClassicBoard(), playerSetups);
                _boardGraph = new BoardGraph(_gameState.BoardState != null ? 
                    ClassicChessFactory.CreateClassicBoard() : _boardDefinition ?? ClassicChessFactory.CreateClassicBoard());
                _moveValidator = new MoveValidator(_boardGraph, _unitDefinitions);
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

            // Validate it's this player's turn
            if (playerId != _gameState.CurrentPlayerId)
            {
                SendErrorToClient("Not your turn", clientId);
                return;
            }

            // Get unit
            var unit = _gameState.GetUnit(unitId);
            if (unit == null)
            {
                SendErrorToClient("Unit not found", clientId);
                return;
            }

            // Validate move
            var result = _moveValidator.ValidateMove(unit, targetNode, _gameState.Units, playerId);
            if (!result.IsValid)
            {
                SendErrorToClient(result.Error, clientId);
                return;
            }

            // Execute move
            int fromNode = unit.CurrentNodeId;
            _gameState.ExecuteMove(unitId, targetNode, result.IsCapture, result.CapturedUnitId);

            Debug.Log($"[NetworkedGameState] Move executed: Unit {unitId} from {fromNode} to {targetNode}");

            // Notify all clients
            BroadcastMoveClientRpc(unitId, fromNode, targetNode);

            if (result.IsCapture && result.CapturedUnitId.HasValue)
            {
                BroadcastCaptureClientRpc(result.CapturedUnitId.Value);
            }

            // Check win conditions
            _gameState.CheckWinConditions(_moveValidator);

            if (_gameState.Phase == GamePhase.Ended)
            {
                Phase.Value = GamePhaseNetwork.Ended;
                WinnerId.Value = _gameState.WinnerId ?? -1;
            }
            else
            {
                // End turn and switch player
                _gameState.EndTurn();
                
                // Update network variables
                CurrentTurn.Value = _gameState.TurnNumber;
                CurrentPlayerId.Value = _gameState.CurrentPlayerId;
                
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

        #endregion

        #region Server -> Client Broadcasts

        [ClientRpc]
        private void BroadcastMoveClientRpc(int unitId, int fromNode, int toNode)
        {
            Debug.Log($"[NetworkedGameState] BroadcastMove received: Unit {unitId} from {fromNode} to {toNode}");
            
            // Update local game state on clients (server already updated)
            if (!IsServer && _gameState != null)
            {
                var unit = _gameState.GetUnit(unitId);
                if (unit != null)
                {
                    unit.CurrentNodeId = toNode;
                    unit.HasMovedThisTurn = true;
                    unit.HasEverMoved = true;
                }
            }
            
            OnUnitMoved?.Invoke(unitId, fromNode, toNode);
        }

        [ClientRpc]
        private void BroadcastCaptureClientRpc(int unitId)
        {
            OnUnitCaptured?.Invoke(unitId);
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
            if (_gameState == null || _moveValidator == null)
                return new List<int>();

            var unit = _gameState.GetUnit(unitId);
            if (unit == null)
                return new List<int>();

            if (!_unitDefinitions.TryGetValue(unit.DefinitionId, out var definition))
                return new List<int>();

            return _moveValidator.GetValidMovesForUnit(unit, definition, _gameState.Units);
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

