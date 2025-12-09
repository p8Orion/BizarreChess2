using System;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport.Relay;

namespace BizarreChess.Networking
{
    /// <summary>
    /// Manages network connection modes: Host (casual), Client, and Dedicated Server (ranked).
    /// Supports Unity Relay for NAT traversal and WebGL compatibility.
    /// </summary>
    public class GameNetworkManager : MonoBehaviour
    {
        public static GameNetworkManager Instance { get; private set; }

        [Header("Configuration")]
        [SerializeField] private string _defaultAddress = "127.0.0.1";
        [SerializeField] private ushort _defaultPort = 7777;
        [SerializeField] private int _maxRelayConnections = 2;

        [Header("References")]
        [SerializeField] private NetworkManager _networkManager;
        [SerializeField] private UnityTransport _transport;

        // Events
        public event Action OnHostStarted;
        public event Action OnClientConnected;
        public event Action OnClientDisconnected;
        public event Action<string> OnConnectionFailed;
        public event Action<string> OnRelayJoinCodeGenerated;

        // State
        public bool IsHost => _networkManager != null && _networkManager.IsHost;
        public bool IsClient => _networkManager != null && _networkManager.IsClient;
        public bool IsServer => _networkManager != null && _networkManager.IsServer;
        public bool IsConnected => _networkManager != null && _networkManager.IsConnectedClient;
        public ulong LocalClientId => _networkManager?.LocalClientId ?? 0;

        // Relay state
        public bool IsUsingRelay { get; private set; }
        public string CurrentJoinCode { get; private set; }
        public bool IsUnityServicesInitialized { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

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

        #region Unity Services Initialization

        /// <summary>
        /// Initialize Unity Services (required for Relay).
        /// Call this before using any Relay functionality.
        /// </summary>
        public async Task<bool> InitializeUnityServicesAsync()
        {
            if (IsUnityServicesInitialized)
                return true;

            try
            {
                await UnityServices.InitializeAsync();
                
                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                IsUnityServicesInitialized = true;
                Debug.Log($"[GameNetworkManager] Unity Services initialized. Player ID: {AuthenticationService.Instance.PlayerId}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameNetworkManager] Failed to initialize Unity Services: {e.Message}");
                OnConnectionFailed?.Invoke($"Unity Services error: {e.Message}");
                return false;
            }
        }

        #endregion

        #region Relay Connection Modes

        /// <summary>
        /// Start as Host using Unity Relay.
        /// Returns the join code for other players to connect.
        /// </summary>
        public async Task<string> StartHostWithRelayAsync()
        {
            if (_networkManager == null || _transport == null)
            {
                OnConnectionFailed?.Invoke("Network components not configured");
                return null;
            }

            if (!await InitializeUnityServicesAsync())
                return null;

            try
            {
                // Create Relay allocation
                var allocation = await RelayService.Instance.CreateAllocationAsync(_maxRelayConnections);
                
                // Get join code
                CurrentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
                
                // Configure transport with Relay data
                // Use "wss" for WebGL, "dtls" for other platforms
#if UNITY_WEBGL && !UNITY_EDITOR
                string connectionType = "wss";
                _transport.UseWebSockets = true;
#else
                string connectionType = "dtls";
#endif
                _transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, connectionType));

                // Start host
                bool success = _networkManager.StartHost();
                
                if (success)
                {
                    IsUsingRelay = true;
                    Debug.Log($"[GameNetworkManager] Relay Host started. Join Code: {CurrentJoinCode}");
                    OnHostStarted?.Invoke();
                    OnRelayJoinCodeGenerated?.Invoke(CurrentJoinCode);
                    return CurrentJoinCode;
                }
                else
                {
                    CurrentJoinCode = null;
                    OnConnectionFailed?.Invoke("Failed to start Relay host");
                    return null;
                }
            }
            catch (SessionException e)
            {
                Debug.LogError($"[GameNetworkManager] Relay error: {e.Message}");
                OnConnectionFailed?.Invoke($"Relay error: {e.Message}");
                return null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameNetworkManager] Host error: {e.Message}");
                OnConnectionFailed?.Invoke($"Host error: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Start as Client and connect to a Relay host using join code.
        /// </summary>
        public async Task<bool> StartClientWithRelayAsync(string joinCode)
        {
            if (_networkManager == null || _transport == null)
            {
                OnConnectionFailed?.Invoke("Network components not configured");
                return false;
            }

            if (string.IsNullOrEmpty(joinCode))
            {
                OnConnectionFailed?.Invoke("Join code is required");
                return false;
            }

            if (!await InitializeUnityServicesAsync())
                return false;

            try
            {
                // Join Relay allocation using code
                var joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
                
                // Configure transport with Relay data
                // Use "wss" for WebGL, "dtls" for other platforms
#if UNITY_WEBGL && !UNITY_EDITOR
                string connectionType = "wss";
                _transport.UseWebSockets = true;
#else
                string connectionType = "dtls";
#endif
                _transport.SetRelayServerData(AllocationUtils.ToRelayServerData(joinAllocation, connectionType));

                // Start client
                bool success = _networkManager.StartClient();
                
                if (success)
                {
                    IsUsingRelay = true;
                    CurrentJoinCode = joinCode;
                    Debug.Log($"[GameNetworkManager] Relay Client connecting with code: {joinCode}");
                    return true;
                }
                else
                {
                    OnConnectionFailed?.Invoke("Failed to start Relay client");
                    return false;
                }
            }
            catch (SessionException e)
            {
                Debug.LogError($"[GameNetworkManager] Relay error: {e.Message}");
                OnConnectionFailed?.Invoke($"Relay error: {e.Message}");
                return false;
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameNetworkManager] Client error: {e.Message}");
                OnConnectionFailed?.Invoke($"Client error: {e.Message}");
                return false;
            }
        }

        #endregion

        #region Direct Connection Modes (LAN/IP)

        /// <summary>
        /// Start as Host (server + client) for direct LAN connection.
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
                    IsUsingRelay = false;
                    Debug.Log($"[GameNetworkManager] Direct Host started on port {port}");
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
        /// Start as Client and connect directly to IP address.
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
                    IsUsingRelay = false;
                    Debug.Log($"[GameNetworkManager] Direct Client connecting to {address}:{port}");
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
                    IsUsingRelay = false;
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
                IsUsingRelay = false;
                CurrentJoinCode = null;
                Debug.Log("[GameNetworkManager] Disconnected");
            }
        }

        #endregion

        #region WebSocket Configuration

        /// <summary>
        /// Configure transport for WebSocket (for WebGL builds).
        /// Call this before StartHost/StartClient/StartServer.
        /// </summary>
        public void ConfigureForWebSocket(bool useSecure = false)
        {
            if (_transport == null) return;

            _transport.UseWebSockets = true;
            Debug.Log($"[GameNetworkManager] WebSocket transport configured (secure: {useSecure})");
        }

        /// <summary>
        /// Configure transport for standard UDP.
        /// </summary>
        public void ConfigureForUDP()
        {
            if (_transport == null) return;

            _transport.UseWebSockets = false;
            Debug.Log("[GameNetworkManager] UDP transport configured");
        }

        #endregion

        #region Callbacks

        private void OnClientConnectedCallback(ulong clientId)
        {
            Debug.Log($"[GameNetworkManager] Client {clientId} connected (Relay: {IsUsingRelay})");
            
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
