using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;

/// <summary>Headless WebGL build for GitHub Pages. Run: Unity -batchmode -quit -executeMethod WebGLBuildGhPages.Build
/// with CM_WEBGL_OUTPUT set to the output directory. Uses the default template.
/// Brotli stays on (small upload); Decompression Fallback decodes client-side,
/// which is Unity's documented setup for static hosts without content-encoding
/// headers (exactly GitHub Pages).</summary>
public static class WebGLBuildGhPages
{
    public static void Build()
    {
        string output = Environment.GetEnvironmentVariable("CM_WEBGL_OUTPUT");
        if (string.IsNullOrEmpty(output))
            throw new Exception("CM_WEBGL_OUTPUT env var is not set.");
        PlayerSettings.WebGL.template = "APPLICATION:Default";
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;
        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        if (scenes.Length == 0)
            throw new Exception("No enabled scenes in Build Settings.");
        Console.WriteLine($"[WebGLBuild] scenes: {string.Join(", ", scenes)} -> {output}");
        BuildReport report = BuildPipeline.BuildPlayer(scenes, output, BuildTarget.WebGL, BuildOptions.None);
        Console.WriteLine($"[WebGLBuild] result: {report.summary.result}");
        if (report.summary.result != BuildResult.Succeeded)
            throw new Exception("WebGL build failed: " + report.summary.result);
    }

    /// <summary>Runs after any other preprocess (e.g. the Yandex platform applier) and forces
    /// Pages-friendly settings. Active only with CM_GHPAGES_BUILD=1, so Yandex builds are untouched.</summary>
    public class GhPagesBuildPreprocess : IPreprocessBuildWithReport
    {
        public int callbackOrder => 10000;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.WebGL) return;
            if (Environment.GetEnvironmentVariable("CM_GHPAGES_BUILD") != "1") return;
            PlayerSettings.WebGL.template = "APPLICATION:Default";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = true;
            Console.WriteLine("[WebGLBuild] gh-pages settings forced: default template, brotli + fallback");
        }
    }
}
