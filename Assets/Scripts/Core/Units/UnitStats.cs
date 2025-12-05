using System;

namespace BizarreChess.Core.Units
{
    /// <summary>
    /// Base stats for a unit (level 1, no equipment).
    /// </summary>
    [Serializable]
    public struct UnitBaseStats
    {
        public int Health;
        public int Attack;
        public int Defense;
        public int Speed;        // Turn order / initiative
        public int Range;        // Attack range (1 = melee)
        public int Movement;     // How far can move per turn (for non-pattern based movement)

        public static UnitBaseStats Default => new UnitBaseStats
        {
            Health = 10,
            Attack = 5,
            Defense = 2,
            Speed = 5,
            Range = 1,
            Movement = 1
        };
    }

    /// <summary>
    /// Stat growth per level.
    /// </summary>
    [Serializable]
    public struct UnitGrowthStats
    {
        public int HealthPerLevel;
        public int AttackPerLevel;
        public int DefensePerLevel;
        public int SpeedPerLevel;

        public static UnitGrowthStats Default => new UnitGrowthStats
        {
            HealthPerLevel = 2,
            AttackPerLevel = 1,
            DefensePerLevel = 1,
            SpeedPerLevel = 0
        };
    }

    /// <summary>
    /// Calculated stats at runtime (base + level + equipment + modifiers).
    /// </summary>
    [Serializable]
    public struct UnitCalculatedStats
    {
        public int MaxHealth;
        public int Attack;
        public int Defense;
        public int Speed;
        public int Range;
        public int Movement;

        public static UnitCalculatedStats Calculate(UnitBaseStats baseStats, UnitGrowthStats growth, int level)
        {
            return new UnitCalculatedStats
            {
                MaxHealth = baseStats.Health + growth.HealthPerLevel * (level - 1),
                Attack = baseStats.Attack + growth.AttackPerLevel * (level - 1),
                Defense = baseStats.Defense + growth.DefensePerLevel * (level - 1),
                Speed = baseStats.Speed + growth.SpeedPerLevel * (level - 1),
                Range = baseStats.Range,
                Movement = baseStats.Movement
            };
        }
    }
}

