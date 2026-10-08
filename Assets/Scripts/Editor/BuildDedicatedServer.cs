using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildDedicatedServer
{
    // Batch mode: Unity -batchmode -quit -projectPath . -executeMethod BuildDedicatedServer.Linux
    [MenuItem("Build/Dedicated Server (Linux)")]
    public static void Linux() => Build(BuildTarget.StandaloneLinux64, "Builds/Server/Linux/BananaShooterServer.x86_64");

    [MenuItem("Build/Dedicated Server (macOS)")]
    public static void Mac() => Build(BuildTarget.StandaloneOSX, "Builds/Server/Mac/BananaShooterServer");

    static void Build(BuildTarget target, string path)
    {
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            target = target,
            subtarget = (int)StandaloneBuildSubtarget.Server,
            locationPathName = path,
        });

        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"Dedicated server build failed: {report.summary.result}");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        // GameServer.Init reads the app id from the working directory.
        File.Copy("steam_appid.txt", Path.Combine(Path.GetDirectoryName(path), "steam_appid.txt"), true);
        Debug.Log($"Dedicated server built to {path}");
    }
}
