using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace TheLastCommit.EditorTools
{
    /// <summary>
    /// One-click release builds: Tools > The Last Commit > Build Release. Builds the playable scene (the first enabled scene in
    /// Build Settings) into Builds/Release/&lt;Platform&gt;/. macOS needs the "Mac Build Support (Mono)" module installed in Unity Hub.
    /// Package the result with the panda-service folder (source only: no .venv, no .env) next to the executable.
    /// </summary>
    public static class BuildRelease
    {
        const string Root = "Builds/Release";

        [MenuItem("Tools/The Last Commit/Build Release/Windows (x64)")]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, Path.Combine(Root, "Windows", "The Last Commit", "The Last Commit.exe"));

        [MenuItem("Tools/The Last Commit/Build Release/macOS (Apple Silicon + Intel)")]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, Path.Combine(Root, "macOS", "The Last Commit.app"));

        static void Build(BuildTarget target, string location)
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Build Release", "Stop Play mode first, then run the build again.", "OK");
                return;
            }
            var group = BuildPipeline.GetBuildTargetGroup(target);
            if (!BuildPipeline.IsBuildTargetSupported(group, target))
            {
                EditorUtility.DisplayDialog("Build Release", target + " is not installed in this Unity. Add the module in Unity Hub (Installs > Add modules).", "OK");
                return;
            }
            string[] scenes = System.Array.ConvertAll(System.Array.FindAll(EditorBuildSettings.scenes, s => s.enabled), s => s.path);
            if (scenes.Length == 0) { EditorUtility.DisplayDialog("Build Release", "No scene is enabled in Build Settings.", "OK"); return; }
            if (target == BuildTarget.StandaloneOSX)
                EditorUserBuildSettings.SetPlatformSettings("OSXUniversal", "Architecture", "x64ARM64");   // one .app for Intel and Apple Silicon
            var dir = Path.GetDirectoryName(location);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = location,
                target = target,
                targetGroup = group,
                options = BuildOptions.None,
            });
            var s = report.summary;
            Debug.Log($"[BuildRelease] {target}: {s.result}, {s.totalSize / (1024f * 1024f):F0} MB, {s.totalTime.TotalSeconds:F0} s -> {location}");
            if (s.result == BuildResult.Succeeded) EditorUtility.RevealInFinder(location);
        }
    }
}
