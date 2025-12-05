using UnityEngine;
using System.Collections.Generic;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Generates flat cylinder meshes (tokens) with UV-mapped top for PNG textures.
    /// Used for the Token2D render mode.
    /// </summary>
    public static class Token2DMeshGenerator
    {
        private const int SEGMENTS = 32; // Circular smoothness
        private const float DEFAULT_HEIGHT = 0.1f;
        private const float DEFAULT_RADIUS = 0.4f;

        /// <summary>
        /// Generates a flat cylinder mesh suitable for displaying a texture on top.
        /// </summary>
        public static Mesh GenerateTokenMesh(float radius = DEFAULT_RADIUS, float height = DEFAULT_HEIGHT)
        {
            var mesh = new Mesh();
            mesh.name = "Token2D";

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            // === TOP FACE ===
            // Center vertex
            int topCenterIndex = vertices.Count;
            vertices.Add(new Vector3(0, height, 0));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));

            // Edge vertices for top
            int topEdgeStart = vertices.Count;
            for (int i = 0; i <= SEGMENTS; i++)
            {
                float angle = (i / (float)SEGMENTS) * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                vertices.Add(new Vector3(x, height, z));
                normals.Add(Vector3.up);
                // UV maps texture to fill the top circle
                uvs.Add(new Vector2(0.5f + Mathf.Cos(angle) * 0.5f, 0.5f + Mathf.Sin(angle) * 0.5f));
            }

            // Top face triangles (fan from center)
            for (int i = 0; i < SEGMENTS; i++)
            {
                triangles.Add(topCenterIndex);
                triangles.Add(topEdgeStart + i);
                triangles.Add(topEdgeStart + i + 1);
            }

            // === BOTTOM FACE ===
            int bottomCenterIndex = vertices.Count;
            vertices.Add(new Vector3(0, 0, 0));
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            int bottomEdgeStart = vertices.Count;
            for (int i = 0; i <= SEGMENTS; i++)
            {
                float angle = (i / (float)SEGMENTS) * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;

                vertices.Add(new Vector3(x, 0, z));
                normals.Add(Vector3.down);
                uvs.Add(new Vector2(0.5f + Mathf.Cos(angle) * 0.5f, 0.5f + Mathf.Sin(angle) * 0.5f));
            }

            // Bottom face triangles (fan from center, reversed winding)
            for (int i = 0; i < SEGMENTS; i++)
            {
                triangles.Add(bottomCenterIndex);
                triangles.Add(bottomEdgeStart + i + 1);
                triangles.Add(bottomEdgeStart + i);
            }

            // === SIDE FACES ===
            int sideStart = vertices.Count;
            for (int i = 0; i <= SEGMENTS; i++)
            {
                float angle = (i / (float)SEGMENTS) * Mathf.PI * 2f;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                Vector3 normal = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));

                // Top edge vertex
                vertices.Add(new Vector3(x, height, z));
                normals.Add(normal);
                uvs.Add(new Vector2(i / (float)SEGMENTS, 1));

                // Bottom edge vertex
                vertices.Add(new Vector3(x, 0, z));
                normals.Add(normal);
                uvs.Add(new Vector2(i / (float)SEGMENTS, 0));
            }

            // Side triangles
            for (int i = 0; i < SEGMENTS; i++)
            {
                int topLeft = sideStart + i * 2;
                int bottomLeft = sideStart + i * 2 + 1;
                int topRight = sideStart + (i + 1) * 2;
                int bottomRight = sideStart + (i + 1) * 2 + 1;

                // Two triangles per quad
                triangles.Add(topLeft);
                triangles.Add(topRight);
                triangles.Add(bottomLeft);

                triangles.Add(topRight);
                triangles.Add(bottomRight);
                triangles.Add(bottomLeft);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// Creates a complete GameObject with token mesh, material, and collider.
        /// </summary>
        public static GameObject CreateTokenObject(Texture2D texture, bool isWhite, float radius = DEFAULT_RADIUS, float height = DEFAULT_HEIGHT)
        {
            var go = new GameObject("Token2D");

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.mesh = GenerateTokenMesh(radius, height);

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.material = CreateTokenMaterial(texture, isWhite);

            // Add collider for click detection
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = meshFilter.mesh;
            collider.convex = true;

            return go;
        }

        private static Material CreateTokenMaterial(Texture2D texture, bool isWhite)
        {
            // Use URP Lit shader if available, fallback to Standard
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");

            var mat = new Material(shader);

            if (texture != null)
            {
                mat.mainTexture = texture;
                mat.color = Color.white; // Don't tint the texture
            }
            else
            {
                // Fallback color if no texture
                mat.color = isWhite
                    ? new Color(0.95f, 0.92f, 0.85f)  // Ivory
                    : new Color(0.15f, 0.12f, 0.10f); // Dark wood
            }

            mat.SetFloat("_Smoothness", 0.5f);
            mat.SetFloat("_Metallic", 0.0f);

            return mat;
        }
    }
}

