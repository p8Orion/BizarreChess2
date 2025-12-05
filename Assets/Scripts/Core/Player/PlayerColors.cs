using UnityEngine;

namespace BizarreChess.Core.Player
{
    /// <summary>
    /// Centralized player color configuration.
    /// All visual systems should derive their colors from here.
    /// </summary>
    [System.Serializable]
    public class PlayerColorScheme
    {
        public Color PrimaryColor;      // Main piece color
        public Color SecondaryColor;    // Accent/detail color
        public Color SelectHighlight;   // Selection highlight (derived, more saturated)
        public Color HoverHighlight;    // Hover highlight (derived, more transparent)

        public PlayerColorScheme(Color primary, Color secondary)
        {
            PrimaryColor = primary;
            SecondaryColor = secondary;
            
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
    }

    /// <summary>
    /// Static access to player colors. Can be customized at runtime.
    /// </summary>
    public static class PlayerColors
    {
        private static PlayerColorScheme[] _schemes;

        static PlayerColors()
        {
            // Default: White/Ivory and Brown/Black
            _schemes = new PlayerColorScheme[]
            {
                new PlayerColorScheme(
                    new Color(0.95f, 0.92f, 0.85f),  // Ivory primary
                    new Color(0.00f, 0.95f, 0f)   // Darker ivory secondary
                ),
                new PlayerColorScheme(
                    new Color(0.20f, 0.15f, 0.12f),  // Dark brown primary
                    new Color(0.95f, 0.0f, 0.0f)   // Lighter brown secondary
                )
            };
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
        public static void Set(int playerId, Color primary, Color secondary)
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
            
            _schemes[playerId] = new PlayerColorScheme(primary, secondary);
        }

        /// <summary>
        /// Reset to default colors.
        /// </summary>
        public static void ResetToDefaults()
        {
            _schemes = new PlayerColorScheme[]
            {
                new PlayerColorScheme(
                    new Color(0.95f, 0.92f, 0.85f),
                    new Color(0.85f, 0.82f, 0.75f)
                ),
                new PlayerColorScheme(
                    new Color(0.20f, 0.15f, 0.12f),
                    new Color(0.30f, 0.25f, 0.20f)
                )
            };
        }
    }
}

