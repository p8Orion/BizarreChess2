using System;
using System.Collections.Generic;

namespace BizarreChess.Core.Units
{
    /// <summary>
    /// Runtime state of a unit in a match (mutable, synchronized in multiplayer).
    /// </summary>
    [Serializable]
    public class UnitState
    {
        // Identity
        public int UnitId;               // Unique ID within this match
        public string DefinitionId;      // Reference to UnitDefinition
        public int OwnerId;              // Player ID who owns this unit
        public int CurrentNodeId;        // Position on the board

        // Progression (persistent between matches for owned units)
        public int Level;
        public int Experience;
        public int ExperienceToNextLevel;

        // Current stats (recalculated when modifiers change)
        public int CurrentHealth;
        public int MaxHealth;
        public int Attack;
        public int Defense;
        public int Speed;
        public int Range;

        // Equipment
        public List<string> EquippedItemIds;

        // Modifiers (buffs/debuffs)
        public List<Modifier> ActiveModifiers;

        // Turn state
        public bool HasMovedThisTurn;
        public bool HasActedThisTurn;
        public bool HasEverMoved;        // For pawn double move

        // Status
        public bool IsAlive;
        public int TurnsUntilRespawn;    // -1 if no respawn

        public UnitState()
        {
            EquippedItemIds = new List<string>();
            ActiveModifiers = new List<Modifier>();
            IsAlive = true;
            Level = 1;
            TurnsUntilRespawn = -1;
        }

        /// <summary>
        /// Create initial state from a unit definition.
        /// </summary>
        public static UnitState Create(int unitId, UnitDefinition definition, int ownerId, int nodeId, int level = 1)
        {
            var stats = UnitCalculatedStats.Calculate(definition.BaseStats, definition.GrowthStats, level);

            return new UnitState
            {
                UnitId = unitId,
                DefinitionId = definition.UnitId,
                OwnerId = ownerId,
                CurrentNodeId = nodeId,
                Level = level,
                Experience = 0,
                ExperienceToNextLevel = CalculateXPRequired(level),
                CurrentHealth = stats.MaxHealth,
                MaxHealth = stats.MaxHealth,
                Attack = stats.Attack,
                Defense = stats.Defense,
                Speed = stats.Speed,
                Range = stats.Range,
                IsAlive = true,
                HasMovedThisTurn = false,
                HasActedThisTurn = false,
                HasEverMoved = false
            };
        }

        #region Stat Calculation

        /// <summary>
        /// Recalculate stats based on base + level + equipment + modifiers.
        /// </summary>
        public void RecalculateStats(UnitDefinition definition)
        {
            var baseCalc = UnitCalculatedStats.Calculate(definition.BaseStats, definition.GrowthStats, Level);

            MaxHealth = ApplyModifiers("Health", baseCalc.MaxHealth);
            Attack = ApplyModifiers("Attack", baseCalc.Attack);
            Defense = ApplyModifiers("Defense", baseCalc.Defense);
            Speed = ApplyModifiers("Speed", baseCalc.Speed);
            Range = ApplyModifiers("Range", baseCalc.Range);

            // Clamp health to max
            if (CurrentHealth > MaxHealth)
                CurrentHealth = MaxHealth;
        }

        private int ApplyModifiers(string stat, int baseValue)
        {
            int additive = 0;
            float multiplicative = 1f;
            int? overrideValue = null;

            foreach (var mod in ActiveModifiers)
            {
                if (mod.TargetStat != stat || mod.IsExpired)
                    continue;

                switch (mod.Operation)
                {
                    case ModifierOperation.Add:
                        additive += mod.Value;
                        break;
                    case ModifierOperation.Multiply:
                        multiplicative *= mod.Value / 100f;
                        break;
                    case ModifierOperation.Set:
                    case ModifierOperation.Override:
                        overrideValue = mod.Value;
                        break;
                }
            }

            if (overrideValue.HasValue)
                return overrideValue.Value;

            return (int)((baseValue + additive) * multiplicative);
        }

        #endregion

        #region Experience & Leveling

        public static int CalculateXPRequired(int level)
        {
            // Simple formula: 100 * level^1.5
            return (int)(100 * Math.Pow(level, 1.5));
        }

        public void AddExperience(int amount)
        {
            Experience += amount;
            while (Experience >= ExperienceToNextLevel)
            {
                Experience -= ExperienceToNextLevel;
                Level++;
                ExperienceToNextLevel = CalculateXPRequired(Level);
            }
        }

        #endregion

        #region Combat

        public void TakeDamage(int damage)
        {
            int actualDamage = Math.Max(1, damage - Defense);
            CurrentHealth -= actualDamage;

            if (CurrentHealth <= 0)
            {
                CurrentHealth = 0;
                IsAlive = false;
            }

            // Remove "until damaged" modifiers
            ActiveModifiers.RemoveAll(m => m.DurationType == ModifierDuration.UntilDamaged);
        }

        public void Heal(int amount)
        {
            CurrentHealth = Math.Min(CurrentHealth + amount, MaxHealth);
        }

        #endregion

        #region Turn Management

        public void StartTurn()
        {
            HasMovedThisTurn = false;
            HasActedThisTurn = false;
        }

        public void EndTurn()
        {
            // Decrement turn-based modifiers
            for (int i = ActiveModifiers.Count - 1; i >= 0; i--)
            {
                var mod = ActiveModifiers[i];
                mod.DecrementTurn();
                ActiveModifiers[i] = mod;

                if (mod.IsExpired)
                {
                    ActiveModifiers.RemoveAt(i);
                }
            }

            // Remove end-of-turn modifiers
            ActiveModifiers.RemoveAll(m => m.DurationType == ModifierDuration.UntilEndOfTurn);
        }

        public void MoveTo(int newNodeId)
        {
            CurrentNodeId = newNodeId;
            HasMovedThisTurn = true;
            HasEverMoved = true;
        }

        public bool CanAct => IsAlive && !HasActedThisTurn;
        public bool CanMove => IsAlive && !HasMovedThisTurn;

        #endregion

        #region Modifiers

        public void AddModifier(Modifier modifier)
        {
            ActiveModifiers.Add(modifier);
        }

        public void RemoveModifier(string modifierId)
        {
            ActiveModifiers.RemoveAll(m => m.Id == modifierId);
        }

        public void RemoveModifiersBySource(string source)
        {
            ActiveModifiers.RemoveAll(m => m.Source == source);
        }

        #endregion
    }
}

