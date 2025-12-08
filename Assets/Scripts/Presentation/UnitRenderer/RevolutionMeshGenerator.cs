using UnityEngine;
using BizarreChess.Core.Player;

namespace BizarreChess.Presentation.UnitRenderer
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
        public const int DEFAULT_RESOLUTION = 64;
        public const float DEFAULT_SIMPLIFICATION = 0.5f;  // 85% reduction (keeps 15% of triangles)
        public const float DEFAULT_AGGRESSIVENESS = 0.5f;   // Moderate - preserves details better
        public const float DEFAULT_HEIGHT = 1.5f;
        public const float DEFAULT_SIZE = 1.0f;

        /// <summary>
        /// Generate a revolution mesh from a profile texture.
        /// </summary>
        /// <param name="profileTexture">PNG profile (Y=height, X=radius)</param>
        /// <param name="height">Target height of the piece</param>
        /// <param name="resolution">Voxel grid resolution (32 = 32³)</param>
        /// <param name="simplificationRatio">How much to simplify (0.9 = 90% reduction)</param>
        /// <param name="aggressiveness">Simplifier aggressiveness (0=conservative, 1=very aggressive)</param>
        /// <returns>Generated and simplified mesh</returns>
        public static Mesh GenerateRevolutionMesh(
            Texture2D profileTexture, 
            float height = DEFAULT_HEIGHT,
            int resolution = DEFAULT_RESOLUTION,
            float simplificationRatio = DEFAULT_SIMPLIFICATION,
            float aggressiveness = DEFAULT_AGGRESSIVENESS)
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

            // Step 3: Weld duplicate vertices and smooth normals
            Mesh smoothMesh = MeshSmoother.WeldAndSmooth(denseMesh);
            
            Debug.Log($"[RevolutionMeshGenerator] After MarchingCubes: {denseMesh.vertexCount} verts, {denseMesh.triangles.Length/3} tris");
            Debug.Log($"[RevolutionMeshGenerator] After Weld: {smoothMesh.vertexCount} verts, {smoothMesh.triangles.Length/3} tris");

            // Step 4: Simplify with QEM
            Mesh simplifiedMesh = MeshSimplifier.Simplify(smoothMesh, simplificationRatio, aggressiveness);
            
            Debug.Log($"[RevolutionMeshGenerator] After Simplify ({simplificationRatio:P0} reduction, aggr={aggressiveness:F1}): {simplifiedMesh.vertexCount} verts, {simplifiedMesh.triangles.Length/3} tris");

            // Step 5: Generate cylindrical UVs for texturing
            GenerateCylindricalUVs(simplifiedMesh);
            
            // Scale to target height
            ScaleMesh(simplifiedMesh, height / size);

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
        /// Generate cylindrical UVs for the mesh (for texture mapping on revolution surfaces).
        /// U = angle around Y axis (0-1), V = height (0-1)
        /// </summary>
        private static void GenerateCylindricalUVs(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var uvs = new Vector2[vertices.Length];
            
            // Find bounds for normalization
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < vertices.Length; i++)
            {
                minY = Mathf.Min(minY, vertices[i].y);
                maxY = Mathf.Max(maxY, vertices[i].y);
            }
            float heightRange = maxY - minY;
            if (heightRange < 0.0001f) heightRange = 1f;
            
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 v = vertices[i];
                
                // U = angle around Y axis (cylindrical projection)
                float angle = Mathf.Atan2(v.x, v.z);
                float u = (angle / (2f * Mathf.PI)) + 0.5f; // Map -π..π to 0..1
                
                // V = normalized height
                float vCoord = (v.y - minY) / heightRange;
                
                uvs[i] = new Vector2(u, vCoord);
            }
            
            mesh.uv = uvs;
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
        /// <param name="profileTexture">Profile texture for mesh generation</param>
        /// <param name="playerId">Player ID for color/texture scheme</param>
        /// <param name="height">Target height</param>
        public static GameObject CreateRevolutionObject(
            Texture2D profileTexture, 
            int playerId, 
            float height = DEFAULT_HEIGHT)
        {
            var go = new GameObject("RevolutionPiece");

            var meshFilter = go.AddComponent<MeshFilter>();
            meshFilter.mesh = GenerateRevolutionMesh(profileTexture, height);

            var meshRenderer = go.AddComponent<MeshRenderer>();
            
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") 
                         ?? Shader.Find("Standard");
            var mat = new Material(shader);
            
            // Apply player color scheme (texture + tint)
            var colorScheme = PlayerColors.Get(playerId);
            ApplyColorSchemeToMaterial(mat, colorScheme);
            
            meshRenderer.material = mat;

            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = meshFilter.mesh;
            collider.convex = true;

            go.transform.localScale = Vector3.one * 0.8f;

            return go;
        }
        
        /// <summary>
        /// Create revolution object with legacy bool parameter (for backwards compatibility).
        /// </summary>
        public static GameObject CreateRevolutionObject(
            Texture2D profileTexture, 
            bool isWhite, 
            float height = DEFAULT_HEIGHT)
        {
            return CreateRevolutionObject(profileTexture, isWhite ? 0 : 1, height);
        }
        
        /// <summary>
        /// Apply player color scheme to a material.
        /// </summary>
        private static void ApplyColorSchemeToMaterial(Material mat, PlayerColorScheme colorScheme)
        {
            if (colorScheme.PieceTexture != null)
            {
                mat.mainTexture = colorScheme.PieceTexture;
                mat.mainTextureScale = new Vector2(colorScheme.TextureTiling, colorScheme.TextureTiling);
                mat.color = colorScheme.PrimaryColor;
            }
            else
            {
                mat.mainTexture = null;
                mat.color = colorScheme.PrimaryColor;
            }

            mat.SetFloat("_Smoothness", colorScheme.Smoothness);
        }
    }
}
