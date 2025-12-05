using UnityEngine;
using System.Collections.Generic;

namespace BizarreChess.Presentation
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
        /// <returns>Simplified mesh</returns>
        public static Mesh Simplify(Mesh mesh, float targetReduction = 0.9f)
        {
            targetReduction = Mathf.Clamp01(targetReduction);
            
            var vertices = new List<Vector3>(mesh.vertices);
            var triangles = new List<int>(mesh.triangles);

            if (vertices.Count < 4 || triangles.Count < 12)
                return mesh; // Too small to simplify

            int targetTriCount = Mathf.Max(4, Mathf.RoundToInt(triangles.Count / 3 * (1f - targetReduction)));
            
            var simplifier = new QEMSimplifier(vertices, triangles);
            simplifier.Simplify(targetTriCount);

            var result = new Mesh();
            result.name = mesh.name + "_Simplified";
            
            var (newVerts, newTris) = simplifier.GetResult();
            
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
            private Dictionary<int, HashSet<int>> _vertexTriangles; // vertex -> triangles using it
            private Dictionary<long, float> _edgeCosts;
            private SortedSet<EdgeCollapse> _collapseQueue;

            public QEMSimplifier(List<Vector3> vertices, List<int> triangles)
            {
                _vertices = new List<Vector3>(vertices);
                _triangles = new List<int>(triangles);
                _deletedVertices = new HashSet<int>();
                _deletedTriangles = new HashSet<int>();
                _vertexTriangles = new Dictionary<int, HashSet<int>>();
                _edgeCosts = new Dictionary<long, float>();
                _collapseQueue = new SortedSet<EdgeCollapse>(new EdgeCollapseComparer());

                Initialize();
            }

            private void Initialize()
            {
                int vertCount = _vertices.Count;
                int triCount = _triangles.Count / 3;

                // Initialize quadrics to zero
                _quadrics = new Matrix4x4[vertCount];
                for (int i = 0; i < vertCount; i++)
                {
                    _quadrics[i] = Matrix4x4.zero;
                }

                // Build vertex-triangle adjacency and compute quadrics
                for (int i = 0; i < vertCount; i++)
                {
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

                    // Compute plane equation for this triangle
                    Vector3 v0 = _vertices[i0];
                    Vector3 v1 = _vertices[i1];
                    Vector3 v2 = _vertices[i2];

                    Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                    if (normal.sqrMagnitude < 0.0001f) continue;

                    float d = -Vector3.Dot(normal, v0);

                    // Build fundamental quadric Kp = pp^T where p = (a, b, c, d)
                    Matrix4x4 kp = ComputePlaneQuadric(normal.x, normal.y, normal.z, d);

                    // Add to vertex quadrics
                    _quadrics[i0] = AddMatrices(_quadrics[i0], kp);
                    _quadrics[i1] = AddMatrices(_quadrics[i1], kp);
                    _quadrics[i2] = AddMatrices(_quadrics[i2], kp);
                }

                // Initialize edge collapse costs
                HashSet<long> processedEdges = new HashSet<long>();
                for (int t = 0; t < triCount; t++)
                {
                    int i0 = _triangles[t * 3];
                    int i1 = _triangles[t * 3 + 1];
                    int i2 = _triangles[t * 3 + 2];

                    AddEdgeIfNew(i0, i1, processedEdges);
                    AddEdgeIfNew(i1, i2, processedEdges);
                    AddEdgeIfNew(i2, i0, processedEdges);
                }
            }

            private void AddEdgeIfNew(int v0, int v1, HashSet<long> processed)
            {
                long key = GetEdgeKey(v0, v1);
                if (processed.Contains(key)) return;
                processed.Add(key);

                float cost = ComputeEdgeCost(v0, v1, out Vector3 optimalPos);
                _edgeCosts[key] = cost;
                _collapseQueue.Add(new EdgeCollapse(v0, v1, cost, optimalPos));
            }

            private float ComputeEdgeCost(int v0, int v1, out Vector3 optimalPos)
            {
                Matrix4x4 q = AddMatrices(_quadrics[v0], _quadrics[v1]);

                // Try to find optimal position by solving the linear system
                // For simplicity, we'll use the midpoint or one of the endpoints
                Vector3 p0 = _vertices[v0];
                Vector3 p1 = _vertices[v1];
                Vector3 mid = (p0 + p1) * 0.5f;

                float cost0 = EvaluateQuadric(q, p0);
                float cost1 = EvaluateQuadric(q, p1);
                float costMid = EvaluateQuadric(q, mid);

                if (costMid <= cost0 && costMid <= cost1)
                {
                    optimalPos = mid;
                    return costMid;
                }
                else if (cost0 <= cost1)
                {
                    optimalPos = p0;
                    return cost0;
                }
                else
                {
                    optimalPos = p1;
                    return cost1;
                }
            }

            private float EvaluateQuadric(Matrix4x4 q, Vector3 v)
            {
                // Q(v) = v^T * Q * v where v is (x, y, z, 1)
                Vector4 v4 = new Vector4(v.x, v.y, v.z, 1);
                Vector4 qv = q * v4;
                return Vector4.Dot(v4, qv);
            }

            private Matrix4x4 ComputePlaneQuadric(float a, float b, float c, float d)
            {
                // Kp[i,j] = p[i] * p[j] where p = (a, b, c, d)
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

                while (currentTriCount > targetTriCount && _collapseQueue.Count > 0)
                {
                    // Get minimum cost edge
                    EdgeCollapse collapse = _collapseQueue.Min;
                    _collapseQueue.Remove(collapse);

                    int v0 = collapse.V0;
                    int v1 = collapse.V1;

                    // Skip if either vertex was deleted
                    if (_deletedVertices.Contains(v0) || _deletedVertices.Contains(v1))
                        continue;

                    // Perform collapse: merge v1 into v0
                    PerformCollapse(v0, v1, collapse.OptimalPosition);

                    currentTriCount = _triangles.Count / 3 - _deletedTriangles.Count;
                }
            }

            private void PerformCollapse(int vKeep, int vRemove, Vector3 newPos)
            {
                // Update position
                _vertices[vKeep] = newPos;

                // Update quadric
                _quadrics[vKeep] = AddMatrices(_quadrics[vKeep], _quadrics[vRemove]);

                // Find triangles to delete (those with both vertices)
                // and triangles to update (those with vRemove but not vKeep)
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
                    {
                        // Triangle has both vertices - becomes degenerate
                        trisToDelete.Add(t);
                    }
                    else
                    {
                        // Triangle only has vRemove - update it
                        trisToUpdate.Add(t);
                    }
                }

                // Delete degenerate triangles
                foreach (int t in trisToDelete)
                {
                    _deletedTriangles.Add(t);
                }

                // Update triangles: replace vRemove with vKeep
                foreach (int t in trisToUpdate)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (_triangles[t * 3 + i] == vRemove)
                        {
                            _triangles[t * 3 + i] = vKeep;
                        }
                    }
                    _vertexTriangles[vKeep].Add(t);
                }

                // Mark vRemove as deleted
                _deletedVertices.Add(vRemove);

                // Update edges for vKeep's neighborhood
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
                        {
                            neighbors.Add(vi);
                        }
                    }
                }

                foreach (int n in neighbors)
                {
                    float cost = ComputeEdgeCost(v, n, out Vector3 optPos);
                    _collapseQueue.Add(new EdgeCollapse(v, n, cost, optPos));
                }
            }

            public (List<Vector3>, List<int>) GetResult()
            {
                // Compact vertices and triangles
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

                    // Skip degenerate triangles
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

            public EdgeCollapse(int v0, int v1, float cost, Vector3 optPos)
            {
                V0 = v0;
                V1 = v1;
                Cost = cost;
                OptimalPosition = optPos;
            }
        }

        private class EdgeCollapseComparer : IComparer<EdgeCollapse>
        {
            public int Compare(EdgeCollapse a, EdgeCollapse b)
            {
                int costCompare = a.Cost.CompareTo(b.Cost);
                if (costCompare != 0) return costCompare;

                // Tie-breaker to ensure uniqueness in SortedSet
                int v0Compare = a.V0.CompareTo(b.V0);
                if (v0Compare != 0) return v0Compare;

                return a.V1.CompareTo(b.V1);
            }
        }
    }
}

