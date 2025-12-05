using UnityEngine;
using System.Collections.Generic;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Generates 3D chess piece meshes using rotational symmetry (lathe/revolution).
    /// Each piece is defined by a 2D profile that gets rotated around the Y axis.
    /// </summary>
    public static class ChessPieceMeshGenerator
    {
        private const int SEGMENTS = 24; // Rotational segments (smoothness)

        #region Public API

        public static Mesh GeneratePawnMesh()
        {
            // Simple rounded shape
            var profile = new List<Vector2>
            {
                new Vector2(0.00f, 0.00f),  // Base center
                new Vector2(0.35f, 0.00f),  // Base edge
                new Vector2(0.35f, 0.05f),  // Base top
                new Vector2(0.30f, 0.08f),
                new Vector2(0.25f, 0.10f),  // Neck start
                new Vector2(0.12f, 0.25f),  // Neck
                new Vector2(0.10f, 0.35f),
                new Vector2(0.12f, 0.45f),  // Collar
                new Vector2(0.20f, 0.50f),
                new Vector2(0.22f, 0.55f),  // Head start
                new Vector2(0.20f, 0.65f),
                new Vector2(0.15f, 0.72f),
                new Vector2(0.08f, 0.78f),
                new Vector2(0.00f, 0.80f),  // Top
            };
            return GenerateLatheMesh(profile, "Pawn");
        }

        public static Mesh GenerateRookMesh()
        {
            // Castle tower shape with battlements
            var profile = new List<Vector2>
            {
                new Vector2(0.00f, 0.00f),
                new Vector2(0.38f, 0.00f),  // Base
                new Vector2(0.38f, 0.06f),
                new Vector2(0.32f, 0.10f),
                new Vector2(0.28f, 0.15f),  // Neck
                new Vector2(0.25f, 0.50f),  // Tower body
                new Vector2(0.28f, 0.55f),
                new Vector2(0.32f, 0.58f),  // Crown base
                new Vector2(0.32f, 0.75f),  // Crown top (battlements simulated)
                new Vector2(0.25f, 0.75f),  // Battlement indent
                new Vector2(0.25f, 0.68f),
                new Vector2(0.18f, 0.68f),
                new Vector2(0.18f, 0.75f),
                new Vector2(0.00f, 0.75f),  // Top center
            };
            return GenerateLatheMesh(profile, "Rook");
        }

        public static Mesh GenerateKnightMesh()
        {
            // Knight is tricky - using a stylized symmetric version
            // More like a chess piece silhouette than a horse
            var profile = new List<Vector2>
            {
                new Vector2(0.00f, 0.00f),
                new Vector2(0.35f, 0.00f),
                new Vector2(0.35f, 0.05f),
                new Vector2(0.28f, 0.10f),
                new Vector2(0.20f, 0.15f),
                new Vector2(0.15f, 0.30f),  // Neck
                new Vector2(0.12f, 0.45f),
                new Vector2(0.15f, 0.55f),
                new Vector2(0.22f, 0.65f),  // Head area
                new Vector2(0.25f, 0.75f),
                new Vector2(0.22f, 0.85f),  // Mane/top
                new Vector2(0.15f, 0.92f),
                new Vector2(0.08f, 0.96f),
                new Vector2(0.00f, 0.98f),
            };
            return GenerateLatheMesh(profile, "Knight");
        }

        public static Mesh GenerateBishopMesh()
        {
            // Tall piece with pointed top (miter)
            var profile = new List<Vector2>
            {
                new Vector2(0.00f, 0.00f),
                new Vector2(0.35f, 0.00f),
                new Vector2(0.35f, 0.05f),
                new Vector2(0.30f, 0.08f),
                new Vector2(0.22f, 0.12f),
                new Vector2(0.15f, 0.25f),  // Neck
                new Vector2(0.12f, 0.40f),
                new Vector2(0.10f, 0.55f),
                new Vector2(0.12f, 0.62f),  // Collar
                new Vector2(0.18f, 0.68f),
                new Vector2(0.20f, 0.75f),  // Head
                new Vector2(0.18f, 0.85f),
                new Vector2(0.12f, 0.92f),
                new Vector2(0.05f, 0.98f),  // Point
                new Vector2(0.00f, 1.02f),  // Tip
            };
            return GenerateLatheMesh(profile, "Bishop");
        }

        public static Mesh GenerateQueenMesh()
        {
            // Tall elegant piece with crown
            var profile = new List<Vector2>
            {
                new Vector2(0.00f, 0.00f),
                new Vector2(0.40f, 0.00f),
                new Vector2(0.40f, 0.06f),
                new Vector2(0.35f, 0.10f),
                new Vector2(0.28f, 0.15f),
                new Vector2(0.18f, 0.30f),  // Neck
                new Vector2(0.14f, 0.50f),
                new Vector2(0.12f, 0.65f),
                new Vector2(0.15f, 0.72f),  // Collar
                new Vector2(0.22f, 0.78f),
                new Vector2(0.25f, 0.85f),  // Crown base
                new Vector2(0.28f, 0.92f),
                new Vector2(0.25f, 0.98f),  // Crown points
                new Vector2(0.18f, 1.02f),
                new Vector2(0.12f, 1.08f),
                new Vector2(0.08f, 1.12f),
                new Vector2(0.00f, 1.15f),  // Ball on top
            };
            return GenerateLatheMesh(profile, "Queen");
        }

        public static Mesh GenerateKingMesh()
        {
            // Tallest piece with cross on top
            var profile = new List<Vector2>
            {
                new Vector2(0.00f, 0.00f),
                new Vector2(0.42f, 0.00f),
                new Vector2(0.42f, 0.06f),
                new Vector2(0.36f, 0.10f),
                new Vector2(0.30f, 0.15f),
                new Vector2(0.20f, 0.30f),
                new Vector2(0.16f, 0.50f),
                new Vector2(0.14f, 0.68f),
                new Vector2(0.18f, 0.75f),  // Collar
                new Vector2(0.24f, 0.80f),
                new Vector2(0.26f, 0.88f),  // Crown
                new Vector2(0.22f, 0.95f),
                new Vector2(0.15f, 1.00f),
                new Vector2(0.10f, 1.05f),
                // Cross base
                new Vector2(0.06f, 1.08f),
                new Vector2(0.06f, 1.20f),  // Cross vertical
                new Vector2(0.00f, 1.25f),
            };
            return GenerateLatheMesh(profile, "King");
        }

        #endregion

        #region Mesh Generation

        private static Mesh GenerateLatheMesh(List<Vector2> profile, string name)
        {
            var mesh = new Mesh();
            mesh.name = name;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            int profileCount = profile.Count;

            // Generate vertices by rotating the profile
            for (int seg = 0; seg <= SEGMENTS; seg++)
            {
                float angle = (seg / (float)SEGMENTS) * Mathf.PI * 2f;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);

                for (int p = 0; p < profileCount; p++)
                {
                    float x = profile[p].x * cos;
                    float y = profile[p].y;
                    float z = profile[p].x * sin;

                    vertices.Add(new Vector3(x, y, z));

                    // Calculate normal (perpendicular to surface)
                    Vector2 tangent;
                    if (p == 0)
                        tangent = profile[1] - profile[0];
                    else if (p == profileCount - 1)
                        tangent = profile[p] - profile[p - 1];
                    else
                        tangent = profile[p + 1] - profile[p - 1];

                    Vector2 normal2D = new Vector2(tangent.y, -tangent.x).normalized;
                    Vector3 normal = new Vector3(normal2D.x * cos, normal2D.y, normal2D.x * sin).normalized;
                    normals.Add(normal);

                    // UVs
                    uvs.Add(new Vector2(seg / (float)SEGMENTS, p / (float)(profileCount - 1)));
                }
            }

            // Generate triangles
            for (int seg = 0; seg < SEGMENTS; seg++)
            {
                for (int p = 0; p < profileCount - 1; p++)
                {
                    int current = seg * profileCount + p;
                    int next = (seg + 1) * profileCount + p;

                    // Two triangles per quad
                    triangles.Add(current);
                    triangles.Add(next);
                    triangles.Add(current + 1);

                    triangles.Add(next);
                    triangles.Add(next + 1);
                    triangles.Add(current + 1);
                }
            }

            // Bottom cap
            int bottomCenter = vertices.Count;
            vertices.Add(Vector3.zero);
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int seg = 0; seg < SEGMENTS; seg++)
            {
                int current = seg * profileCount;
                int next = (seg + 1) * profileCount;
                triangles.Add(bottomCenter);
                triangles.Add(next);
                triangles.Add(current);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            return mesh;
        }

        #endregion

        #region Utility

        public static Mesh GetMeshForPieceType(Core.Units.PieceType type)
        {
            return type switch
            {
                Core.Units.PieceType.Pawn => GeneratePawnMesh(),
                Core.Units.PieceType.Rook => GenerateRookMesh(),
                Core.Units.PieceType.Knight => GenerateKnightMesh(),
                Core.Units.PieceType.Bishop => GenerateBishopMesh(),
                Core.Units.PieceType.Queen => GenerateQueenMesh(),
                Core.Units.PieceType.King => GenerateKingMesh(),
                _ => GeneratePawnMesh()
            };
        }

        /// <summary>
        /// Creates a complete GameObject with mesh, material, and collider.
        /// </summary>
        public static GameObject CreatePieceObject(Core.Units.PieceType type, bool isWhite)
        {
            var go = new GameObject(type.ToString());
            
            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.mesh = GetMeshForPieceType(type);

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.material = CreatePieceMaterial(isWhite);

            // Add collider for click detection
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = meshFilter.mesh;
            collider.convex = true;

            // Scale to fit on tile (pieces are ~1 unit tall in profile)
            go.transform.localScale = Vector3.one * 0.8f;

            return go;
        }

        private static Material CreatePieceMaterial(bool isWhite)
        {
            // Use URP Lit shader if available, fallback to Standard
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") 
                         ?? Shader.Find("Standard");
            
            var mat = new Material(shader);
            
            if (isWhite)
            {
                mat.color = new Color(0.95f, 0.92f, 0.85f); // Ivory white
            }
            else
            {
                mat.color = new Color(0.15f, 0.12f, 0.10f); // Dark wood
            }

            // Add some smoothness for that polished look
            mat.SetFloat("_Smoothness", 0.7f);
            mat.SetFloat("_Metallic", 0.0f);

            return mat;
        }

        #endregion
    }
}

