using UnityEngine;

namespace BizarreChess.Core.Board
{
    [System.Serializable]
    public class TileStyle
    {
        public Color Color = Color.white;
        public Texture2D Texture;
        public float TextureTiling = 1f;
        public float Smoothness = 0.3f;

        public TileStyle() { }

        public TileStyle(Color color, Texture2D texture = null, float tiling = 1f, float smoothness = 0.3f)
        {
            Color = color;
            Texture = texture;
            TextureTiling = tiling;
            Smoothness = smoothness;
        }
    }

    [System.Serializable]
    public class BoardSkin
    {
        public string SkinId;
        public string DisplayName;

        public TileStyle NormalLight;
        public TileStyle NormalDark;
        public TileStyle Impassable;
        public TileStyle Boost;
        public TileStyle Trap;
        public TileStyle Teleport;
        public TileStyle Unstable;
        public TileStyle Destroyed;

        public BoardSkin()
        {
            NormalLight = new TileStyle(new Color(0.93f, 0.86f, 0.70f));
            NormalDark = new TileStyle(new Color(0.55f, 0.36f, 0.24f));
            Impassable = new TileStyle(Color.gray);
            Boost = new TileStyle(Color.green * 0.7f);
            Trap = new TileStyle(Color.red * 0.7f);
            Teleport = new TileStyle(Color.blue * 0.7f);
            Unstable = new TileStyle(Color.yellow * 0.7f);
            Destroyed = new TileStyle(Color.black);
        }

        public TileStyle GetStyle(NodeType type, bool isLight)
        {
            return type switch
            {
                NodeType.Normal => isLight ? NormalLight : NormalDark,
                NodeType.Impassable => Impassable,
                NodeType.Boost => Boost,
                NodeType.Trap => Trap,
                NodeType.Teleport => Teleport,
                NodeType.Unstable => Unstable,
                NodeType.Destroyed => Destroyed,
                NodeType.Abyss => null,
                _ => NormalLight
            };
        }
    }

    public static class BoardSkins
    {
        private static BoardSkin _current;
        private static BoardSkin _classicWood;
        private static BoardSkin _colorful;

        static BoardSkins()
        {
            InitializePresets();
            _current = _classicWood;
        }

        private static void InitializePresets()
        {
            // Classic Wood - with textures
            var lightWoodTex = LoadTexture("Wood2");
            var darkWoodTex = LoadTexture("Wood2");
            
            _classicWood = new BoardSkin
            {
                SkinId = "classic_wood",
                DisplayName = "Classic Wood",
                NormalLight = new TileStyle(new Color(0.95f, 0.85f, 0.70f), lightWoodTex, tiling: 1f, smoothness: 0.3f),
                NormalDark = new TileStyle(new Color(0.55f, 0.35f, 0.20f), darkWoodTex, tiling: 1f, smoothness: 0.3f),
                Impassable = new TileStyle(new Color(0.25f, 0.25f, 0.25f), darkWoodTex, tiling: 1f, smoothness: 0.1f),
                Boost = new TileStyle(new Color(0.3f, 0.7f, 0.4f), smoothness: 0.4f),
                Trap = new TileStyle(new Color(0.7f, 0.25f, 0.25f), smoothness: 0.2f),
                Teleport = new TileStyle(new Color(0.3f, 0.5f, 0.8f), smoothness: 0.6f),
                Unstable = new TileStyle(new Color(0.8f, 0.6f, 0.2f), smoothness: 0.3f),
                Destroyed = new TileStyle(new Color(0.1f, 0.08f, 0.05f), smoothness: 0.0f)
            };

            // Colorful - no textures, vibrant solid colors
            _colorful = new BoardSkin
            {
                SkinId = "colorful",
                DisplayName = "Colorful",
                NormalLight = new TileStyle(new Color(0.95f, 0.95f, 0.90f), smoothness: 0.5f),  // Off-white
                NormalDark = new TileStyle(new Color(0.2f, 0.6f, 0.8f), smoothness: 0.5f),      // Cyan blue
                Impassable = new TileStyle(new Color(0.3f, 0.3f, 0.35f), smoothness: 0.2f),     // Dark gray
                Boost = new TileStyle(new Color(0.2f, 0.9f, 0.4f), smoothness: 0.6f),           // Bright green
                Trap = new TileStyle(new Color(0.95f, 0.2f, 0.3f), smoothness: 0.4f),           // Bright red
                Teleport = new TileStyle(new Color(0.6f, 0.3f, 0.9f), smoothness: 0.7f),        // Purple
                Unstable = new TileStyle(new Color(1f, 0.8f, 0.2f), smoothness: 0.5f),          // Golden yellow
                Destroyed = new TileStyle(new Color(0.15f, 0.15f, 0.15f), smoothness: 0.1f)     // Near black
            };
        }

        // Preset skins
        public static BoardSkin ClassicWood => _classicWood;
        public static BoardSkin Colorful => _colorful;

        // Current active skin
        public static BoardSkin Current => _current ?? _classicWood;

        public static void SetSkin(BoardSkin skin) => _current = skin ?? _classicWood;

        public static void ResetToDefault() => _current = _classicWood;

        public static void UseClassicWood() => _current = _classicWood;

        public static void UseColorful() => _current = _colorful;

        public static Texture2D LoadTexture(string textureName)
        {
            if (string.IsNullOrEmpty(textureName)) return null;
            return Resources.Load<Texture2D>($"Textures/{textureName}");
        }
    }
}
