using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace BizarreChess.Presentation.UnitRenderer
{
    /// <summary>
    /// Caches generated meshes to avoid regenerating them every time.
    /// In Editor: Can save meshes as assets for WebGL builds.
    /// In Runtime: Loads pre-baked meshes from Resources, falls back to generation.
    /// </summary>
    public static class MeshCache
    {
        private const string CACHE_PATH = "Pieces/Revolution/Meshes";
        private const string EDITOR_CACHE_PATH = "Assets/Resources/Pieces/Revolution/Meshes";
        
        // Runtime memory cache
        private static Dictionary<string, Mesh> _runtimeCache = new Dictionary<string, Mesh>();
        
        /// <summary>
        /// Get or generate a revolution mesh for a piece.
        /// </summary>
        /// <param name="pieceName">Name of the piece (e.g., "King", "Queen")</param>
        /// <param name="profileTexture">Texture to use if generation is needed</param>
        /// <param name="height">Height of the piece</param>
        /// <returns>The mesh (cached, loaded, or newly generated)</returns>
        public static Mesh GetOrCreateMesh(string pieceName, Texture2D profileTexture, float height)
        {
            string cacheKey = GetCacheKey(pieceName, height);
            
            // 1. Check runtime memory cache
            if (_runtimeCache.TryGetValue(cacheKey, out Mesh cachedMesh))
            {
                Debug.Log($"[MeshCache] Using memory cache for: {pieceName}");
                return cachedMesh;
            }
            
            // 2. Try to load from Resources (pre-baked meshes)
            Mesh loadedMesh = LoadFromResources(cacheKey);
            if (loadedMesh != null)
            {
                Debug.Log($"[MeshCache] Loaded pre-baked mesh for: {pieceName}");
                _runtimeCache[cacheKey] = loadedMesh;
                return loadedMesh;
            }
            
            // 3. Generate mesh (fallback for non-WebGL or missing cache)
            Debug.Log($"[MeshCache] Generating mesh for: {pieceName} (not found in cache)");
            Mesh generatedMesh = RevolutionMeshGenerator.GenerateRevolutionMesh(
                profileTexture, 
                height,
                RevolutionMeshGenerator.DEFAULT_RESOLUTION,
                RevolutionMeshGenerator.DEFAULT_SIMPLIFICATION,
                RevolutionMeshGenerator.DEFAULT_AGGRESSIVENESS
            );
            generatedMesh.name = cacheKey;
            
            // Store in memory cache
            _runtimeCache[cacheKey] = generatedMesh;
            
            // 4. In Editor, save to disk for future builds
#if UNITY_EDITOR
            SaveToResources(generatedMesh, cacheKey);
#endif
            
            return generatedMesh;
        }
        
        /// <summary>
        /// Load a mesh from Resources folder.
        /// </summary>
        private static Mesh LoadFromResources(string cacheKey)
        {
            string resourcePath = $"{CACHE_PATH}/{cacheKey}";
            return Resources.Load<Mesh>(resourcePath);
        }
        
        /// <summary>
        /// Generate cache key from piece name and height.
        /// </summary>
        private static string GetCacheKey(string pieceName, float height)
        {
            // Sanitize and create unique key
            string sanitized = pieceName.Replace(" ", "_");
            return $"{sanitized}_h{height:F1}";
        }
        
        /// <summary>
        /// Clear the runtime memory cache.
        /// </summary>
        public static void ClearRuntimeCache()
        {
            _runtimeCache.Clear();
        }
        
        /// <summary>
        /// Check if a pre-baked mesh exists for a piece.
        /// </summary>
        public static bool HasCachedMesh(string pieceName, float height)
        {
            string cacheKey = GetCacheKey(pieceName, height);
            
            if (_runtimeCache.ContainsKey(cacheKey))
                return true;
                
            return Resources.Load<Mesh>($"{CACHE_PATH}/{cacheKey}") != null;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Save a mesh to the Resources folder as an asset.
        /// Only available in Editor.
        /// </summary>
        private static void SaveToResources(Mesh mesh, string cacheKey)
        {
            // Ensure directory exists
            string fullPath = $"{EDITOR_CACHE_PATH}";
            if (!AssetDatabase.IsValidFolder(fullPath))
            {
                // Create folder hierarchy
                CreateFolderHierarchy(fullPath);
            }
            
            string assetPath = $"{fullPath}/{cacheKey}.asset";
            
            // Check if asset already exists
            Mesh existingMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (existingMesh != null)
            {
                Debug.Log($"[MeshCache] Mesh already exists at: {assetPath}");
                return;
            }
            
            // Create a copy of the mesh for saving
            Mesh meshToSave = Object.Instantiate(mesh);
            meshToSave.name = cacheKey;
            
            AssetDatabase.CreateAsset(meshToSave, assetPath);
            AssetDatabase.SaveAssets();
            
            Debug.Log($"[MeshCache] Saved mesh to: {assetPath}");
        }
        
        /// <summary>
        /// Create folder hierarchy for the cache path.
        /// </summary>
        private static void CreateFolderHierarchy(string path)
        {
            string[] folders = path.Split('/');
            string currentPath = folders[0]; // "Assets"
            
            for (int i = 1; i < folders.Length; i++)
            {
                string newPath = $"{currentPath}/{folders[i]}";
                if (!AssetDatabase.IsValidFolder(newPath))
                {
                    AssetDatabase.CreateFolder(currentPath, folders[i]);
                }
                currentPath = newPath;
            }
        }
        
        /// <summary>
        /// Pre-bake all meshes for the given piece definitions.
        /// Call this from an Editor script to prepare for WebGL builds.
        /// </summary>
        public static void BakeAllMeshes(params (string name, Texture2D texture, float height)[] pieces)
        {
            Debug.Log($"[MeshCache] Baking {pieces.Length} meshes...");
            
            foreach (var piece in pieces)
            {
                GetOrCreateMesh(piece.name, piece.texture, piece.height);
            }
            
            AssetDatabase.Refresh();
            Debug.Log("[MeshCache] Baking complete!");
        }
#endif
    }
}

