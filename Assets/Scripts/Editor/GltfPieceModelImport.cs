using System.IO;
using UnityEditor;
using UnityEngine;

namespace BizarreChess.Editor
{
    /// <summary>
    /// Keeps piece GLBs on glTFast with Legacy clips so Move / *_Move can play at runtime.
    /// </summary>
    public static class GltfPieceModelImport
    {
        private const string ModelsFolder = "Assets/Resources/Pieces/Models";
        private const int AnimationMethodLegacy = 1;

        [MenuItem("Bizarre Chess/Reimport Piece Models")]
        public static void ReimportPieceModels()
        {
            if (!Directory.Exists(ModelsFolder))
            {
                Debug.LogWarning($"[GltfPieceModelImport] Missing folder {ModelsFolder}");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("", new[] { ModelsFolder });
            int count = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!path.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase))
                    continue;

                EnsureLegacyAnimation(path, forceReimport: true);
                LogClips(path);
                count++;
            }

            Debug.Log($"[GltfPieceModelImport] Reimported {count} piece GLBs");
        }

        private class Postprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(
                string[] importedAssets,
                string[] deletedAssets,
                string[] movedAssets,
                string[] movedFromAssetPaths)
            {
                for (int i = 0; i < importedAssets.Length; i++)
                {
                    string path = importedAssets[i];
                    if (!path.Replace('\\', '/').StartsWith(ModelsFolder)
                        || !path.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase))
                        continue;

                    EnsureLegacyAnimation(path, forceReimport: false);
                }
            }
        }

        private static void EnsureLegacyAnimation(string path, bool forceReimport)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null)
                return;

            if (importer.GetType().Name != "GltfImporter")
            {
                if (forceReimport)
                    Debug.LogWarning($"[GltfPieceModelImport] {path} importer is {importer.GetType().Name}, not GltfImporter. Animation clips may be missing.");
                if (forceReimport)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                return;
            }

            var so = new SerializedObject(importer);
            var method = so.FindProperty("importSettings.animationMethod");
            bool changed = method != null && method.intValue != AnimationMethodLegacy;
            if (changed)
            {
                method.intValue = AnimationMethodLegacy;
                so.ApplyModifiedPropertiesWithoutUndo();
                importer.SaveAndReimport();
                return;
            }

            if (forceReimport)
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        private static void LogClips(string path)
        {
            var clips = AssetDatabase.LoadAllAssetsAtPath(path);
            int found = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] is AnimationClip clip && clip != null && !clip.name.StartsWith("__preview"))
                {
                    found++;
                    Debug.Log($"[GltfPieceModelImport] {Path.GetFileName(path)} clip '{clip.name}' legacy={clip.legacy} len={clip.length:0.00}s");
                }
            }

            if (found == 0)
                Debug.Log($"[GltfPieceModelImport] {Path.GetFileName(path)} has no animation clips");
        }
    }
}
