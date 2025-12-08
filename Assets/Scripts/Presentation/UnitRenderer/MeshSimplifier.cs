using UnityEngine;
using System.Collections.Generic;

namespace BizarreChess.Presentation.UnitRenderer
{
    /// <summary>
    /// Mesh simplification using Quadric Error Metrics (QEM).
    /// Based on Garland & Heckbert's surface simplification algorithm.
    /// </summary>
    public static class MeshSimplifier
    {
        /// <summary>
        /// Simplify a mesh by collapsing edges based on quadric error metrics.
        /// </summary>
        /// <param name="mesh">Mesh to simplify</param>
        /// <param name="targetReduction">Target reduction ratio (0.9 = reduce to 10% of original)</param>
        /// <param name="aggressiveness">How aggressive to be (0=conservative, 1=very aggressive). Default 0.9</param>
        /// <returns>Simplified mesh</returns>
        public static Mesh Simplify(Mesh mesh, float targetReduction = 0.9f, float aggressiveness = 0.9f)
        {
            targetReduction = Mathf.Clamp01(targetReduction);
            aggressiveness = Mathf.Clamp01(aggressiveness);
            
            var vertices = new List<Vector3>(mesh.vertices);
            var triangles = new List<int>(mesh.triangles);

            if (vertices.Count < 4 || triangles.Count < 12)
                return mesh;

            int targetTriCount = Mathf.Max(4, Mathf.RoundToInt(triangles.Count / 3 * (1f - targetReduction)));
            
            // Flip threshold: ranges from -0.5 (conservative) to -0.99 (very aggressive)
            float flipThreshold = Mathf.Lerp(-0.5f, -0.99f, aggressiveness);
            
            var simplifier = new QEMSimplifier(vertices, triangles, flipThreshold);
            simplifier.Simplify(targetTriCount);

            var result = new Mesh();
            result.name = mesh.name + "_Simplified";
            
            var (newVerts, newTris) = simplifier.GetResult();
            
            if (newVerts.Count == 0 || newTris.Count == 0)
            {
                Debug.LogWarning("[MeshSimplifier] Simplification produced empty mesh, returning original");
                return mesh;
            }
            
            if (newVerts.Count > 65535)
            {
                result.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            
            result.SetVertices(newVerts);
            result.SetTriangles(newTris, 0);
            result.RecalculateNormals();
            result.RecalculateBounds();

            return result;
        }

        private class QEMSimplifier
        {
            private List<Vector3> _vertices;
            private List<int> _triangles;
            private Matrix4x4[] _quadrics;
            private HashSet<int> _deletedVertices;
            private HashSet<int> _deletedTriangles;
            private Dictionary<int, HashSet<int>> _vertexTriangles;
            private SortedSet<EdgeCollapse> _collapseQueue;
            
            // Version tracking to invalidate stale queue entries
            private int[] _vertexVersion;
            private int _globalVersion;
            
            // Flip detection threshold (more negative = more aggressive)
            private float _flipThreshold;

            public QEMSimplifier(List<Vector3> vertices, List<int> triangles, float flipThreshold = -0.7f)
            {
                _vertices = new List<Vector3>(vertices);
                _triangles = new List<int>(triangles);
                _deletedVertices = new HashSet<int>();
                _deletedTriangles = new HashSet<int>();
                _vertexTriangles = new Dictionary<int, HashSet<int>>();
                _collapseQueue = new SortedSet<EdgeCollapse>(new EdgeCollapseComparer());
                _vertexVersion = new int[vertices.Count];
                _globalVersion = 0;
                _flipThreshold = flipThreshold;

                Initialize();
            }

            private void Initialize()
            {
                int vertCount = _vertices.Count;
                int triCount = _triangles.Count / 3;

                _quadrics = new Matrix4x4[vertCount];
                for (int i = 0; i < vertCount; i++)
                {
                    _quadrics[i] = Matrix4x4.zero;
                    _vertexTriangles[i] = new HashSet<int>();
                }

                for (int t = 0; t < triCount; t++)
                {
                    int i0 = _triangles[t * 3];
                    int i1 = _triangles[t * 3 + 1];
                    int i2 = _triangles[t * 3 + 2];

                    _vertexTriangles[i0].Add(t);
                    _vertexTriangles[i1].Add(t);
                    _vertexTriangles[i2].Add(t);

                    Vector3 v0 = _vertices[i0];
                    Vector3 v1 = _vertices[i1];
                    Vector3 v2 = _vertices[i2];

                    Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                    if (normal.sqrMagnitude < 0.0001f) continue;

                    float d = -Vector3.Dot(normal, v0);
                    Matrix4x4 kp = ComputePlaneQuadric(normal.x, normal.y, normal.z, d);

                    _quadrics[i0] = AddMatrices(_quadrics[i0], kp);
                    _quadrics[i1] = AddMatrices(_quadrics[i1], kp);
                    _quadrics[i2] = AddMatrices(_quadrics[i2], kp);
                }

                // Initialize edges
                HashSet<long> processedEdges = new HashSet<long>();
                for (int t = 0; t < triCount; t++)
                {
                    int i0 = _triangles[t * 3];
                    int i1 = _triangles[t * 3 + 1];
                    int i2 = _triangles[t * 3 + 2];

                    TryAddEdge(i0, i1, processedEdges);
                    TryAddEdge(i1, i2, processedEdges);
                    TryAddEdge(i2, i0, processedEdges);
                }
            }

            private void TryAddEdge(int v0, int v1, HashSet<long> processed)
            {
                long key = GetEdgeKey(v0, v1);
                if (processed.Contains(key)) return;
                processed.Add(key);

                float cost = ComputeEdgeCost(v0, v1, out Vector3 optimalPos);
                _collapseQueue.Add(new EdgeCollapse(v0, v1, cost, optimalPos, 
                    _vertexVersion[v0], _vertexVersion[v1]));
            }

            private float ComputeEdgeCost(int v0, int v1, out Vector3 optimalPos)
            {
                Matrix4x4 q = AddMatrices(_quadrics[v0], _quadrics[v1]);

                Vector3 p0 = _vertices[v0];
                Vector3 p1 = _vertices[v1];
                Vector3 mid = (p0 + p1) * 0.5f;

                float cost0 = EvaluateQuadric(q, p0);
                float cost1 = EvaluateQuadric(q, p1);
                float costMid = EvaluateQuadric(q, mid);

                // Add edge length penalty to preserve shape
                float edgeLength = (p1 - p0).magnitude;
                float lengthPenalty = edgeLength * 0.01f;
                
                // Penalty for edges that cross different heights (preserve horizontal bands)
                // This makes the simplifier prefer collapsing horizontal edges over vertical ones
                float heightDiff = Mathf.Abs(p1.y - p0.y);
                float heightPenalty = heightDiff * 0.5f;

                float totalPenalty = lengthPenalty + heightPenalty;

                if (costMid <= cost0 && costMid <= cost1)
                {
                    optimalPos = mid;
                    return costMid + totalPenalty;
                }
                else if (cost0 <= cost1)
                {
                    optimalPos = p0;
                    return cost0 + totalPenalty;
                }
                else
                {
                    optimalPos = p1;
                    return cost1 + totalPenalty;
                }
            }

            private float EvaluateQuadric(Matrix4x4 q, Vector3 v)
            {
                Vector4 v4 = new Vector4(v.x, v.y, v.z, 1);
                Vector4 qv = q * v4;
                return Mathf.Max(0, Vector4.Dot(v4, qv));
            }

            private Matrix4x4 ComputePlaneQuadric(float a, float b, float c, float d)
            {
                Matrix4x4 kp = new Matrix4x4();
                kp[0, 0] = a * a; kp[0, 1] = a * b; kp[0, 2] = a * c; kp[0, 3] = a * d;
                kp[1, 0] = b * a; kp[1, 1] = b * b; kp[1, 2] = b * c; kp[1, 3] = b * d;
                kp[2, 0] = c * a; kp[2, 1] = c * b; kp[2, 2] = c * c; kp[2, 3] = c * d;
                kp[3, 0] = d * a; kp[3, 1] = d * b; kp[3, 2] = d * c; kp[3, 3] = d * d;
                return kp;
            }

            private Matrix4x4 AddMatrices(Matrix4x4 a, Matrix4x4 b)
            {
                Matrix4x4 result = new Matrix4x4();
                for (int i = 0; i < 4; i++)
                    for (int j = 0; j < 4; j++)
                        result[i, j] = a[i, j] + b[i, j];
                return result;
            }

            private long GetEdgeKey(int v0, int v1)
            {
                if (v0 > v1) (v0, v1) = (v1, v0);
                return ((long)v0 << 32) | (long)v1;
            }

            public void Simplify(int targetTriCount)
            {
                int currentTriCount = _triangles.Count / 3 - _deletedTriangles.Count;
                int initialTriCount = currentTriCount;
                int maxIterations = _triangles.Count; // Safety limit
                int skippedDeleted = 0, skippedStale = 0, skippedFlip = 0, performed = 0;

                Debug.Log($"[MeshSimplifier] Starting: {currentTriCount} tris, target: {targetTriCount}");

                while (currentTriCount > targetTriCount && _collapseQueue.Count > 0 && maxIterations-- > 0)
                {
                    EdgeCollapse collapse = _collapseQueue.Min;
                    _collapseQueue.Remove(collapse);

                    int v0 = collapse.V0;
                    int v1 = collapse.V1;

                    // Skip if vertices deleted
                    if (_deletedVertices.Contains(v0) || _deletedVertices.Contains(v1))
                    {
                        skippedDeleted++;
                        continue;
                    }

                    // Skip stale entries (version mismatch)
                    if (collapse.V0Version != _vertexVersion[v0] || 
                        collapse.V1Version != _vertexVersion[v1])
                    {
                        skippedStale++;
                        continue;
                    }

                    // Check if collapse would flip any triangles
                    if (WouldFlipTriangles(v0, v1, collapse.OptimalPosition))
                    {
                        skippedFlip++;
                        continue;
                    }

                    PerformCollapse(v0, v1, collapse.OptimalPosition);
                    performed++;
                    currentTriCount = _triangles.Count / 3 - _deletedTriangles.Count;
                }
                
                Debug.Log($"[MeshSimplifier] Done: {initialTriCount} -> {currentTriCount} tris. " +
                          $"Collapses: {performed}, Skipped (deleted:{skippedDeleted}, stale:{skippedStale}, flip:{skippedFlip})");
            }

            private bool WouldFlipTriangles(int vKeep, int vRemove, Vector3 newPos)
            {
                
                foreach (int t in _vertexTriangles[vRemove])
                {
                    if (_deletedTriangles.Contains(t)) continue;

                    int i0 = _triangles[t * 3];
                    int i1 = _triangles[t * 3 + 1];
                    int i2 = _triangles[t * 3 + 2];

                    // Skip triangles that will be deleted (have both vertices)
                    bool hasKeep = (i0 == vKeep || i1 == vKeep || i2 == vKeep);
                    if (hasKeep) continue;

                    // Get the other two vertices
                    Vector3 a = (i0 == vRemove) ? newPos : _vertices[i0];
                    Vector3 b = (i1 == vRemove) ? newPos : _vertices[i1];
                    Vector3 c = (i2 == vRemove) ? newPos : _vertices[i2];

                    Vector3 oldNormal = GetTriangleNormal(t);
                    Vector3 newNormal = Vector3.Cross(b - a, c - a);
                    
                    // Skip if old triangle was already degenerate
                    if (oldNormal.sqrMagnitude < 0.0000001f)
                        continue;
                    
                    // Check if new triangle is degenerate
                    if (newNormal.sqrMagnitude < 0.0000001f)
                        return true;
                    
                    // Normalize and check for flip
                    oldNormal.Normalize();
                    newNormal.Normalize();
                    
                    if (Vector3.Dot(oldNormal, newNormal) < _flipThreshold)
                        return true;
                }

                // Also check triangles using vKeep
                foreach (int t in _vertexTriangles[vKeep])
                {
                    if (_deletedTriangles.Contains(t)) continue;

                    int i0 = _triangles[t * 3];
                    int i1 = _triangles[t * 3 + 1];
                    int i2 = _triangles[t * 3 + 2];

                    // Skip triangles that will be deleted
                    bool hasRemove = (i0 == vRemove || i1 == vRemove || i2 == vRemove);
                    if (hasRemove) continue;

                    Vector3 a = (i0 == vKeep) ? newPos : _vertices[i0];
                    Vector3 b = (i1 == vKeep) ? newPos : _vertices[i1];
                    Vector3 c = (i2 == vKeep) ? newPos : _vertices[i2];

                    Vector3 oldNormal = GetTriangleNormal(t);
                    Vector3 newNormal = Vector3.Cross(b - a, c - a);
                    
                    if (oldNormal.sqrMagnitude < 0.0000001f)
                        continue;
                    
                    if (newNormal.sqrMagnitude < 0.0000001f)
                        return true;
                    
                    oldNormal.Normalize();
                    newNormal.Normalize();
                    
                    if (Vector3.Dot(oldNormal, newNormal) < _flipThreshold)
                        return true;
                }

                return false;
            }

            private Vector3 GetTriangleNormal(int t)
            {
                Vector3 v0 = _vertices[_triangles[t * 3]];
                Vector3 v1 = _vertices[_triangles[t * 3 + 1]];
                Vector3 v2 = _vertices[_triangles[t * 3 + 2]];
                return Vector3.Cross(v1 - v0, v2 - v0);
            }

            private void PerformCollapse(int vKeep, int vRemove, Vector3 newPos)
            {
                _globalVersion++;
                
                _vertices[vKeep] = newPos;
                _quadrics[vKeep] = AddMatrices(_quadrics[vKeep], _quadrics[vRemove]);
                _vertexVersion[vKeep] = _globalVersion;

                var trisToDelete = new List<int>();
                var trisToUpdate = new List<int>();

                foreach (int t in _vertexTriangles[vRemove])
                {
                    if (_deletedTriangles.Contains(t)) continue;

                    int i0 = _triangles[t * 3];
                    int i1 = _triangles[t * 3 + 1];
                    int i2 = _triangles[t * 3 + 2];

                    bool hasKeep = (i0 == vKeep || i1 == vKeep || i2 == vKeep);

                    if (hasKeep)
                        trisToDelete.Add(t);
                    else
                        trisToUpdate.Add(t);
                }

                foreach (int t in trisToDelete)
                {
                    _deletedTriangles.Add(t);
                }

                foreach (int t in trisToUpdate)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (_triangles[t * 3 + i] == vRemove)
                            _triangles[t * 3 + i] = vKeep;
                    }
                    _vertexTriangles[vKeep].Add(t);
                }

                _deletedVertices.Add(vRemove);
                UpdateEdgesForVertex(vKeep);
            }

            private void UpdateEdgesForVertex(int v)
            {
                HashSet<int> neighbors = new HashSet<int>();

                foreach (int t in _vertexTriangles[v])
                {
                    if (_deletedTriangles.Contains(t)) continue;

                    for (int i = 0; i < 3; i++)
                    {
                        int vi = _triangles[t * 3 + i];
                        if (vi != v && !_deletedVertices.Contains(vi))
                            neighbors.Add(vi);
                    }
                }

                foreach (int n in neighbors)
                {
                    float cost = ComputeEdgeCost(v, n, out Vector3 optPos);
                    _collapseQueue.Add(new EdgeCollapse(v, n, cost, optPos,
                        _vertexVersion[v], _vertexVersion[n]));
                }
            }

            public (List<Vector3>, List<int>) GetResult()
            {
                var newVertices = new List<Vector3>();
                var vertexMap = new Dictionary<int, int>();

                for (int i = 0; i < _vertices.Count; i++)
                {
                    if (!_deletedVertices.Contains(i))
                    {
                        vertexMap[i] = newVertices.Count;
                        newVertices.Add(_vertices[i]);
                    }
                }

                var newTriangles = new List<int>();
                int triCount = _triangles.Count / 3;

                for (int t = 0; t < triCount; t++)
                {
                    if (_deletedTriangles.Contains(t)) continue;

                    int i0 = _triangles[t * 3];
                    int i1 = _triangles[t * 3 + 1];
                    int i2 = _triangles[t * 3 + 2];

                    if (i0 == i1 || i1 == i2 || i2 == i0) continue;
                    if (!vertexMap.ContainsKey(i0) || !vertexMap.ContainsKey(i1) || !vertexMap.ContainsKey(i2))
                        continue;

                    newTriangles.Add(vertexMap[i0]);
                    newTriangles.Add(vertexMap[i1]);
                    newTriangles.Add(vertexMap[i2]);
                }

                return (newVertices, newTriangles);
            }
        }

        private struct EdgeCollapse
        {
            public int V0;
            public int V1;
            public float Cost;
            public Vector3 OptimalPosition;
            public int V0Version;
            public int V1Version;

            public EdgeCollapse(int v0, int v1, float cost, Vector3 optPos, int v0Ver, int v1Ver)
            {
                V0 = v0;
                V1 = v1;
                Cost = cost;
                OptimalPosition = optPos;
                V0Version = v0Ver;
                V1Version = v1Ver;
            }
        }

        private class EdgeCollapseComparer : IComparer<EdgeCollapse>
        {
            public int Compare(EdgeCollapse a, EdgeCollapse b)
            {
                int costCompare = a.Cost.CompareTo(b.Cost);
                if (costCompare != 0) return costCompare;

                int v0Compare = a.V0.CompareTo(b.V0);
                if (v0Compare != 0) return v0Compare;
                
                int v1Compare = a.V1.CompareTo(b.V1);
                if (v1Compare != 0) return v1Compare;
                
                // Include versions in comparison for uniqueness
                int ver0Compare = a.V0Version.CompareTo(b.V0Version);
                if (ver0Compare != 0) return ver0Compare;

                return a.V1Version.CompareTo(b.V1Version);
            }
        }
    }
}
