using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class Il2CppAotCi
{
    private const string ScenePath = "Assets/Scenes/Sandbox.unity";

    public static void BuildIos()
    {
        if (!File.Exists(ScenePath))
        {
            throw new FileNotFoundException("IL2CPP AOT CI scene was not found.", ScenePath);
        }

        if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.iOS, BuildTarget.iOS))
        {
            throw new InvalidOperationException("Unable to switch the Unity project to the iOS build target.");
        }

        PlayerSettings.SetScriptingBackend(BuildTargetGroup.iOS, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.iOS, ManagedStrippingLevel.High);
        PlayerSettings.SetIl2CppCompilerConfiguration(
            BuildTargetGroup.iOS,
            Il2CppCompilerConfiguration.Release);

        var configuredPath = Environment.GetEnvironmentVariable("UNITY_IOS_BUILD_PATH");
        var outputPath = Path.GetFullPath(string.IsNullOrWhiteSpace(configuredPath)
            ? "Builds/iOS"
            : configuredPath);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = outputPath,
            target = BuildTarget.iOS,
            options = BuildOptions.None,
        });

        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Unity iOS IL2CPP build failed: {report.summary.result} " +
                $"({report.summary.totalErrors} errors). Output: {outputPath}");
        }
    }
}
