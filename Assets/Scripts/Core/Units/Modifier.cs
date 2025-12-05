using System;

namespace BizarreChess.Core.Units
{
    /// <summary>
    /// A modifier that affects unit stats (buff, debuff, equipment bonus, etc.)
    /// </summary>
    [Serializable]
    public struct Modifier
    {
        public string Id;
        public string DisplayName;
        public ModifierType Type;
        public string TargetStat;        // "Attack", "Defense", "Speed", etc.
        public ModifierOperation Operation;
        public int Value;
        public ModifierDuration DurationType;
        public int TurnsRemaining;       // For turn-based duration
        public string Source;            // What caused this (ability, item, tile, etc.)

        public bool IsExpired => DurationType == ModifierDuration.Turns && TurnsRemaining <= 0;

        public static Modifier CreateBuff(string stat, int value, int turns, string source = "")
        {
            return new Modifier
            {
                Id = Guid.NewGuid().ToString(),
                Type = ModifierType.Buff,
                TargetStat = stat,
                Operation = ModifierOperation.Add,
                Value = value,
                DurationType = ModifierDuration.Turns,
                TurnsRemaining = turns,
                Source = source
            };
        }

        public static Modifier CreateDebuff(string stat, int value, int turns, string source = "")
        {
            return new Modifier
            {
                Id = Guid.NewGuid().ToString(),
                Type = ModifierType.Debuff,
                TargetStat = stat,
                Operation = ModifierOperation.Add,
                Value = -value,
                DurationType = ModifierDuration.Turns,
                TurnsRemaining = turns,
                Source = source
            };
        }

        public static Modifier CreatePermanent(string stat, int value, string source)
        {
            return new Modifier
            {
                Id = Guid.NewGuid().ToString(),
                Type = ModifierType.Equipment,
                TargetStat = stat,
                Operation = ModifierOperation.Add,
                Value = value,
                DurationType = ModifierDuration.Permanent,
                TurnsRemaining = -1,
                Source = source
            };
        }

        public void DecrementTurn()
        {
            if (DurationType == ModifierDuration.Turns && TurnsRemaining > 0)
            {
                TurnsRemaining--;
            }
        }
    }

    public enum ModifierType
    {
        Buff,
        Debuff,
        Equipment,
        TileEffect,
        Ability,
        Aura
    }

    public enum ModifierOperation
    {
        Add,           // Stat + Value
        Multiply,      // Stat * Value (Value is percentage, e.g., 150 = 1.5x)
        Set,           // Stat = Value
        Override       // Ignore other modifiers, use this value
    }

    public enum ModifierDuration
    {
        // Temporary (in match)
        Turns,              // Lasts X turns
        UntilEndOfTurn,     // Expires at end of current turn
        UntilDamaged,       // Expires when unit takes damage
        WhileOnNode,        // Active while on specific node type
        UntilMatchEnd,      // Entire match
        
        // Persistent (between matches)
        Permanent,          // Forever (equipment, unlocks)
        UntilRemoved        // Until player manually removes
    }
}

