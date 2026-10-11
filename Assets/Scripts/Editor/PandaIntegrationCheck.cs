using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Panda;
using Process = System.Diagnostics.Process;
using ProcessStartInfo = System.Diagnostics.ProcessStartInfo;

namespace PandaEditor
{
    public static class PandaIntegrationCheck
    {
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, Private).GetValue(obj);
        static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Private).SetValue(obj, value);
        static void Call(object obj, string name, params object[] args) => obj.GetType().GetMethod(name, Private).Invoke(obj, args);
        static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
        static async Task Until(Func<bool> ready)
        {
            var deadline = DateTime.UtcNow.AddSeconds(25);
            while (!ready())
            {
                Require(DateTime.UtcNow < deadline, "Integration check timed out.");
                await Task.Delay(100);
            }
        }

        [MenuItem("Tools/Panda/Check integration (Play mode)")]
        static async void RunMenu() => Debug.Log(await Run());

        [MenuItem("Tools/Panda/Check local backend paths")]
        public static void CheckBackendPaths()
        {
            var find = typeof(PandaService).GetMethod("FindBackendFolder", BindingFlags.Static | BindingFlags.NonPublic);
            var project = Directory.GetParent(Application.dataPath).FullName;
            var expected = Path.Combine(project, "panda-service");
            foreach (var path in new[] { Application.dataPath, Path.Combine(project, "Builds", "Game_Data"),
                Path.Combine(project, "Builds", "Windows", "Game_Data") })
                Require((string)find.Invoke(null, new object[] { path }) == expected, "Local build backend lookup failed: " + path);
            Debug.Log("PANDA_BACKEND_PATH_CHECK_PASSED");
        }

#if UNITY_EDITOR_WIN
        [MenuItem("Tools/Panda/Check Python process cleanup")]
        static async void RunShutdownMenu() => Debug.Log(await CheckBackendShutdown());

        public static async Task<string> CheckBackendShutdown()
        {
            Require(!EditorApplication.isPlaying, "Stop Play before checking process cleanup.");
            var python = Path.Combine(Directory.GetParent(Application.dataPath).FullName,
                "panda-service", ".venv", "Scripts", "python.exe");
            var obj = new GameObject("Panda process check") { hideFlags = HideFlags.HideAndDontSave };
            var service = obj.AddComponent<PandaService>();
            Process child = null;
            try
            {
                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = "-c \"import os,time; print(os.getpid(),flush=True); time.sleep(60)\"",
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                });
                Set(service, "_process", process);
                var output = process.StandardOutput.ReadLineAsync();
                await Until(() => output.IsCompleted);
                child = Process.GetProcessById(int.Parse(await output));
                Call(service, "StopBackend");
                Require(child.WaitForExit(5000), "The venv interpreter survived backend shutdown.");
                return "PANDA_BACKEND_SHUTDOWN_CHECK_PASSED";
            }
            finally
            {
                Call(service, "StopBackend");
                if (child != null) { if (!child.HasExited) child.Kill(); child.Dispose(); }
                UnityEngine.Object.DestroyImmediate(obj);
            }
        }
#endif

        // Uses the real Dijkstra problems with a unique temporary save; existing player saves are untouched.
        public static async Task<string> Run()
        {
            Require(EditorApplication.isPlaying, "Enter Play mode in SampleScene before running this check.");
            CheckBackendPaths();
            var editor = CodeEditorUI.Instance;
            var companion = PandaCompanion.Instance;
            var service = PandaService.Instance;
            var player = UnityEngine.Object.FindAnyObjectByType<FirstPersonController>();
            Require(editor != null && companion != null && service != null && player != null,
                "The scene needs the code terminal, first-person controller and Panda.");
            Require(!CodeEditorUI.IsOpen, "Close the terminal before running this check.");
            Require(!Field<bool>(editor, "_runStarted"), "Run this check in a fresh Play session before opening a terminal.");
            Require(companion.speechBubble != null, "Panda's speech bubble reference is missing.");
            await Until(() => service.BackendReady);
            var track = "panda-check-" + Guid.NewGuid().ToString("N");
            var savePath = Path.Combine(Application.persistentDataPath, "save_" + track + ".json");
            StatusResponse status = null;
            Action<StatusResponse> onStatus = s => status = s;
            service.OnStatusUpdated += onStatus;
            try
            {
                Set(editor, "_track", track);
                var problems = typeof(CodeEditorUI).GetMethod("LoadTrack", BindingFlags.Static | BindingFlags.NonPublic)
                    .Invoke(null, new object[] { "dijkstra" });
                Set(editor, "_problems", problems);
                Call(editor, "RebuildRefBar");
                Call(editor, "StartFresh");
                editor.Open(track);
                Require(player.inputLocked && editor.CurrentProblemText.Contains("Priority Queue"), "Terminal flow/context failed.");
                var code = Field<InputField>(editor, "_code");
                code.text = "print(0)";
                Call(editor, "RunCode", false);
                await Until(() => !Field<bool>(editor, "_running"));
                await Until(() => status != null && status.failed_runs == 1);
                Require(editor.CurrentTestResults.Contains("differs"), "Failed sample run was not reported.");
                editor.GetComponentsInChildren<Button>(true).First(b => b.name == "PandaHint").onClick.Invoke();
                await Until(() => !Field<bool>(companion, "_hinting"));
                Require(editor.CurrentHints.Count == 1 && companion.speechBubble.bubbleText.text.Contains("Hint 1/3"), "Panda hint button failed.");
                Call(editor, "SelectStage", 0);
                Require(editor.CurrentHints.Count == 1, "Reopening the same level must preserve hint history.");
                code.text = "import sys, heapq\nh=[]\nlines=sys.stdin.read().splitlines()\nfor line in lines[1:]:\n p=line.split()\n if p[0]=='push': heapq.heappush(h,int(p[1]))\n else: print(heapq.heappop(h))\n";
                Require(editor.CurrentCode == code.text && editor.MinutesLeft > 0, "Code/timer context failed.");
                Call(editor, "RunCode", true);
                await Until(() => !Field<bool>(editor, "_running"));
                Require(Field<HashSet<string>>(editor, "_passed").Contains("dijkstra-1-heap"), "Python Submit failed.");
                await Until(() => companion.speechBubble.bubbleText.text != "Stuck on something? Click me for a hint."
                    && !companion.speechBubble.bubbleText.text.Contains("Hint 1/3"));
                Call(editor, "SelectStage", 1);
                await Until(() => status.failed_runs == 0);
                Require(editor.CurrentHints.Count == 0 && editor.CurrentProblemText.Contains("Adjacency List"), "Level switch/hint reset failed.");
                var payload = new HintRequest { problem = "quoted \"problem\"\nnext line", code = code.text,
                    previous_hints = new List<string> { "hint\nline" }, minutes_left = 3.5f };
                var restored = JsonUtility.FromJson<HintRequest>(JsonUtility.ToJson(payload));
                Require(restored.problem == payload.problem && restored.minutes_left == 3.5f
                    && restored.previous_hints[0] == payload.previous_hints[0], "Hint JSON payload failed.");
                return "PANDA_INTEGRATION_CHECK_PASSED: Python Run/Submit, failures, hints, celebration, level switch and context.";
            }
            finally
            {
                service.OnStatusUpdated -= onStatus;
                Set(editor, "_runStarted", false);
                editor.Close();
                if (File.Exists(savePath)) File.Delete(savePath);
            }
        }
    }
}
