using System;

namespace BizarreChess.Core.Graph
{
    /// <summary>
    /// Definition of an edge connecting two nodes (immutable template).
    /// </summary>
    [Serializable]
    public struct EdgeDefinition
    {
        public int FromNodeId;
        public int ToNodeId;
        public bool IsBidirectional;
        public EdgeType Type;

        public EdgeDefinition(int from, int to, bool bidirectional = true, EdgeType type = EdgeType.Normal)
        {
            FromNodeId = from;
            ToNodeId = to;
            IsBidirectional = bidirectional;
            Type = type;
        }
    }

    /// <summary>
    /// Runtime state of an edge (can be destroyed or modified).
    /// </summary>
    [Serializable]
    public struct EdgeState
    {
        public int FromNodeId;
        public int ToNodeId;
        public bool IsActive;
        public EdgeType CurrentType;

        public static EdgeState FromDefinition(EdgeDefinition def)
        {
            return new EdgeState
            {
                FromNodeId = def.FromNodeId,
                ToNodeId = def.ToNodeId,
                IsActive = true,
                CurrentType = def.Type
            };
        }
    }

    public enum EdgeType
    {
        Normal,
        OneWay,
        Blocked,      // Temporarily impassable
        Hazardous     // Damages units passing through
    }
}

