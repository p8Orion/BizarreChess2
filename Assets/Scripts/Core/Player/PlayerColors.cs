using UnityEngine;

namespace BizarreChess.Core.Player
{
    /// <summary>
    /// Centralized player color and texture configuration.
    /// All visual systems should derive their appearance from here.
    /// </summary>
    [System.Serializable]
    public class PlayerColorScheme
    {
        public Color PrimaryColor;      // Main piece color (tint)
        public Color SecondaryColor;    // Accent/detail color
        public Color SelectHighlight;   // Selection highlight (derived, more saturated)
        public Color HoverHighlight;    // Hover highlight (derived, more transparent)
        
        // Texture settings
        public Texture2D PieceTexture;      // Main texture for pieces (albedo)
        public float TextureTiling = 2f;    // How much the texture tiles
        public float Smoothness = 0.5f;     // Material smoothness

        public PlayerColorScheme(Color primary, Color secondary, Texture2D texture = null)
        {
            PrimaryColor = primary;
            SecondaryColor = secondary;
            PieceTexture = texture;
            
            DeriveHighlightColors(secondary);
        }
        
        /// <summary>
        /// Create a color scheme with texture loaded by name from Resources/Textures/
        /// </summary>
        public PlayerColorScheme(Color primary, Color secondary, string textureName)
            : this(primary, secondary, LoadTextureByName(textureName))
        {
        }
        
        /// <summary>
        /// Create a color scheme with just a texture name. Uses white as primary, green as highlight.
        /// </summary>
        public PlayerColorScheme(string textureName) 
            : this(Color.white, new Color(0f, 0.8f, 0f), LoadTextureByName(textureName))
        {
        }
        
        /// <summary>
        /// Create a color scheme with texture and custom highlight color.
        /// </summary>
        public PlayerColorScheme(string textureName, Color highlightColor) 
            : this(Color.white, highlightColor, LoadTextureByName(textureName))
        {
        }
        
        private void DeriveHighlightColors(Color secondary)
        {
            // Derive highlight colors from secondary color
            // Make it more saturated and vibrant for visibility
            float h, s, v;
            Color.RGBToHSV(secondary, out h, out s, out v);
            
            // Boost saturation and adjust value for better visibility
            s = Mathf.Clamp01(s + 0.3f);
            v = Mathf.Clamp01(v > 0.5f ? v : v + 0.3f);
            
            Color vibrant = Color.HSVToRGB(h, s, v);
            SelectHighlight = new Color(vibrant.r, vibrant.g, vibrant.b, 0.5f);
            HoverHighlight = new Color(vibrant.r, vibrant.g, vibrant.b, 0.25f);
        }
        
        private static Texture2D LoadTextureByName(string textureName)
        {
            if (string.IsNullOrEmpty(textureName)) return null;
            return Resources.Load<Texture2D>($"Textures/{textureName}");
        }
    }

    /// <summary>
    /// Static access to player colors and textures. Can be customized at runtime.
    /// </summary>
    public static class PlayerColors
    {
        private static PlayerColorScheme[] _schemes;
        private static bool _initialized = false;
        
        // Default texture paths (in Resources folder)
        private const string TEXTURE_PATH_LIGHT = "Textures/Wood1";
        private const string TEXTURE_PATH_DARK = "Textures/Wood2";

        static PlayerColors()
        {
            InitializeDefaults();
        }
        
        private static void InitializeDefaults()
        {
            // Load default textures
            Texture2D lightWood = Resources.Load<Texture2D>(TEXTURE_PATH_LIGHT);
            Texture2D darkWood = Resources.Load<Texture2D>(TEXTURE_PATH_DARK);
            
            Debug.Log($"[PlayerColors] Loading textures - Light: {(lightWood != null ? lightWood.name : "NULL")} from '{TEXTURE_PATH_LIGHT}'");
            Debug.Log($"[PlayerColors] Loading textures - Dark: {(darkWood != null ? darkWood.name : "NULL")} from '{TEXTURE_PATH_DARK}'");
            
            // Default: White/Ivory and Brown/Black with wood textures
            _schemes = new PlayerColorScheme[]
            {
                new PlayerColorScheme(
                    new Color(0.95f, 0.92f, 0.85f),  // Ivory primary (tint)
                    new Color(0.00f, 0.95f, 0f),    // Green selection highlight
                    lightWood                        // Light wood texture
                ),
                new PlayerColorScheme(
                    new Color(0.20f, 0.15f, 0.12f),  // Dark brown primary (tint)
                    new Color(0.95f, 0.0f, 0.0f),   // Red selection highlight
                    darkWood                         // Dark wood texture
                )
            };
            
            _initialized = true;
        }

        /// <summary>
        /// Get color scheme for a player.
        /// </summary>
        public static PlayerColorScheme Get(int playerId)
        {
            if (_schemes == null || _schemes.Length == 0)
                return new PlayerColorScheme(Color.white, Color.gray);
            
            return _schemes[Mathf.Clamp(playerId, 0, _schemes.Length - 1)];
        }

        /// <summary>
        /// Set custom color scheme for a player.
        /// </summary>
        public static void Set(int playerId, Color primary, Color secondary, Texture2D texture = null)
        {
            if (playerId < 0) return;
            
            // Expand array if needed
            if (playerId >= _schemes.Length)
            {
                var newSchemes = new PlayerColorScheme[playerId + 1];
                for (int i = 0; i < _schemes.Length; i++)
                    newSchemes[i] = _schemes[i];
                for (int i = _schemes.Length; i <= playerId; i++)
                    newSchemes[i] = new PlayerColorScheme(Color.gray, Color.gray);
                _schemes = newSchemes;
            }
            
            _schemes[playerId] = new PlayerColorScheme(primary, secondary, texture);
        }
        
        /// <summary>
        /// Set only the texture for a player (keeps existing colors).
        /// </summary>
        public static void SetTexture(int playerId, Texture2D texture)
        {
            if (playerId < 0 || playerId >= _schemes.Length) return;
            _schemes[playerId].PieceTexture = texture;
        }
        
        /// <summary>
        /// Set texture tiling for a player.
        /// </summary>
        public static void SetTextureTiling(int playerId, float tiling)
        {
            if (playerId < 0 || playerId >= _schemes.Length) return;
            _schemes[playerId].TextureTiling = tiling;
        }
        
        /// <summary>
        /// Set material smoothness for a player.
        /// </summary>
        public static void SetSmoothness(int playerId, float smoothness)
        {
            if (playerId < 0 || playerId >= _schemes.Length) return;
            _schemes[playerId].Smoothness = Mathf.Clamp01(smoothness);
        }

        /// <summary>
        /// Reset to default colors and textures.
        /// </summary>
        public static void ResetToDefaults()
        {
            InitializeDefaults();
        }
        
        /// <summary>
        /// Load a texture from Resources/Textures by name.
        /// </summary>
        public static Texture2D LoadTexture(string textureName)
        {
            return Resources.Load<Texture2D>($"Textures/{textureName}");
        }
    }
}

