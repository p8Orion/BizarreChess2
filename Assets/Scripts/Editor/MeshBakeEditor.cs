using UnityEngine;
using UnityEditor;
using BizarreChess.Presentation.UnitRenderer;
using BizarreChess.Core.Factories;

namespace BizarreChess.Editor
{
    /// <summary>
    /// Editor tool to pre-bake revolution meshes for WebGL builds.
    /// </summary>
    public class MeshBakeEditor : EditorWindow
    {
        [MenuItem("Bizarre Chess/Bake Revolution Meshes")]
        public static void ShowWindow()
        {
            GetWindow<MeshBakeEditor>("Mesh Baker");
        }

        [MenuItem("Bizarre Chess/Bake All Meshes Now")]
        public static void BakeAllMeshesNow()
        {
            BakeDefaultPieces();
        }

        private void OnGUI()
        {
            GUILayout.Label("Revolution Mesh Baker", EditorStyles.boldLabel);
            GUILayout.Space(10);
            
            EditorGUILayout.HelpBox(
                "This tool pre-generates and saves revolution meshes as assets.\n" +
                "Pre-baked meshes load instantly in WebGL builds instead of being generated at runtime.",
                MessageType.Info
            );
            
            GUILayout.Space(10);
            
            if (GUILayout.Button("Bake Default Chess Pieces", GUILayout.Height(40)))
            {
                BakeDefaultPieces();
            }
            
            GUILayout.Space(10);
            
            if (GUILayout.Button("Bake All Revolution Textures in Resources", GUILayout.Height(40)))
            {
                BakeAllTexturesInResources();
            }
            
            GUILayout.Space(20);
            
            EditorGUILayout.HelpBox(
                "Meshes are saved to:\nResources/Pieces/Revolution/Meshes/",
                MessageType.None
            );

            GUILayout.Space(10);

            if (GUILayout.Button("Clear Runtime Cache"))
            {
                MeshCache.ClearRuntimeCache();
                Debug.Log("[MeshBakeEditor] Runtime cache cleared");
            }
        }

        private static void BakeDefaultPieces()
        {
            Debug.Log("[MeshBakeEditor] Starting bake of default chess pieces...");
            
            // Load textures from Resources/Pieces/Revolution
            string[] pieceNames = { "King", "Queen", "Rook", "Bishop", "Knight", "Pawn", "Lancer", "Camel", "Crossbowman" };
            
            int bakedCount = 0;
            foreach (string pieceName in pieceNames)
            {
                string texturePath = $"Pieces/Revolution/{pieceName}";
                Texture2D texture = Resources.Load<Texture2D>(texturePath);
                
                if (texture != null)
                {
                    // Standard height
                    MeshCache.GetOrCreateMesh(pieceName, texture, 1.5f);
                    bakedCount++;
                    Debug.Log($"[MeshBakeEditor] Baked: {pieceName}");
                }
                else
                {
                    Debug.LogWarning($"[MeshBakeEditor] Texture not found: {texturePath}");
                }
            }
            
            AssetDatabase.Refresh();
            Debug.Log($"[MeshBakeEditor] Baking complete! {bakedCount} meshes baked.");
            EditorUtility.DisplayDialog("Mesh Baking Complete", $"Successfully baked {bakedCount} meshes.", "OK");
        }

        private static void BakeAllTexturesInResources()
        {
            Debug.Log("[MeshBakeEditor] Scanning for all revolution textures...");
            
            // Find all textures in the Revolution folder
            Texture2D[] textures = Resources.LoadAll<Texture2D>("Pieces/Revolution");
            
            if (textures.Length == 0)
            {
                Debug.LogWarning("[MeshBakeEditor] No textures found in Resources/Pieces/Revolution");
                EditorUtility.DisplayDialog("No Textures Found", "No textures found in Resources/Pieces/Revolution", "OK");
                return;
            }
            
            int bakedCount = 0;
            foreach (Texture2D texture in textures)
            {
                MeshCache.GetOrCreateMesh(texture.name, texture, 1.5f);
                bakedCount++;
                Debug.Log($"[MeshBakeEditor] Baked: {texture.name}");
            }
            
            AssetDatabase.Refresh();
            Debug.Log($"[MeshBakeEditor] Baking complete! {bakedCount} meshes baked.");
            EditorUtility.DisplayDialog("Mesh Baking Complete", $"Successfully baked {bakedCount} meshes from textures.", "OK");
        }
    }
}

