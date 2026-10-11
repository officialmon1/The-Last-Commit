using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace Panda
{
    /// <summary>
    /// Single entry point to the panda-service backend (FastAPI: face/hand tracking + OpenAI hints).
    /// Launches it as a local subprocess on Play so nobody has to start a second server by hand,
    /// and exposes its HTTP endpoints as plain C# calls. Lives for the whole session (DontDestroyOnLoad).
    /// </summary>
    public class PandaService : MonoBehaviour
    {
        public static PandaService Instance { get; private set; }

        [Header("Backend process")]
        [Tooltip("If on, Unity starts panda-service/main.py itself via uvicorn when Play begins.")]
        public bool launchBackendAutomatically = true;
        [Tooltip("Leave empty to auto-detect (project root)/panda-service. Set this if autodetect fails, " +
                 "e.g. in a standalone build where panda-service was copied next to the .exe/.app.")]
        public string backendFolderOverride = "";
        public string host = "127.0.0.1";
        public int port = 8000;
        [Tooltip("Turn the webcam face/hand tracker on as soon as the backend is ready.")]
        public bool autoEnableFaceTracking = true;
        public float statusPollInterval = 0.5f;
        public float backendReadyTimeoutSeconds = 20f;

        public event Action<StatusResponse> OnStatusUpdated;
        public event Action OnBackendReady;
        public event Action<string> OnBackendFailed;

        public bool BackendReady { get; private set; }

        string BaseUrl => $"http://{host}:{port}";

        Process _process;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            // Keep local HTTP requests progressing when the game window loses focus.
            Application.runInBackground = true;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            StartCoroutine(Boot());
        }

        IEnumerator Boot()
        {
            // Reuse a manually started backend instead of launching a second server on the same port.
            using (var req = UnityWebRequest.Get($"{BaseUrl}/health"))
            {
                req.timeout = 2;
                yield return req.SendWebRequest();
                BackendReady = req.result == UnityWebRequest.Result.Success;
            }
            if (!BackendReady && launchBackendAutomatically)
            {
                string error = TryLaunchBackend();
                if (error != null)
                {
                    Debug.LogError($"[PandaService] Could not launch panda-service: {error}");
                    OnBackendFailed?.Invoke(error);
                    yield break;
                }
            }

            float deadline = Time.realtimeSinceStartup + backendReadyTimeoutSeconds;
            while (!BackendReady && Time.realtimeSinceStartup < deadline)
            {
                using (var req = UnityWebRequest.Get($"{BaseUrl}/health"))
                {
                    req.timeout = 2;
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        BackendReady = true;
                        break;
                    }
                }
                yield return new WaitForSecondsRealtime(0.3f);
            }

            if (!BackendReady)
            {
                string error = "panda-service did not respond on " + BaseUrl +
                                " within " + backendReadyTimeoutSeconds + "s. " +
                                "Is Python/uvicorn installed in panda-service/.venv? Check the Console above for its output.";
                Debug.LogError("[PandaService] " + error);
                OnBackendFailed?.Invoke(error);
                yield break;
            }

            Debug.Log("[PandaService] Backend is up.");
            yield return PostJson("/event", "{\"type\":\"reset\"}", null);
            OnBackendReady?.Invoke();
            if (autoEnableFaceTracking)
            {
                // the webcam only turns on once the player has pressed Begin on the start screen (which tells them it will)
                if (GameStart.Instance != null && !GameStart.Started) GameStart.GameStarted += StartFaceTrackingWhenGameStarts;
                else StartFaceTracking();
            }

            StartCoroutine(PollStatusLoop());
        }

        // ---------------- locating & launching the python backend ----------------
        string ResolveBackendFolder()
        {
            if (!string.IsNullOrEmpty(backendFolderOverride))
                return backendFolderOverride;
            return FindBackendFolder(Application.dataPath);
        }

        // Local builds live under Builds/, while the Python service and .env stay at the project root.
        static string FindBackendFolder(string dataPath)
        {
            for (var folder = Directory.GetParent(dataPath); folder != null; folder = folder.Parent)
            {
                string candidate = Path.Combine(folder.FullName, "panda-service");
                if (File.Exists(Path.Combine(candidate, "main.py"))) return candidate;
            }
            return null;
        }

        string ResolvePythonExecutable(string backendFolder)
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            string win = Path.Combine(backendFolder, ".venv", "Scripts", "python.exe");
            if (File.Exists(win)) return win;
#else
            foreach (var name in new[] { "python3.13", "python3", "python" })
            {
                string unix = Path.Combine(backendFolder, ".venv", "bin", name);
                if (File.Exists(unix)) return unix;
            }
#endif
            return null;
        }

        string TryLaunchBackend()
        {
            if (_process != null && !_process.HasExited)
                return null; // already running (e.g. domain reload in the editor)

            string backendFolder = ResolveBackendFolder();
            if (backendFolder == null || !Directory.Exists(backendFolder))
                return $"couldn't find the panda-service folder (looked at '{backendFolder}'). " +
                       "Set PandaService.backendFolderOverride in the Inspector.";

            string python = ResolvePythonExecutable(backendFolder);
            if (python == null)
                return $"couldn't find a python executable inside '{backendFolder}/.venv'. " +
                       "Follow the Panda assistant setup in the project's README.";

            var psi = new ProcessStartInfo
            {
                FileName = python,
                Arguments = $"-m uvicorn main:app --host {host} --port {port}",
                WorkingDirectory = backendFolder,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process.OutputDataReceived += (s, e) => { if (e.Data != null) Debug.Log("[panda-service] " + e.Data); };
            _process.ErrorDataReceived += (s, e) => { if (e.Data != null) Debug.Log("[panda-service] " + e.Data); };

            try
            {
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                Debug.Log($"[PandaService] Launched backend: {python} (cwd {backendFolder})");
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        void OnApplicationQuit()
        {
            StopBackend();
        }

        void OnDestroy()
        {
            GameStart.GameStarted -= StartFaceTrackingWhenGameStarts;
            if (Instance != this) return;
            Instance = null;
            StopBackend();
        }

        void StartFaceTrackingWhenGameStarts()
        {
            GameStart.GameStarted -= StartFaceTrackingWhenGameStarts;
            if (this != null && BackendReady) StartFaceTracking();
        }

        void StopBackend()
        {
            BackendReady = false;
            if (_process == null) return;
            try
            {
                if (!_process.HasExited)
                {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
                    // Windows venv launchers spawn the real interpreter as a child.
                    using (var stop = Process.Start(new ProcessStartInfo
                    {
                        FileName = Path.Combine(Environment.SystemDirectory, "taskkill.exe"),
                        Arguments = $"/PID {_process.Id} /T /F",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    })) stop.WaitForExit(5000);
#endif
                    if (!_process.HasExited) _process.Kill();
                }
            }
            catch (InvalidOperationException) { /* process never started or already exited */ }
            finally { _process.Dispose(); _process = null; }
        }

        // ---------------- polling ----------------
        IEnumerator PollStatusLoop()
        {
            while (true)
            {
                float idle = CodeEditorUI.IsOpen ? CodeEditorUI.Instance.IdleSeconds : 0f;
                string request = "{\"idle_seconds\":" + idle.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
                yield return PostJson("/status", request, (ok, body) =>
                {
                    if (ok)
                    {
                        var status = JsonUtility.FromJson<StatusResponse>(body);
                        OnStatusUpdated?.Invoke(status);
                    }
                });
                yield return new WaitForSecondsRealtime(statusPollInterval);
            }
        }

        // ---------------- public API ----------------
        public void RequestHint(string problem, string code, string testResults, int level,
            List<string> previousHints, Action<HintResponse> onDone, float minutesLeft = 36f)
        {
            string body = JsonUtility.ToJson(new HintRequest
            {
                problem = problem, code = code, test_results = testResults, level = level,
                previous_hints = previousHints ?? new List<string>(), minutes_left = minutesLeft
            });
            StartCoroutine(PostJson("/hint", body, (ok, resp) =>
            {
                onDone?.Invoke(ok ? JsonUtility.FromJson<HintResponse>(resp) : null);
            }));
        }

        public void SendEvent(string type, Action<EventResponse> onDone = null)
        {
            string body = "{\"type\":\"" + Json.Escape(type) + "\"}";
            StartCoroutine(PostJson("/event", body, (ok, resp) =>
            {
                onDone?.Invoke(ok ? JsonUtility.FromJson<EventResponse>(resp) : null);
            }));
        }

        public void StartFaceTracking(Action<bool, string> onDone = null)
        {
            StartCoroutine(PostJson("/face/start", "{}", (ok, resp) =>
            {
                if (!ok) { onDone?.Invoke(false, "couldn't reach the backend"); return; }
                var r = JsonUtility.FromJson<FaceStartResponse>(resp);
                onDone?.Invoke(r.ok, r.error);
                if (!r.ok) Debug.LogWarning("[PandaService] face tracking didn't start: " + r.error);
            }));
        }

        public void StopFaceTracking()
        {
            StartCoroutine(PostJson("/face/stop", "{}", null));
        }

        // ---------------- HTTP plumbing ----------------
        IEnumerator PostJson(string path, string jsonBody, Action<bool, string> onDone)
        {
            using (var req = new UnityWebRequest(BaseUrl + path, "POST"))
            {
                byte[] raw = Encoding.UTF8.GetBytes(jsonBody);
                req.uploadHandler = new UploadHandlerRaw(raw);
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = 20;
                yield return req.SendWebRequest();
                bool ok = req.result == UnityWebRequest.Result.Success;
                onDone?.Invoke(ok, ok ? req.downloadHandler.text : null);
            }
        }
    }
}
