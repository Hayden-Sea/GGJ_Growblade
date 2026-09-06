using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace SwordGame.Editor
{
    /// <summary>Command-line entry point for the public Windows release.</summary>
    public static class BuildWindows
    {
        public static void BuildWindows64()
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var outputDirectory = Path.Combine(projectRoot, "Builds", "Windows");
            Directory.CreateDirectory(outputDirectory);

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Game/Scenes/Game.scene" },
                locationPathName = Path.Combine(outputDirectory, "Growblade.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            var report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Windows build failed: {report.summary.result}; errors={report.summary.totalErrors}");

            Debug.Log($"Growblade Windows build succeeded: {report.summary.outputPath}; size={report.summary.totalSize}");
        }
    }
}
