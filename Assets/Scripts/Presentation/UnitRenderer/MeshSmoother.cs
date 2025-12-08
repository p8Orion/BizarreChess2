using UnityEngine;
using System.Collections.Generic;

namespace BizarreChess.Presentation.UnitRenderer
{
    /// <summary>
    /// Post-processing for marching cubes meshes.
    /// Welds duplicate vertices and recalculates smooth normals.
    /// </summary>
    public static class MeshSmoother
    {
        /// <summary>
        /// Weld duplicate vertices and recalculate smooth normals.
        /// </summary>
        /// <param name="mesh">Input mesh (will not be modified)</param>
        /// <param name="weldThreshold">Distance threshold for welding vertices</param>
        /// <returns>New mesh with welded vertices and smooth normals</returns>
        public static Mesh WeldAndSmooth(Mesh mesh, float weldThreshold = 0.0001f)
        {
            var oldVertices = mesh.vertices;
            var oldTriangles = mesh.triangles;

            if (oldVertices.Length == 0 || oldTriangles.Length == 0)
                return mesh;

            // Step 1: Weld vertices that are at the same position
            var (newVertices, vertexRemap) = WeldVertices(oldVertices, weldThreshold);

            // Step 2: Remap triangle indices
            var newTriangles = new int[oldTriangles.Length];
            for (int i = 0; i < oldTriangles.Length; i++)
            {
                newTriangles[i] = vertexRemap[oldTriangles[i]];
            }

            // Step 3: Remove degenerate triangles
            var cleanTriangles = RemoveDegenerateTriangles(newTriangles);

            // Step 4: Create new mesh
            var result = new Mesh();
            result.name = mesh.name + "_Smooth";

            if (newVertices.Count > 65535)
            {
                result.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            result.SetVertices(newVertices);
            result.SetTriangles(cleanTriangles, 0);
            
            // Step 5: Recalculate normals (now vertices are properly shared)
            result.RecalculateNormals();
            result.RecalculateBounds();

            return result;
        }

        /// <summary>
        /// Weld vertices that are within threshold distance of each other.
        /// Uses spatial hashing for O(n) performance.
        /// </summary>
        private static (List<Vector3> vertices, int[] remap) WeldVertices(
            Vector3[] vertices, float threshold)
        {
            var newVertices = new List<Vector3>();
            var remap = new int[vertices.Length];
            
            // Spatial hash for fast lookup
            float cellSize = threshold * 2f;
            var spatialHash = new Dictionary<long, List<int>>();

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                long hash = GetSpatialHash(v, cellSize);

                int existingIndex = -1;

                // Check this cell and neighbors for existing vertex
                for (int dx = -1; dx <= 1 && existingIndex < 0; dx++)
                {
                    for (int dy = -1; dy <= 1 && existingIndex < 0; dy++)
                    {
                        for (int dz = -1; dz <= 1 && existingIndex < 0; dz++)
                        {
                            long neighborHash = GetSpatialHashOffset(v, cellSize, dx, dy, dz);
                            
                            if (spatialHash.TryGetValue(neighborHash, out var candidates))
                            {
                                foreach (int candidateIdx in candidates)
                                {
                                    if ((newVertices[candidateIdx] - v).sqrMagnitude <= threshold * threshold)
                                    {
                                        existingIndex = candidateIdx;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                if (existingIndex >= 0)
                {
                    // Reuse existing vertex
                    remap[i] = existingIndex;
                }
                else
                {
                    // Add new vertex
                    int newIndex = newVertices.Count;
                    newVertices.Add(v);
                    remap[i] = newIndex;

                    // Add to spatial hash
                    if (!spatialHash.TryGetValue(hash, out var list))
                    {
                        list = new List<int>();
                        spatialHash[hash] = list;
                    }
                    list.Add(newIndex);
                }
            }

            return (newVertices, remap);
        }

        private static long GetSpatialHash(Vector3 v, float cellSize)
        {
            int x = Mathf.FloorToInt(v.x / cellSize);
            int y = Mathf.FloorToInt(v.y / cellSize);
            int z = Mathf.FloorToInt(v.z / cellSize);
            
            // Pack into 64-bit hash (21 bits per component)
            const long mask = 0x1FFFFF; // 21 bits
            return ((long)(x & mask) << 42) | ((long)(y & mask) << 21) | (long)(z & mask);
        }

        private static long GetSpatialHashOffset(Vector3 v, float cellSize, int dx, int dy, int dz)
        {
            int x = Mathf.FloorToInt(v.x / cellSize) + dx;
            int y = Mathf.FloorToInt(v.y / cellSize) + dy;
            int z = Mathf.FloorToInt(v.z / cellSize) + dz;
            
            const long mask = 0x1FFFFF;
            return ((long)(x & mask) << 42) | ((long)(y & mask) << 21) | (long)(z & mask);
        }

        /// <summary>
        /// Remove triangles where two or more vertices are the same.
        /// </summary>
        private static List<int> RemoveDegenerateTriangles(int[] triangles)
        {
            var result = new List<int>(triangles.Length);

            for (int i = 0; i < triangles.Length; i += 3)
            {
                int i0 = triangles[i];
                int i1 = triangles[i + 1];
                int i2 = triangles[i + 2];

                // Skip degenerate triangles
                if (i0 != i1 && i1 != i2 && i2 != i0)
                {
                    result.Add(i0);
                    result.Add(i1);
                    result.Add(i2);
                }
            }

            return result;
        }
    }
}
