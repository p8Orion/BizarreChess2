using UnityEngine;

namespace BizarreChess.Presentation
{
    /// <summary>
    /// Generates 3D revolution meshes from cross-section profile textures.
    /// Uses a voxel-based approach with Marching Cubes and QEM simplification.
    /// 
    /// Pipeline:
    /// 1. Voxelize: Project PNG profile radially to create 3D voxel grid
    /// 2. Marching Cubes: Extract isosurface mesh from voxels
    /// 3. QEM Simplify: Reduce triangle count while preserving shape
    /// 
    /// The PNG is a cross-section cut from center to edge:
    /// - Y axis = height (bottom to top)
    /// - X axis = radius from center outward
    /// - Alpha >= threshold = solid material
    /// - Transparent = empty space (creates concavities)
    /// </summary>
    public static class RevolutionMeshGenerator
    {
        public const int DEFAULT_RESOLUTION = 32;
        public const float DEFAULT_SIMPLIFICATION = 0.9f;
        public const float DEFAULT_HEIGHT = 1.5f;
        public const float DEFAULT_SIZE = 1.0f;

        /// <summary>
        /// Generate a revolution mesh from a profile texture.
        /// </summary>
        /// <param name="profileTexture">PNG profile (Y=height, X=radius)</param>
        /// <param name="height">Target height of the piece</param>
        /// <param name="resolution">Voxel grid resolution (32 = 32³)</param>
        /// <param name="simplificationRatio">How much to simplify (0.9 = 90% reduction)</param>
        /// <returns>Generated and simplified mesh</returns>
        public static Mesh GenerateRevolutionMesh(
            Texture2D profileTexture, 
            float height = DEFAULT_HEIGHT,
            int resolution = DEFAULT_RESOLUTION,
            float simplificationRatio = DEFAULT_SIMPLIFICATION)
        {
            if (profileTexture == null)
            {
                Debug.LogWarning("[RevolutionMeshGenerator] No texture provided, using default shape");
                return GenerateDefaultMesh(height);
            }

            // Step 1: Voxelize the profile
            float size = DEFAULT_SIZE;
            var voxelGrid = new VoxelGrid(profileTexture, resolution, size);

            // Step 2: Extract mesh with Marching Cubes
            Mesh denseMesh = MarchingCubes.Generate(voxelGrid, 0.5f);

            if (denseMesh.vertexCount == 0)
            {
                Debug.LogWarning("[RevolutionMeshGenerator] Marching cubes produced empty mesh");
                return GenerateDefaultMesh(height);
            }

            // Step 3: Simplify with QEM
            Mesh simplifiedMesh = MeshSimplifier.Simplify(denseMesh, simplificationRatio);

            // Scale to target height
            ScaleMesh(simplifiedMesh, height / size);

            // Cleanup intermediate mesh
            if (denseMesh != simplifiedMesh)
            {
                Object.Destroy(denseMesh);
            }

            simplifiedMesh.name = "RevolutionVolume";
            return simplifiedMesh;
        }

        /// <summary>
        /// Scale mesh vertices to achieve target height.
        /// </summary>
        private static void ScaleMesh(Mesh mesh, float scale)
        {
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                vertices[i] *= scale;
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
        }

        /// <summary>
        /// Generate a default pawn-like shape when no texture is provided.
        /// </summary>
        private static Mesh GenerateDefaultMesh(float height)
        {
            // Create a simple procedural profile texture
            int texSize = 32;
            var texture = new Texture2D(texSize, texSize, TextureFormat.ARGB32, false);

            for (int y = 0; y < texSize; y++)
            {
                float t = y / (float)(texSize - 1);
                
                // Pawn profile: base -> stem -> head
                float outerR;
                float innerR = 0;

                if (t < 0.1f)
                {
                    outerR = 0.9f;
                }
                else if (t < 0.2f)
                {
                    outerR = Mathf.Lerp(0.9f, 0.4f, (t - 0.1f) / 0.1f);
                }
                else if (t < 0.6f)
                {
                    outerR = 0.35f;
                }
                else if (t < 0.7f)
                {
                    outerR = Mathf.Lerp(0.35f, 0.7f, (t - 0.6f) / 0.1f);
                }
                else
                {
                    float headT = (t - 0.7f) / 0.3f;
                    outerR = 0.7f * Mathf.Cos(headT * Mathf.PI * 0.5f);
                    outerR = Mathf.Max(outerR, 0.05f);
                }

                int outerX = Mathf.RoundToInt(outerR * (texSize - 1));
                int innerX = Mathf.RoundToInt(innerR * (texSize - 1));

                for (int x = 0; x < texSize; x++)
                {
                    bool solid = x >= innerX && x <= outerX;
                    texture.SetPixel(x, y, solid ? Color.white : Color.clear);
                }
            }

            texture.Apply();

            var mesh = GenerateRevolutionMesh(texture, height, 24, 0.85f);
            Object.Destroy(texture);

            return mesh;
        }

        /// <summary>
        /// Create a complete GameObject with revolution mesh and material.
        /// </summary>
        public static GameObject CreateRevolutionObject(
            Texture2D texture, 
            bool isWhite, 
            float height = DEFAULT_HEIGHT)
        {
            var go = new GameObject("RevolutionPiece");

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.mesh = GenerateRevolutionMesh(texture, height);

            var meshRenderer = go.AddComponent<MeshRenderer>();
            
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") 
                         ?? Shader.Find("Standard");
            var mat = new Material(shader);
            
            mat.color = isWhite 
                ? new Color(0.95f, 0.92f, 0.85f)   // Ivory
                : new Color(0.15f, 0.12f, 0.10f);  // Dark wood
            mat.SetFloat("_Smoothness", 0.7f);
            
            meshRenderer.material = mat;

            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = meshFilter.mesh;
            collider.convex = true;

            go.transform.localScale = Vector3.one * 0.8f;

            return go;
        }
    }
}
