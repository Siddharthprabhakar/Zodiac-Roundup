using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class WebGLBuild
{
    private const string RelativeOutputFolder = "Builds/WebGL";

    [MenuItem("Tools/Zodiac Roundup/Build WebGL")]
    public static void Build()
    {
        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string outputFolder = Path.Combine(projectRoot, RelativeOutputFolder);
        Directory.CreateDirectory(outputFolder);

        string[] scenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();

        if (scenes.Length == 0)
            throw new InvalidOperationException("No enabled scenes were found in Build Settings.");

        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = true;

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = outputFolder,
            target = BuildTarget.WebGL,
            options = BuildOptions.None
        };

        Debug.Log($"[WebGL Build] Building {scenes.Length} scenes to {outputFolder}");
        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
            throw new Exception($"WebGL build failed: {summary.result} ({summary.totalErrors} errors)");

        Debug.Log($"[WebGL Build] Succeeded in {summary.totalTime}. Reported size: {summary.totalSize:N0} bytes.");
    }
}
