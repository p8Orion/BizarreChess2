using System;
using System.Collections.Generic;
using BizarreChess.Core.Board;
using BizarreChess.Core.Skills;
using BizarreChess.Core.Items;

namespace BizarreChess.Core.Units
{
    /// <summary>
    /// Runtime state of a unit in a match (mutable, synchronized in multiplayer).
    /// This is a clone of UnitDefinition that can be modified independently per instance.
    /// </summary>
    [Serializable]
    public class UnitState
    {
        // Identity
        public int UnitId;               // Unique ID within this match
        public string DefinitionId;      // Reference to UnitDefinition (for visuals, etc.)
        public int OwnerId;              // Player ID who owns this unit
        public int CurrentNodeId;        // Position on the board

        // Cloned from definition (modifiable per instance)
        public List<MovementPattern> MovementPatterns;
        
        [NonSerialized]
        public List<Skill> Skills;

        // Turn state
        public bool HasMovedThisTurn;
        public bool HasActedThisTurn;
        public bool HasEverMoved;        // For pawn double move

        // Status
        public bool IsAlive;

        // Item
        [NonSerialized]
        public Item HeldItem;

        public UnitState()
        {
            MovementPatterns = new List<MovementPattern>();
            Skills = new List<Skill>();
            IsAlive = true;
        }

        /// <summary>
        /// Create initial state from a unit definition.
        /// Clones MovementPatterns and Skills so each instance can be modified independently.
        /// </summary>
        public static UnitState Create(int unitId, UnitDefinition definition, int ownerId, int nodeId)
        {
            var state = new UnitState
            {
                UnitId = unitId,
                DefinitionId = definition.UnitId,
                OwnerId = ownerId,
                CurrentNodeId = nodeId,
                IsAlive = true,
                HasMovedThisTurn = false,
                HasActedThisTurn = false,
                HasEverMoved = false
            };

            // Clone movement patterns
            state.MovementPatterns = CloneMovementPatterns(definition.MovementPatterns);

            // Clone skills
            state.Skills = CloneSkills(definition.Skills);

            return state;
        }

        /// <summary>
        /// Deep clone movement patterns from a definition.
        /// </summary>
        private static List<MovementPattern> CloneMovementPatterns(List<MovementPattern> patterns)
        {
            if (patterns == null)
                return new List<MovementPattern>();

            var cloned = new List<MovementPattern>(patterns.Count);
            foreach (var pattern in patterns)
            {
                cloned.Add(new MovementPattern
                {
                    Type = pattern.Type,
                    MaxDistance = pattern.MaxDistance,
                    MinDistance = pattern.MinDistance,
                    CanJump = pattern.CanJump,
                    CaptureOnly = pattern.CaptureOnly,
                    MoveOnly = pattern.MoveOnly,
                    FirstMoveOnly = pattern.FirstMoveOnly,
                    RangedCapture = pattern.RangedCapture,
                    Direction = pattern.Direction,
                    LeapX = pattern.LeapX,
                    LeapY = pattern.LeapY
                });
            }
            return cloned;
        }

        /// <summary>
        /// Deep clone skills from a definition.
        /// </summary>
        private static List<Skill> CloneSkills(List<Skill> skills)
        {
            if (skills == null)
                return new List<Skill>();

            var cloned = new List<Skill>(skills.Count);
            foreach (var skill in skills)
            {
                cloned.Add(skill.Clone());
            }
            return cloned;
        }

        #region Movement

        /// <summary>
        /// Get all valid target nodes for this unit's movement patterns.
        /// </summary>
        public List<int> GetAllValidMoves(BoardGraph board, int playerSide,
            Func<int, bool> isOccupied, Func<int, bool> isEnemy)
        {
            return GetAllCategorizedMoves(board, playerSide, isOccupied, isEnemy).GetAll();
        }

        /// <summary>
        /// Get all valid target nodes categorized by type (move-only, capture-only, both).
        /// </summary>
        public MoveTargets GetAllCategorizedMoves(BoardGraph board, int playerSide,
            Func<int, bool> isOccupied, Func<int, bool> isEnemy)
        {
            var result = new MoveTargets();
            var seen = new HashSet<int>();

            foreach (var pattern in MovementPatterns)
            {
                // Skip first-move-only patterns if unit has already moved
                if (pattern.FirstMoveOnly && HasEverMoved)
                    continue;

                var targets = pattern.GetCategorizedTargets(board, CurrentNodeId, playerSide, isOccupied, isEnemy);
                result.Merge(targets, seen);
            }

            return result;
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
            // Future: process skill effects that trigger on turn end
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

        #region Skills

        /// <summary>
        /// Get a skill by its ID.
        /// </summary>
        public Skill GetSkill(string skillId)
        {
            return Skills?.Find(s => s.Id == skillId);
        }

        /// <summary>
        /// Check if unit has a specific skill.
        /// </summary>
        public bool HasSkill(string skillId)
        {
            return Skills?.Exists(s => s.Id == skillId) ?? false;
        }

        /// <summary>
        /// Add a skill to this unit instance.
        /// </summary>
        public void AddSkill(Skill skill)
        {
            Skills ??= new List<Skill>();
            Skills.Add(skill);
        }

        /// <summary>
        /// Remove a skill from this unit instance.
        /// </summary>
        public void RemoveSkill(string skillId)
        {
            Skills?.RemoveAll(s => s.Id == skillId);
        }

        #endregion

        #region Items

        /// <summary>
        /// Check if this unit can pick up an item (must not already hold one).
        /// </summary>
        public bool CanPickUpItem => HeldItem == null;

        /// <summary>
        /// Pick up an item and apply its effect.
        /// </summary>
        /// <param name="item">The item to pick up.</param>
        /// <returns>True if successful, false if already holding an item.</returns>
        public bool PickUpItem(Item item)
        {
            if (HeldItem != null)
                return false;

            HeldItem = item;
            item.NodeId = -1; // Item is no longer on the board
            item.OnPick(this);
            return true;
        }

        /// <summary>
        /// Drop the currently held item.
        /// </summary>
        /// <param name="dropNodeId">The node where the item should be placed.</param>
        /// <returns>The dropped item, or null if not holding any.</returns>
        public Item DropItem(int dropNodeId)
        {
            if (HeldItem == null)
                return null;

            var item = HeldItem;
            item.OnDrop(this);
            item.NodeId = dropNodeId;
            HeldItem = null;
            return item;
        }

        /// <summary>
        /// Check if unit has an item that should drop on death.
        /// </summary>
        public bool HasDropOnDeathItem => HeldItem != null && HeldItem.DropOnDeath;

        #endregion
    }
}
