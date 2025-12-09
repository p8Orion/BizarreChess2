using System;
using UnityEngine;
using BizarreChess.Core.Units;
using BizarreChess.Core.Skills;

namespace BizarreChess.Core.Items
{
    /// <summary>
    /// An item that grants the Forcefield skill when picked up.
    /// The item drops back to the board when the unit dies.
    /// </summary>
    [Serializable]
    public class ForceFieldGeneratorItem : Item
    {
        public ForceFieldGeneratorItem()
        {
            DisplayName = "Force Field Generator";
            Description = "Grants a protective forcefield that blocks one capture attempt. Drops on death.";
            DropOnDeath = true;
            Shape = ItemShape.Sphere;
            ItemColor = new Color(0.2f, 0.8f, 1f, 1f); // Cyan/electric blue
        }

        /// <summary>
        /// Grants the Forcefield skill to the unit.
        /// </summary>
        public override void OnPick(UnitState unit)
        {
            // Only add if unit doesn't already have an active forcefield
            if (!unit.HasSkill("Forcefield"))
            {
                unit.AddSkill(new ForcefieldSkill());
            }
        }

        /// <summary>
        /// Removes the Forcefield skill if still present.
        /// Note: If forcefield was consumed in combat, it's already removed.
        /// </summary>
        public override void OnDrop(UnitState unit)
        {
            unit.RemoveSkill("Forcefield");
        }

        public override Item Clone()
        {
            return new ForceFieldGeneratorItem
            {
                Id = this.Id,
                DisplayName = this.DisplayName,
                Description = this.Description,
                NodeId = this.NodeId,
                DropOnDeath = this.DropOnDeath,
                Shape = this.Shape,
                ItemColor = this.ItemColor
            };
        }
    }
}

