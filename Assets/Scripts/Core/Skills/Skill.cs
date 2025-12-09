using System;
using UnityEngine;

namespace BizarreChess.Core.Skills
{
    /// <summary>
    /// Base class for all skills/abilities that can be attached to units.
    /// Skills are cloned from UnitDefinition to UnitState so each instance can be modified independently.
    /// </summary>
    [Serializable]
    public abstract class Skill
    {
        public string Id;
        public string DisplayName;
        
        [TextArea]
        public string Description;

        /// <summary>
        /// Create a deep copy of this skill for a unit instance.
        /// </summary>
        public abstract Skill Clone();
    }
}

