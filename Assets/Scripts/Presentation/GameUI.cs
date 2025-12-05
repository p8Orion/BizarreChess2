using UnityEngine;
using UnityEngine.UI;
using TMPro;
using BizarreChess.Networking;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Main game UI - connection menu, turn indicator, game over screen.
    /// </summary>
    public class GameUI : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] private GameObject _mainMenuPanel;
        [SerializeField] private GameObject _gamePanel;
        [SerializeField] private GameObject _gameOverPanel;

        [Header("Main Menu")]
        [SerializeField] private Button _hostButton;
        [SerializeField] private Button _joinButton;
        [SerializeField] private Button _offlineButton;
        [SerializeField] private TMP_InputField _addressInput;

        [Header("Game HUD")]
        [SerializeField] private TextMeshProUGUI _turnText;
        [SerializeField] private TextMeshProUGUI _statusText;
        [SerializeField] private Button _resignButton;

        [Header("Game Over")]
        [SerializeField] private TextMeshProUGUI _resultText;
        [SerializeField] private Button _rematchButton;
        [SerializeField] private Button _mainMenuButton;

        private GameManager _gameManager;
        private GameNetworkManager _networkManager;
        private NetworkedGameState _networkedState;

        private void Start()
        {
            _gameManager = FindFirstObjectByType<GameManager>();
            _networkManager = FindFirstObjectByType<GameNetworkManager>();
            _networkedState = FindFirstObjectByType<NetworkedGameState>();

            CreateUIIfNeeded();
            SetupCallbacks();
            ShowMainMenu();
        }

        private void CreateUIIfNeeded()
        {
            if (_mainMenuPanel == null)
            {
                CreateMainMenuUI();
            }
            if (_gamePanel == null)
            {
                CreateGameUI();
            }
            if (_gameOverPanel == null)
            {
                CreateGameOverUI();
            }
        }

        #region UI Creation

        private void CreateMainMenuUI()
        {
            _mainMenuPanel = CreatePanel("MainMenuPanel");

            var title = CreateText(_mainMenuPanel.transform, "Bizarre Chess", 48, new Vector2(0, 150));
            
            _offlineButton = CreateButton(_mainMenuPanel.transform, "Play Offline", new Vector2(0, 50));
            _hostButton = CreateButton(_mainMenuPanel.transform, "Host Game", new Vector2(0, -10));
            _joinButton = CreateButton(_mainMenuPanel.transform, "Join Game", new Vector2(0, -70));
            
            _addressInput = CreateInputField(_mainMenuPanel.transform, "127.0.0.1", new Vector2(0, -130));
        }

        private void CreateGameUI()
        {
            _gamePanel = CreatePanel("GamePanel");
            _gamePanel.GetComponent<Image>().enabled = false; // Transparent background

            // Top center - turn info
            _turnText = CreateText(_gamePanel.transform, "Turn 1 - Player 1", 28, new Vector2(0, -40));
            _turnText.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 1f);
            _turnText.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1f);
            
            _statusText = CreateText(_gamePanel.transform, "", 18, new Vector2(0, -80));
            _statusText.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 1f);
            _statusText.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 1f);
            
            // Bottom right corner - resign button
            _resignButton = CreateButton(_gamePanel.transform, "Resign", new Vector2(-80, 40));
            var resignRect = _resignButton.GetComponent<RectTransform>();
            resignRect.anchorMin = new Vector2(1f, 0f);
            resignRect.anchorMax = new Vector2(1f, 0f);
            resignRect.sizeDelta = new Vector2(120, 40);
        }

        private void CreateGameOverUI()
        {
            _gameOverPanel = CreatePanel("GameOverPanel");

            _resultText = CreateText(_gameOverPanel.transform, "Game Over", 48, new Vector2(0, 50));
            
            _rematchButton = CreateButton(_gameOverPanel.transform, "Rematch", new Vector2(-80, -50));
            _mainMenuButton = CreateButton(_gameOverPanel.transform, "Main Menu", new Vector2(80, -50));
        }

        private GameObject CreatePanel(string name)
        {
            var panel = new GameObject(name);
            panel.transform.SetParent(transform, false);
            
            var rect = panel.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            var image = panel.AddComponent<Image>();
            image.color = new Color(0, 0, 0, 0.8f);

            return panel;
        }

        private TextMeshProUGUI CreateText(Transform parent, string text, int fontSize, Vector2 position)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(400, 60);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return tmp;
        }

        private Button CreateButton(Transform parent, string text, Vector2 position)
        {
            var go = new GameObject("Button_" + text);
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(200, 50);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.3f, 0.3f, 0.4f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;

            var colors = button.colors;
            colors.highlightedColor = new Color(0.4f, 0.4f, 0.6f);
            colors.pressedColor = new Color(0.2f, 0.2f, 0.3f);
            button.colors = colors;

            // Add text child
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);

            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = 20;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;

            return button;
        }

        private TMP_InputField CreateInputField(Transform parent, string placeholder, Vector2 position)
        {
            var go = new GameObject("InputField");
            go.transform.SetParent(parent, false);

            var rect = go.AddComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(200, 40);

            var image = go.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.25f);

            // Text area
            var textArea = new GameObject("TextArea");
            textArea.transform.SetParent(go.transform, false);
            var textAreaRect = textArea.AddComponent<RectTransform>();
            textAreaRect.anchorMin = Vector2.zero;
            textAreaRect.anchorMax = Vector2.one;
            textAreaRect.offsetMin = new Vector2(10, 5);
            textAreaRect.offsetMax = new Vector2(-10, -5);

            // Text
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(textArea.transform, false);
            var textRect = textGO.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.fontSize = 18;
            text.color = Color.white;

            // Placeholder
            var placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(textArea.transform, false);
            var phRect = placeholderGO.AddComponent<RectTransform>();
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = Vector2.zero;
            phRect.offsetMax = Vector2.zero;

            var phText = placeholderGO.AddComponent<TextMeshProUGUI>();
            phText.text = placeholder;
            phText.fontSize = 18;
            phText.color = new Color(0.5f, 0.5f, 0.5f);
            phText.fontStyle = FontStyles.Italic;

            var input = go.AddComponent<TMP_InputField>();
            input.textComponent = text;
            input.placeholder = phText;
            input.textViewport = textAreaRect;
            input.text = placeholder;

            return input;
        }

        #endregion

        #region Callbacks

        private void SetupCallbacks()
        {
            if (_offlineButton != null)
                _offlineButton.onClick.AddListener(OnOfflineClicked);
            
            if (_hostButton != null)
                _hostButton.onClick.AddListener(OnHostClicked);
            
            if (_joinButton != null)
                _joinButton.onClick.AddListener(OnJoinClicked);

            if (_resignButton != null)
                _resignButton.onClick.AddListener(OnResignClicked);

            if (_rematchButton != null)
                _rematchButton.onClick.AddListener(OnRematchClicked);

            if (_mainMenuButton != null)
                _mainMenuButton.onClick.AddListener(ShowMainMenu);

            // Game events
            if (_gameManager != null)
            {
                _gameManager.OnGameEnded += OnGameEnded;
            }

            SubscribeToNetworkEvents();
        }

        private void SubscribeToNetworkEvents()
        {
            if (_networkedState == null)
                _networkedState = FindFirstObjectByType<NetworkedGameState>();
                
            if (_networkedState != null)
            {
                _networkedState.OnTurnChanged += UpdateTurnDisplay;
                _networkedState.OnGameStarted += OnNetworkGameStarted;
            }
        }

        private void OnNetworkGameStarted()
        {
            Debug.Log("[GameUI] Network game started!");
            _statusText.text = "Game started!";
            UpdateTurnDisplay();
        }

        private void OnOfflineClicked()
        {
            ShowGamePanel();
            _gameManager?.NewOfflineGame();
            _statusText.text = "Offline Mode";
        }

        private void OnHostClicked()
        {
            ShowGamePanel();
            _gameManager?.HostGame();
            _statusText.text = "Hosting... Waiting for opponent";
        }

        private void OnJoinClicked()
        {
            string address = _addressInput?.text ?? "127.0.0.1";
            ShowGamePanel();
            _gameManager?.JoinGame(address);
            _statusText.text = $"Connecting to {address}...";
        }

        private void OnResignClicked()
        {
            _networkedState?.RequestResignServerRpc();
        }

        private void OnRematchClicked()
        {
            // For now, just start new offline game
            ShowGamePanel();
            _gameManager?.NewOfflineGame();
        }

        private void OnGameEnded(int winnerId, int localPlayerId)
        {
            ShowGameOver();
            
            if (winnerId == -1)
            {
                _resultText.text = "Draw!";
            }
            else if (winnerId == localPlayerId)
            {
                _resultText.text = "You Win!";
            }
            else
            {
                _resultText.text = "You Lose!";
            }
        }

        private void UpdateTurnDisplay()
        {
            if (_networkedState != null)
            {
                int turn = _networkedState.CurrentTurn.Value;
                int playerId = _networkedState.CurrentPlayerId.Value;
                bool isMyTurn = _networkedState.IsMyTurn();

                _turnText.text = $"Turn {turn} - Player {playerId + 1}";
                _statusText.text = isMyTurn ? "Your turn!" : "Opponent's turn";
                _turnText.color = isMyTurn ? Color.green : Color.white;
            }
        }

        #endregion

        #region Panel Management

        private void ShowMainMenu()
        {
            _mainMenuPanel?.SetActive(true);
            _gamePanel?.SetActive(false);
            _gameOverPanel?.SetActive(false);
        }

        private void ShowGamePanel()
        {
            _mainMenuPanel?.SetActive(false);
            _gamePanel?.SetActive(true);
            _gameOverPanel?.SetActive(false);
            _turnText.text = "Turn 1 - Player 1";
        }

        private void ShowGameOver()
        {
            _mainMenuPanel?.SetActive(false);
            _gamePanel?.SetActive(false);
            _gameOverPanel?.SetActive(true);
        }

        #endregion

        private void Update()
        {
            // Update turn display for offline mode
            if (_gamePanel != null && _gamePanel.activeSelf && _gameManager != null)
            {
                // Could poll game state here if needed
            }
        }
    }
}

