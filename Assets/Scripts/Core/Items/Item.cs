using System;
using UnityEngine;
using BizarreChess.Core.Units;

namespace BizarreChess.Core.Items
{
    /// <summary>
    /// Visual shape for item representation on the board.
    /// </summary>
    public enum ItemShape
    {
        Sphere,
        Cube,
        Capsule,
        Cylinder
    }

    /// <summary>
    /// Base class for all items that can be placed on the board and picked up by units.
    /// Items must be picked up with an explicit action (not automatic).
    /// </summary>
    [Serializable]
    public abstract class Item
    {
        /// <summary>
        /// Unique identifier for this item instance.
        /// </summary>
        public string Id;

        /// <summary>
        /// Display name shown in UI.
        /// </summary>
        public string DisplayName;

        /// <summary>
        /// Description of what the item does.
        /// </summary>
        public string Description;

        /// <summary>
        /// Current position on the board. -1 if held by a unit.
        /// </summary>
        public int NodeId = -1;

        /// <summary>
        /// If true, the item drops back to the board when the holding unit dies.
        /// </summary>
        public bool DropOnDeath = false;

        /// <summary>
        /// Visual shape for the item on the board.
        /// </summary>
        public ItemShape Shape = ItemShape.Sphere;

        /// <summary>
        /// Color of the item visual.
        /// </summary>
        public Color ItemColor = Color.yellow;

        /// <summary>
        /// Counter for generating unique item IDs.
        /// </summary>
        private static int _nextItemId = 0;

        protected Item()
        {
            Id = $"item_{_nextItemId++}";
        }

        /// <summary>
        /// Called when a unit picks up this item.
        /// Implement to apply the item's effect to the unit.
        /// </summary>
        /// <param name="unit">The unit picking up the item.</param>
        public abstract void OnPick(UnitState unit);

        /// <summary>
        /// Called when a unit drops this item (manual drop or death with DropOnDeath=false).
        /// Override to remove any effects applied by OnPick.
        /// </summary>
        /// <param name="unit">The unit dropping the item.</param>
        public virtual void OnDrop(UnitState unit)
        {
            // Default: no cleanup needed
        }

        /// <summary>
        /// Create a deep copy of this item.
        /// </summary>
        public abstract Item Clone();

        /// <summary>
        /// Reset the static ID counter (useful for testing).
        /// </summary>
        public static void ResetIdCounter()
        {
            _nextItemId = 0;
        }
    }
}

