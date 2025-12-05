using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BizarreChess.Core.Units;
using BizarreChess.Core.Armies;

namespace BizarreChess.Core.Factories
{
    /// <summary>
    /// Factory for creating army definitions.
    /// Supports loading from JSON files in Resources folder with caching.
    /// Uses PiecesFactory for piece creation.
    /// </summary>
    public static class ArmyFactory
    {
        #region Cache

        private static Dictionary<string, ArmyDefinition> _cache = new Dictionary<string, ArmyDefinition>();
        private const string DefaultArmiesFolder = "Armies";

        #endregion

        #region Get (Lazy Load + Cache)

        /// <summary>
        /// Get an army by ID. Loads from cache if available, otherwise from JSON, 
        /// falling back to built-in armies.
        /// </summary>
        public static ArmyDefinition Get(string armyId)
        {
            // Check cache first
            if (_cache.TryGetValue(armyId, out var cached))
            {
                return cached;
            }

            // Try to load from JSON
            var army = LoadFromResources($"{DefaultArmiesFolder}/{armyId}");
            if (army != null)
            {
                _cache[armyId] = army;
                return army;
            }

            // Fallback to built-in armies
            army = CreateBuiltInArmy(armyId);
            if (army != null)
            {
                _cache[armyId] = army;
                return army;
            }

            Debug.LogWarning($"[ArmyFactory] Army not found: {armyId}");
            return null;
        }

        /// <summary>
        /// Try to create a built-in army by ID.
        /// </summary>
        private static ArmyDefinition CreateBuiltInArmy(string armyId)
        {
            return armyId switch
            {
                "classic" or "classic_chess_army" => CreateClassicArmy(),
                "horde" => CreateHordeArmy(),
                "major_pieces_only" => CreateMajorPiecesOnlyArmy(),
                "king_and_pawns" => CreateKingAndPawnsArmy(),
                _ => null
            };
        }

        #endregion

        #region JSON Loading

        /// <summary>
        /// Load an army definition from a JSON file in Resources.
        /// </summary>
        /// <param name="path">Path relative to Resources folder (without .json extension)</param>
        public static ArmyDefinition LoadFromResources(string path)
        {
            var textAsset = Resources.Load<TextAsset>(path);
            if (textAsset == null)
            {
                return null;
            }

            return LoadFromJson(textAsset.text);
        }

        /// <summary>
        /// Load an army definition from a JSON string.
        /// </summary>
        public static ArmyDefinition LoadFromJson(string json)
        {
            try
            {
                var armyJson = JsonUtility.FromJson<ArmyJson>(json);
                return CreateFromJson(armyJson);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ArmyFactory] Failed to parse army JSON: {e.Message}");
                return null;
            }
        }

        /// <summary>
        /// Convert ArmyJson DTO to ArmyDefinition.
        /// </summary>
        private static ArmyDefinition CreateFromJson(ArmyJson armyJson)
        {
            if (armyJson == null || armyJson.pieces == null)
            {
                return null;
            }

            var army = CreateEmptyArmy(
                armyJson.armyId ?? "unnamed",
                armyJson.displayName ?? "Unnamed Army",
                armyJson.description ?? ""
            );
            army.RequiresKing = armyJson.requiresKing;

            foreach (var p in armyJson.pieces)
            {
                var unit = PiecesFactory.Get(p.piece);
                if (unit != null)
                {
                    // Derive row from Y position: y=0 is Back, y>0 is Front
                    var row = p.y == 0 ? SlotRow.Back : SlotRow.Front;
                    army.Slots.Add(new ArmySlot(unit, p.x, p.y, row));
                }
                else
                {
                    Debug.LogWarning($"[ArmyFactory] Unknown piece type: {p.piece}");
                }
            }

            return army;
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Preload all armies from a folder in Resources.
        /// </summary>
        public static void PreloadAll(string folder = "Armies")
        {
            var textAssets = Resources.LoadAll<TextAsset>(folder);
            foreach (var textAsset in textAssets)
            {
                var army = LoadFromJson(textAsset.text);
                if (army != null && !string.IsNullOrEmpty(army.ArmyId))
                {
                    _cache[army.ArmyId] = army;
                }
            }
            Debug.Log($"[ArmyFactory] Preloaded {textAssets.Length} armies from {folder}");
        }

        /// <summary>
        /// Clear the army cache. Useful for testing or hot-reloading.
        /// </summary>
        public static void ClearCache()
        {
            _cache.Clear();
        }

        /// <summary>
        /// Get all available army IDs (cached + built-in).
        /// </summary>
        public static IEnumerable<string> GetAvailableArmyIds()
        {
            var builtIn = new[] { "classic", "horde", "major_pieces_only", "king_and_pawns" };
            return _cache.Keys.Concat(builtIn).Distinct();
        }

        #endregion

        #region Classic Army

        /// <summary>
        /// Create the classic chess army (16 pieces in standard positions).
        /// </summary>
        public static ArmyDefinition CreateClassicArmy()
        {
            var army = ScriptableObject.CreateInstance<ArmyDefinition>();
            army.ArmyId = "classic";
            army.DisplayName = "Classic Chess Army";
            army.Description = "Standard chess army with 16 pieces";
            army.RequiresKing = true;
            army.Slots = new List<ArmySlot>();

            // Back row (y=0): Rook, Knight, Bishop, Queen, King, Bishop, Knight, Rook
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("Rook"), 0, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("Knight"), 1, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("Bishop"), 2, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("Queen"), 3, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("King"), 4, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("Bishop"), 5, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("Knight"), 6, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("Rook"), 7, 0, SlotRow.Back));

            // Front row (y=1): 8 pawns
            for (int x = 0; x < 8; x++)
            {
                army.Slots.Add(new ArmySlot(PiecesFactory.Get("Pawn"), x, 1, SlotRow.Front));
            }

            return army;
        }

        #endregion

        #region Custom Army Building

        /// <summary>
        /// Create an empty army definition to build upon.
        /// </summary>
        public static ArmyDefinition CreateEmptyArmy(string armyId, string displayName, string description = "")
        {
            var army = ScriptableObject.CreateInstance<ArmyDefinition>();
            army.ArmyId = armyId;
            army.DisplayName = displayName;
            army.Description = description;
            army.RequiresKing = true;
            army.Slots = new List<ArmySlot>();
            return army;
        }

        /// <summary>
        /// Create a custom army from a list of piece placements.
        /// </summary>
        public static ArmyDefinition CreateCustomArmy(
            string armyId,
            string displayName,
            string description,
            List<PiecePlacement> placements,
            bool requiresKing = true)
        {
            var army = CreateEmptyArmy(armyId, displayName, description);
            army.RequiresKing = requiresKing;

            foreach (var placement in placements)
            {
                var unit = PiecesFactory.Get(placement.PieceName);
                if (unit != null)
                {
                    army.Slots.Add(new ArmySlot(unit, placement.X, placement.Y, placement.Row));
                }
            }

            return army;
        }

        #endregion

        #region Variant Armies

        /// <summary>
        /// Create a pawn-less army (only major pieces).
        /// Useful for endgame practice.
        /// </summary>
        public static ArmyDefinition CreateMajorPiecesOnlyArmy()
        {
            var army = CreateEmptyArmy(
                "major_pieces_only",
                "Major Pieces Only",
                "Army with only major pieces, no pawns"
            );

            // Same back row as classic
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("Rook"), 0, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("Knight"), 1, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("Bishop"), 2, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("Queen"), 3, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("King"), 4, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("Bishop"), 5, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("Knight"), 6, 0, SlotRow.Back));
            army.Slots.Add(new ArmySlot(PiecesFactory.Get("Rook"), 7, 0, SlotRow.Back));

            return army;
        }

        /// <summary>
        /// Create a minimal army with just King and Pawns.
        /// </summary>
        public static ArmyDefinition CreateKingAndPawnsArmy()
        {
            var army = CreateEmptyArmy(
                "king_and_pawns",
                "King and Pawns",
                "Minimal army with only King and Pawns"
            );

            army.Slots.Add(new ArmySlot(PiecesFactory.Get("King"), 4, 0, SlotRow.Back));

            var pawn = PiecesFactory.Get("Pawn");
            for (int x = 0; x < 8; x++)
            {
                army.Slots.Add(new ArmySlot(pawn, x, 1, SlotRow.Front));
            }

            return army;
        }

        /// <summary>
        /// Create a horde army (lots of pawns).
        /// </summary>
        public static ArmyDefinition CreateHordeArmy(int rows = 4)
        {
            var army = CreateEmptyArmy(
                "horde",
                "Horde",
                $"Army with {rows} rows of pawns"
            );
            army.RequiresKing = false; // Horde doesn't need a king

            var pawn = PiecesFactory.Get("Pawn");
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    army.Slots.Add(new ArmySlot(pawn, x, y, y == 0 ? SlotRow.Back : SlotRow.Front));
                }
            }

            return army;
        }

        #endregion
    }

    #region JSON DTOs

    /// <summary>
    /// JSON structure for army definitions.
    /// </summary>
    [Serializable]
    public class ArmyJson
    {
        public string armyId;
        public string displayName;
        public string description;
        public bool requiresKing = true;
        public List<PiecePlacementJson> pieces;
    }

    /// <summary>
    /// JSON structure for piece placements.
    /// </summary>
    [Serializable]
    public class PiecePlacementJson
    {
        public string piece;
        public int x;
        public int y;
    }

    #endregion

    /// <summary>
    /// Simple struct for defining piece placements when building custom armies programmatically.
    /// </summary>
    public struct PiecePlacement
    {
        public string PieceName;
        public int X;
        public int Y;
        public SlotRow Row;

        public PiecePlacement(string pieceName, int x, int y, SlotRow row = SlotRow.Back)
        {
            PieceName = pieceName;
            X = x;
            Y = y;
            Row = row;
        }
    }
}
