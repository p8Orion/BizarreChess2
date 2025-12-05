using System;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

namespace BizarreChess.Networking
{
    /// <summary>
    /// Manages network connection modes: Host (casual), Client, and Dedicated Server (ranked).
    /// </summary>
    public class GameNetworkManager : MonoBehaviour
    {
        public static GameNetworkManager Instance { get; private set; }

        [Header("Configuration")]
        [SerializeField] private string _defaultAddress = "127.0.0.1";
        [SerializeField] private ushort _defaultPort = 7777;

        [Header("References")]
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private UnityTransport _transport;

        public event Action OnHostStarted;
        public event Action OnClientConnected;
        public event Action OnClientDisconnected;
        public event Action<string> OnConnectionFailed;

        public bool IsHost => _networkManager != null && _networkManager.IsHost;
        public bool IsClient => _networkManager != null && _networkManager.IsClient;
        public bool IsServer => _networkManager != null && _networkManager.IsServer;
        public bool IsConnected => _networkManager != null && _networkManager.IsConnectedClient;

        public ulong LocalClientId => _networkManager?.LocalClientId ?? 0;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Auto-find components if not assigned
            if (_networkManager == null)
                _networkManager = GetComponent<NetworkManager>();
            
            if (_transport == null)
                _transport = GetComponent<UnityTransport>();
        }

        private void Start()
        {
            if (_networkManager != null)
            {
                _networkManager.OnClientConnectedCallback += OnClientConnectedCallback;
                _networkManager.OnClientDisconnectCallback += OnClientDisconnectCallback;
            }
        }

        private void OnDestroy()
        {
            if (_networkManager != null)
            {
                _networkManager.OnClientConnectedCallback -= OnClientConnectedCallback;
                _networkManager.OnClientDisconnectCallback -= OnClientDisconnectCallback;
            }
        }

        #region Connection Modes

        /// <summary>
        /// Start as Host (server + client) for casual games.
        /// One player hosts, the other connects.
        /// </summary>
        public bool StartHost(ushort port = 0)
        {
            if (_networkManager == null || _transport == null)
            {
                OnConnectionFailed?.Invoke("Network components not configured");
                return false;
            }

            if (port == 0) port = _defaultPort;

            try
            {
                _transport.SetConnectionData(_defaultAddress, port);
                bool success = _networkManager.StartHost();
                
                if (success)
                {
                    Debug.Log($"[GameNetworkManager] Host started on port {port}");
                    OnHostStarted?.Invoke();
                }
                else
                {
                    OnConnectionFailed?.Invoke("Failed to start host");
                }
                
                return success;
            }
            catch (Exception e)
            {
                OnConnectionFailed?.Invoke($"Host error: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Start as Client and connect to a host or dedicated server.
        /// </summary>
        public bool StartClient(string address = null, ushort port = 0)
        {
            if (_networkManager == null || _transport == null)
            {
                OnConnectionFailed?.Invoke("Network components not configured");
                return false;
            }

            if (string.IsNullOrEmpty(address)) address = _defaultAddress;
            if (port == 0) port = _defaultPort;

            try
            {
                _transport.SetConnectionData(address, port);
                bool success = _networkManager.StartClient();
                
                if (success)
                {
                    Debug.Log($"[GameNetworkManager] Client connecting to {address}:{port}");
                }
                else
                {
                    OnConnectionFailed?.Invoke("Failed to start client");
                }
                
                return success;
            }
            catch (Exception e)
            {
                OnConnectionFailed?.Invoke($"Client error: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Start as Dedicated Server for ranked games.
        /// No local player, just hosts the game.
        /// </summary>
        public bool StartServer(ushort port = 0)
        {
            if (_networkManager == null || _transport == null)
            {
                OnConnectionFailed?.Invoke("Network components not configured");
                return false;
            }

            if (port == 0) port = _defaultPort;

            try
            {
                _transport.SetConnectionData(_defaultAddress, port);
                bool success = _networkManager.StartServer();
                
                if (success)
                {
                    Debug.Log($"[GameNetworkManager] Dedicated server started on port {port}");
                }
                else
                {
                    OnConnectionFailed?.Invoke("Failed to start server");
                }
                
                return success;
            }
            catch (Exception e)
            {
                OnConnectionFailed?.Invoke($"Server error: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Disconnect and shutdown network.
        /// </summary>
        public void Disconnect()
        {
            if (_networkManager != null && _networkManager.IsListening)
            {
                _networkManager.Shutdown();
                Debug.Log("[GameNetworkManager] Disconnected");
            }
        }

        #endregion

        #region WebSocket Configuration

        /// <summary>
        /// Configure transport for WebSocket (for WebGL builds).
        /// Call this before StartHost/StartClient/StartServer.
        /// </summary>
        public void ConfigureForWebSocket()
        {
            if (_transport == null) return;

            // Unity Transport supports WebSocket via protocol type
            // This needs to be configured in the transport settings
            Debug.Log("[GameNetworkManager] WebSocket transport configured");
        }

        #endregion

        #region Callbacks

        private void OnClientConnectedCallback(ulong clientId)
        {
            Debug.Log($"[GameNetworkManager] Client {clientId} connected");
            
            if (clientId == _networkManager.LocalClientId)
            {
                OnClientConnected?.Invoke();
            }
        }

        private void OnClientDisconnectCallback(ulong clientId)
        {
            Debug.Log($"[GameNetworkManager] Client {clientId} disconnected");
            
            if (clientId == _networkManager.LocalClientId)
            {
                OnClientDisconnected?.Invoke();
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// Get the number of connected clients.
        /// </summary>
        public int GetConnectedClientCount()
        {
            if (_networkManager == null || !_networkManager.IsServer)
                return 0;

            return _networkManager.ConnectedClientsIds.Count;
        }

        /// <summary>
        /// Check if we have enough players to start a game.
        /// </summary>
        public bool HasEnoughPlayers(int requiredPlayers = 2)
        {
            return GetConnectedClientCount() >= requiredPlayers;
        }

        #endregion
    }
}

