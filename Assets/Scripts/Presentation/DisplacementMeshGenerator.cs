using UnityEngine;
using System.Collections.Generic;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Generates 3D cylinder meshes from displacement textures.
    /// Texture encoding:
    /// - Alpha channel: radius at each angle (0=no radius, 1=max radius)
    /// - Green (0,1,0): primary player color region
    /// - Magenta (1,0,1): secondary player color region
    /// - Other RGB: static colors (accents that don't change per player)
    /// </summary>
    public static class DisplacementMeshGenerator
    {
        private const int HEIGHT_SEGMENTS = 32; // Vertical resolution
        private const float MAX_RADIUS = 0.4f;
        private const float MIN_RADIUS = 0f; // Allow zero radius for fully transparent areas

        // Color detection thresholds
        private const float GREEN_THRESHOLD = 0.8f;
        private const float MAGENTA_THRESHOLD = 0.8f;

        /// <summary>
        /// Generates a 3D mesh from a displacement texture.
        /// </summary>
        /// <param name="texture">Displacement texture</param>
        /// <param name="height">Total height of the piece</param>
        /// <returns>Generated mesh</returns>
        public static Mesh GenerateDisplacementMesh(Texture2D texture, float height)
        {
            if (texture == null)
            {
                Debug.LogWarning("[DisplacementMeshGenerator] No texture provided, using default cylinder");
                return GenerateDefaultCylinder(height);
            }

            // Make texture readable if it isn't
            Texture2D readableTexture = GetReadableTexture(texture);

            var mesh = new Mesh();
            mesh.name = "DisplacementPiece";

            int segments = readableTexture.width; // Use texture width as angular resolution
            int heightSamples = Mathf.Min(readableTexture.height, HEIGHT_SEGMENTS);

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color>(); // Store original texture colors for shader
            var triangles = new List<int>();

            // Sample the texture to build the profile
            // X axis of texture = angle around Y axis
            // Y axis of texture = height
            for (int h = 0; h <= heightSamples; h++)
            {
                float vCoord = h / (float)heightSamples;
                float y = vCoord * height;
                int texY = Mathf.Clamp(Mathf.RoundToInt(vCoord * (readableTexture.height - 1)), 0, readableTexture.height - 1);

                for (int s = 0; s <= segments; s++)
                {
                    float uCoord = s / (float)segments;
                    float angle = uCoord * Mathf.PI * 2f;
                    int texX = Mathf.Clamp(Mathf.RoundToInt(uCoord * (readableTexture.width - 1)), 0, readableTexture.width - 1);

                    Color pixel = readableTexture.GetPixel(texX, texY);

                    // Alpha determines radius
                    float radius = Mathf.Lerp(MIN_RADIUS, MAX_RADIUS, pixel.a);

                    float x = Mathf.Cos(angle) * radius;
                    float z = Mathf.Sin(angle) * radius;

                    vertices.Add(new Vector3(x, y, z));

                    // Calculate normal (perpendicular to surface)
                    // We'll use a simple outward-pointing normal based on angle
                    // For more accurate normals, we'd need to compute surface gradients
                    Vector3 outward = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                    normals.Add(outward.normalized);

                    // UVs for texture mapping
                    uvs.Add(new Vector2(uCoord, vCoord));

                    // Store the original pixel color (will be processed by shader)
                    colors.Add(pixel);
                }
            }

            // Generate triangles
            int vertsPerRow = segments + 1;
            for (int h = 0; h < heightSamples; h++)
            {
                for (int s = 0; s < segments; s++)
                {
                    int current = h * vertsPerRow + s;
                    int next = current + 1;
                    int above = current + vertsPerRow;
                    int aboveNext = above + 1;

                    // Two triangles per quad
                    triangles.Add(current);
                    triangles.Add(above);
                    triangles.Add(next);

                    triangles.Add(next);
                    triangles.Add(above);
                    triangles.Add(aboveNext);
                }
            }

            // === TOP CAP ===
            int topCenterIndex = vertices.Count;
            vertices.Add(new Vector3(0, height, 0));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 1f));
            colors.Add(Color.white);

            int topRowStart = heightSamples * vertsPerRow;
            for (int s = 0; s < segments; s++)
            {
                triangles.Add(topCenterIndex);
                triangles.Add(topRowStart + s);
                triangles.Add(topRowStart + s + 1);
            }

            // === BOTTOM CAP ===
            int bottomCenterIndex = vertices.Count;
            vertices.Add(new Vector3(0, 0, 0));
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0f));
            colors.Add(Color.white);

            for (int s = 0; s < segments; s++)
            {
                triangles.Add(bottomCenterIndex);
                triangles.Add(s + 1);
                triangles.Add(s);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals(); // Recalculate for smoother shading

            // Clean up temporary texture if we created one
            if (readableTexture != texture)
            {
                Object.Destroy(readableTexture);
            }

            return mesh;
        }

        /// <summary>
        /// Creates a readable copy of a texture if it's not already readable.
        /// </summary>
        private static Texture2D GetReadableTexture(Texture2D source)
        {
            // Try to read a pixel - if it works, texture is readable
            try
            {
                source.GetPixel(0, 0);
                return source; // Already readable
            }
            catch (UnityException)
            {
                // Not readable, create a copy via RenderTexture
            }

            // Create a temporary RenderTexture
            RenderTexture rt = RenderTexture.GetTemporary(
                source.width, source.height, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

            // Copy source texture to RenderTexture
            Graphics.Blit(source, rt);

            // Read pixels from RenderTexture into a new Texture2D
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Texture2D readable = new Texture2D(source.width, source.height, TextureFormat.ARGB32, false);
            readable.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readable.Apply();

            // Cleanup
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);

            return readable;
        }

        /// <summary>
        /// Creates a complete GameObject with displacement mesh and color shader.
        /// </summary>
        public static GameObject CreateDisplacementObject(Texture2D texture, bool isWhite, float height = 3f)
        {
            var go = new GameObject("DisplacementPiece");

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.mesh = GenerateDisplacementMesh(texture, height);

            var meshRenderer = go.AddComponent<MeshRenderer>();
            meshRenderer.material = CreateDisplacementMaterial(texture, isWhite);

            // Add collider for click detection
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = meshFilter.mesh;
            collider.convex = true;

            // Scale to fit on tile
            go.transform.localScale = Vector3.one * 0.8f;

            return go;
        }

        private static Material CreateDisplacementMaterial(Texture2D texture, bool isWhite)
        {
            // Try to use our custom shader first
            Shader shader = Shader.Find("BizarreChess/PieceColor")
                         ?? Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");

            var mat = new Material(shader);

            if (texture != null)
            {
                mat.mainTexture = texture;
            }

            // Set player colors that the shader will use to replace green/magenta
            Color primaryColor = isWhite
                ? new Color(0.95f, 0.92f, 0.85f)  // Ivory
                : new Color(0.15f, 0.12f, 0.10f); // Dark wood

            Color secondaryColor = isWhite
                ? new Color(0.85f, 0.82f, 0.75f)  // Slightly darker ivory
                : new Color(0.25f, 0.22f, 0.20f); // Slightly lighter dark wood

            // Set shader properties (these will be used by our custom shader)
            mat.SetColor("_PrimaryColor", primaryColor);
            mat.SetColor("_SecondaryColor", secondaryColor);

            // Fallback for standard shaders
            mat.color = primaryColor;
            mat.SetFloat("_Smoothness", 0.7f);
            mat.SetFloat("_Metallic", 0.0f);

            return mat;
        }

        /// <summary>
        /// Generates a simple cylinder as fallback when no texture is provided.
        /// </summary>
        private static Mesh GenerateDefaultCylinder(float height)
        {
            var mesh = new Mesh();
            mesh.name = "DefaultCylinder";

            const int segments = 24;
            const float radius = 0.3f;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var triangles = new List<int>();

            // Side vertices
            for (int h = 0; h <= 1; h++)
            {
                float y = h * height;
                for (int s = 0; s <= segments; s++)
                {
                    float angle = (s / (float)segments) * Mathf.PI * 2f;
                    float x = Mathf.Cos(angle) * radius;
                    float z = Mathf.Sin(angle) * radius;

                    vertices.Add(new Vector3(x, y, z));
                    normals.Add(new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)));
                    uvs.Add(new Vector2(s / (float)segments, h));
                }
            }

            // Side triangles
            int vertsPerRow = segments + 1;
            for (int s = 0; s < segments; s++)
            {
                int bl = s;
                int br = s + 1;
                int tl = s + vertsPerRow;
                int tr = s + 1 + vertsPerRow;

                triangles.Add(bl);
                triangles.Add(tl);
                triangles.Add(br);

                triangles.Add(br);
                triangles.Add(tl);
                triangles.Add(tr);
            }

            // Top cap
            int topCenter = vertices.Count;
            vertices.Add(new Vector3(0, height, 0));
            normals.Add(Vector3.up);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int s = 0; s < segments; s++)
            {
                triangles.Add(topCenter);
                triangles.Add(vertsPerRow + s);
                triangles.Add(vertsPerRow + s + 1);
            }

            // Bottom cap
            int bottomCenter = vertices.Count;
            vertices.Add(Vector3.zero);
            normals.Add(Vector3.down);
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int s = 0; s < segments; s++)
            {
                triangles.Add(bottomCenter);
                triangles.Add(s + 1);
                triangles.Add(s);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();

            return mesh;
        }
    }
}

