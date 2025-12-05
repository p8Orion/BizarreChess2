using System;
using UnityEngine;

namespace BizarreChess.Core.Graph
{
    /// <summary>
    /// Definition of a node in the board graph (immutable template data).
    /// </summary>
    [Serializable]
    public struct NodeDefinition
    {
        public int Id;
        public Vector2 Position;           // Visual position for rendering
        public NodeType InitialType;
        public int TeleportTargetId;       // -1 if not a teleport
        public bool IsDestructible;
        public bool IsLightTile;           // For classic chess coloring

        public NodeDefinition(int id, Vector2 position, NodeType type = NodeType.Normal, bool isLight = true)
        {
            Id = id;
            Position = position;
            InitialType = type;
            TeleportTargetId = -1;
            IsDestructible = false;
            IsLightTile = isLight;
        }
    }

    /// <summary>
    /// Runtime state of a node (mutable, synchronized in multiplayer).
    /// </summary>
    [Serializable]
    public struct NodeState
    {
        public int Id;
        public NodeType CurrentType;
        public bool IsActive;              // False if destroyed
        public int TeleportTargetId;       // Can change dynamically
        public int EffectDuration;         // Turns remaining for temporary effects

        public static NodeState FromDefinition(NodeDefinition def)
        {
            return new NodeState
            {
                Id = def.Id,
                CurrentType = def.InitialType,
                IsActive = true,
                TeleportTargetId = def.TeleportTargetId,
                EffectDuration = -1
            };
        }

        public bool IsPassable => IsActive && CurrentType != NodeType.Impassable && CurrentType != NodeType.Destroyed;
    }

    /// <summary>
    /// Types of nodes in the board graph.
    /// </summary>
    public enum NodeType
    {
        Normal,
        Impassable,
        Boost,         // Buffs unit standing here
        Teleport,      // Transports to TeleportTargetId
        Trap,          // Damages or debuffs
        Destroyed,     // Permanently impassable
        Unstable       // Will be destroyed after X turns
    }
}

