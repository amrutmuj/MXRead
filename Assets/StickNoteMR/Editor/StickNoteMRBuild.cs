using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class StickNoteMRBuild
{
    private const string ScenePath = "Assets/StickNoteMR/Scenes/StickNoteMRScene.unity";
    private const string OutputFolder = "Builds/Android";
    private const string ApkName = "StickNoteMR.apk";

    public static void BuildAndroidDevelopment()
    {
        StickNoteMRProjectConfigurator.ConfigureProjectNow();

        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
        {
            bool switched = EditorUserBuildSettings.SwitchActiveBuildTarget(
                BuildTargetGroup.Android,
                BuildTarget.Android);

            if (!switched)
            {
                throw new InvalidOperationException("Failed to switch Stick Note MR project to Android build target.");
            }
        }

        StickNoteMRSceneBootstrap.RebuildSceneFromCommandLine();

        if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), ScenePath)))
        {
            throw new InvalidOperationException("Stick Note MR scene was not generated before build.");
        }

        string buildDirectory = Path.Combine(Directory.GetCurrentDirectory(), OutputFolder);
        Directory.CreateDirectory(buildDirectory);

        string outputPath = Path.Combine(buildDirectory, ApkName);
        BuildPlayerOptions buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            target = BuildTarget.Android,
            locationPathName = outputPath,
            options = BuildOptions.Development | BuildOptions.AllowDebugging
        };

        BuildReport report = BuildPipeline.BuildPlayer(buildPlayerOptions);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Stick Note MR Android build failed with result {summary.result}. See Unity log for details.");
        }

        UnityEngine.Debug.Log(
            $"Stick Note MR build succeeded: {outputPath} ({summary.totalSize} bytes)");
    }
}
