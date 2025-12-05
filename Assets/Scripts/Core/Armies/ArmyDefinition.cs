using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BizarreChess.Core.Units;

namespace BizarreChess.Core.Armies
{
    /// <summary>
    /// Defines the composition of an army (what units, relative positions).
    /// The actual placement depends on the map's spawn zones.
    /// </summary>
    [CreateAssetMenu(fileName = "NewArmy", menuName = "Bizarre Chess/Army Definition")]
    public class ArmyDefinition : ScriptableObject
    {
        [Header("Identity")]
        public string ArmyId;
        public string DisplayName;
        public string Description;

        [Header("Composition")]
        public List<ArmySlot> Slots;

        [Header("Restrictions")]
        public bool RequiresKing = true;  // Most game modes require a king

        /// <summary>
        /// Total cost of this army.
        /// </summary>
        public int TotalCost => Slots.Sum(s => s.Unit != null ? s.Unit.BaseCost : 0);

        /// <summary>
        /// Number of units in this army.
        /// </summary>
        public int UnitCount => Slots.Count;

        /// <summary>
        /// Check if army contains at least one king.
        /// </summary>
        public bool HasKing => Slots.Any(s => s.Unit != null && s.Unit.IsKing);

        /// <summary>
        /// Get all units of a specific type.
        /// </summary>
        public List<ArmySlot> GetSlotsOfType(PieceType type)
        {
            return Slots.Where(s => s.Unit != null && s.Unit.PieceType == type).ToList();
        }

        /// <summary>
        /// Validate this army against match restrictions.
        /// </summary>
        public ArmyValidationResult Validate(ArmyRestrictions restrictions)
        {
            var errors = new List<string>();

            if (RequiresKing && !HasKing)
            {
                errors.Add("Army must contain a King");
            }

            if (restrictions != null)
            {
                if (restrictions.MaxArmyCost > 0 && TotalCost > restrictions.MaxArmyCost)
                {
                    errors.Add($"Army cost ({TotalCost}) exceeds limit ({restrictions.MaxArmyCost})");
                }

                if (restrictions.MaxUnits > 0 && UnitCount > restrictions.MaxUnits)
                {
                    errors.Add($"Too many units ({UnitCount}), max is {restrictions.MaxUnits}");
                }

                if (restrictions.MaxOfSameType > 0)
                {
                    var typeCounts = Slots
                        .Where(s => s.Unit != null)
                        .GroupBy(s => s.Unit.UnitId)
                        .Select(g => new { UnitId = g.Key, Count = g.Count() });

                    foreach (var tc in typeCounts)
                    {
                        if (tc.Count > restrictions.MaxOfSameType)
                        {
                            errors.Add($"Too many {tc.UnitId} ({tc.Count}), max is {restrictions.MaxOfSameType}");
                        }
                    }
                }

                if (restrictions.BannedUnits != null)
                {
                    foreach (var slot in Slots)
                    {
                        if (slot.Unit != null && restrictions.BannedUnits.Contains(slot.Unit.UnitId))
                        {
                            errors.Add($"Unit {slot.Unit.DisplayName} is banned in this match");
                        }
                    }
                }

                if (restrictions.RequiredUnits != null)
                {
                    var presentUnits = Slots
                        .Where(s => s.Unit != null)
                        .Select(s => s.Unit.UnitId)
                        .ToHashSet();

                    foreach (var required in restrictions.RequiredUnits)
                    {
                        if (!presentUnits.Contains(required))
                        {
                            errors.Add($"Army must contain {required}");
                        }
                    }
                }
            }

            return new ArmyValidationResult
            {
                IsValid = errors.Count == 0,
                Errors = errors
            };
        }
    }

    /// <summary>
    /// A slot in an army - unit + relative position.
    /// </summary>
    [Serializable]
    public class ArmySlot
    {
        public UnitDefinition Unit;
        public Vector2Int RelativePosition;  // (0,0) = left corner of player's spawn zone
        public SlotRow Row;                  // Back row (majors) or Front row (pawns)

        public ArmySlot() { }

        public ArmySlot(UnitDefinition unit, int x, int y, SlotRow row = SlotRow.Back)
        {
            Unit = unit;
            RelativePosition = new Vector2Int(x, y);
            Row = row;
        }
    }

    /// <summary>
    /// Which row this slot belongs to (for spawn zone mapping).
    /// </summary>
    public enum SlotRow
    {
        Back,   // Major pieces (King, Queen, Rooks, Bishops, Knights)
        Front   // Minor pieces (Pawns)
    }

    /// <summary>
    /// Restrictions for army building in a match.
    /// </summary>
    [Serializable]
    public class ArmyRestrictions
    {
        public int MaxArmyCost;           // 0 = no limit
        public int MaxUnits;              // 0 = no limit
        public int MaxOfSameType;         // 0 = no limit
        public List<string> BannedUnits;
        public List<string> RequiredUnits;

        public ArmyRestrictions()
        {
            BannedUnits = new List<string>();
            RequiredUnits = new List<string>();
        }

        /// <summary>
        /// Classic chess restrictions.
        /// </summary>
        public static ArmyRestrictions Classic => new ArmyRestrictions
        {
            MaxArmyCost = 0,  // No cost limit in classic
            MaxUnits = 16,
            MaxOfSameType = 0,
            RequiredUnits = new List<string> { "King" }
        };
    }

    /// <summary>
    /// Result of army validation.
    /// </summary>
    public class ArmyValidationResult
    {
        public bool IsValid;
        public List<string> Errors;

        public ArmyValidationResult()
        {
            Errors = new List<string>();
        }
    }
}

