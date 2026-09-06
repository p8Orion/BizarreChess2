using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace BizarreChess.Editor
{
    /// <summary>
    /// Batch/menu WebGL build. Output: project-root/BuildWeb
    /// </summary>
    public static class WebGLBuilder
    {
        public const string OutputPath = "BuildWeb";

        [MenuItem("Bizarre Chess/Build WebGL")]
        public static void BuildFromMenu()
        {
            Build();
        }

        public static void Build()
        {
            MeshBakeEditor.BakeAllMeshesNow();
            UseWebPipeline(out var previousPipeline, out int previousQuality);

            var enabled = EditorBuildSettings.scenes;
            var scenes = new string[enabled.Length];
            int count = 0;
            for (int i = 0; i < enabled.Length; i++)
            {
                if (enabled[i].enabled)
                    scenes[count++] = enabled[i].path;
            }
            System.Array.Resize(ref scenes, count);

            if (count == 0)
            {
                Debug.LogError("[WebGLBuilder] No enabled scenes in Build Settings.");
                ExitBatch(1);
                return;
            }

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = OutputPath,
                target = BuildTarget.WebGL,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            RestorePipeline(previousPipeline, previousQuality);
            WriteHostingFiles(OutputPath);

            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[WebGLBuilder] Build failed: {report.summary.result}");
                ExitBatch(1);
                return;
            }

            Debug.Log($"[WebGLBuilder] Build OK → {Path.GetFullPath(OutputPath)} ({report.summary.totalSize} bytes)");
            ExitBatch(0);
        }

        private static void UseWebPipeline(out RenderPipelineAsset previousPipeline, out int previousQuality)
        {
            previousPipeline = GraphicsSettings.defaultRenderPipeline;
            previousQuality = QualitySettings.GetQualityLevel();

            var mobile = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>("Assets/Settings/Mobile_RPAsset.asset");
            if (mobile != null)
                GraphicsSettings.defaultRenderPipeline = mobile;

            string[] names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                if (names[i] == "Web")
                {
                    QualitySettings.SetQualityLevel(i, true);
                    break;
                }
            }
        }

        private static void RestorePipeline(RenderPipelineAsset previousPipeline, int previousQuality)
        {
            if (Application.isBatchMode)
                return;

            GraphicsSettings.defaultRenderPipeline = previousPipeline;
            QualitySettings.SetQualityLevel(previousQuality, true);
        }

        private static void WriteHostingFiles(string outputPath)
        {
            Directory.CreateDirectory(outputPath);

            File.WriteAllText(Path.Combine(outputPath, "_headers"),
@"/*
  Cross-Origin-Opener-Policy: same-origin
  Cross-Origin-Embedder-Policy: require-corp
  Cache-Control: public, max-age=3600
");

            File.WriteAllText(Path.Combine(outputPath, "netlify.toml"),
@"[[headers]]
  for = ""/*""
  [headers.values]
    Cross-Origin-Opener-Policy = ""same-origin""
    Cross-Origin-Embedder-Policy = ""require-corp""
");
        }

        private static void ExitBatch(int code)
        {
            if (Application.isBatchMode)
                EditorApplication.Exit(code);
        }
    }
}
