using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>One problem. Loaded from Resources/Tracks/&lt;track&gt;.json (field names must match the JSON).</summary>
[Serializable]
public class Problem
{
    public int stage = 1;
    public string slug;
    public string title;
    public string description;
    public string stdin = "";
    public string expected_output = "";
    public string starter_code = "";
    public string sample_input = "";
    public string sample_output = "";
    public int run_timeout_seconds = 5;
}

/// <summary>
/// The Python editor panel on the right side of the screen (plain uGUI, built at runtime, no prefabs).
///
/// Each terminal opens one "track": the problems in Resources/Tracks/&lt;track&gt;.json ordered by stage;
/// a stage unlocks only after the previous one is passed; the last stage is the Boss. Sub-stages come with I/O written, the player fills in the TODOs;
/// the Boss is written from scratch, with Ref buttons that show the player's own accepted solutions from earlier stages (view / insert into the editor).
///
/// Saves are local only: Application.persistentDataPath/save_&lt;track&gt;.json
/// (which stages are passed, current code and input per stage, accepted code, seconds used of the track timer).
/// The first time the terminal is opened with a save present it asks "Continue / New game".
///
/// Usage: <c>CodeEditorUI.Instance.Open("dijkstra")</c>. Esc closes, Ctrl+Enter runs with your own Input, Submit judges with the hidden input.
/// </summary>
public class CodeEditorUI : MonoBehaviour
{
    public static CodeEditorUI Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;
    /// <summary>Fired when a track's Boss is cleared (argument = track). Hook story events here.</summary>
    public static event Action<string> TrackCompleted;
    /// <summary>Fired once when the track timer runs out (argument = track). The petal death sequence listens to this.</summary>
    public static event Action<string> TrackTimeUp;

    [Tooltip("Total time for the whole track (seconds). Starts the first time the terminal is opened, keeps running while the game runs, pauses when the game quits, stored in the local save")]
    public int trackTimeLimitSeconds = 36 * 60;
    [Tooltip("Font size on a 1080p screen; other resolutions scale by screen height (Retina and other high-DPI screens scale up automatically)")] public int fontSize = 18;
    [Tooltip("Extra overall scale (1 = none)")] public float uiScale = 1f;
    [Tooltip("Debug: press this key to use up the timer immediately (to preview the death sequence)")] public Key debugTimeUpKey = Key.F9;

    GameObject _root, _refRoot, _refBar, _choiceRoot;
    Text _title, _timerLabel, _problemText, _output, _status, _refTitle, _choiceText;
    InputField _code, _stdin, _refField;
    Button _runBtn, _submitBtn, _pyBtn;
    Transform _stageBar;
    Font _mono, _ui;
    CanvasScaler _scaler;
    int _lastScreenH;
    float _builtScale = 1f;   // scale used when the UI was built; later resolution changes only rescale the canvas

    /// <summary>Scale relative to 1080p (a 2880x1800 Retina display is about 1.67).</summary>
    public static float ScaleFor(int screenHeight) => Mathf.Clamp(screenHeight / 1080f, 0.5f, 4f);
    CodeInputHelper _codeHelper, _stdinHelper;

    // Current track
    string _track;
    List<Problem> _problems = new List<Problem>();
    int _current = -1;
    Problem _problem => _current >= 0 && _current < _problems.Count ? _problems[_current] : null;
    readonly HashSet<string> _passed = new HashSet<string>();                 // slugs of passed problems
    readonly Dictionary<string, string> _passedCode = new Dictionary<string, string>();
    readonly Dictionary<string, string> _codeCache = new Dictionary<string, string>();
    readonly Dictionary<string, string> _stdinCache = new Dictionary<string, string>();
    readonly List<Button> _stageBtns = new List<Button>();
    string _shownSlug;   // which problem's code the editor currently shows (cached under this slug on switch/close)
    bool _running;

    // Track timer
    float _runElapsed, _saveTimer;
    bool _runStarted, _runCompleted, _timeUpFired;

    float SecondsLeft => Mathf.Max(0f, trackTimeLimitSeconds - _runElapsed);
    bool TimeUp => _runStarted && SecondsLeft <= 0f;
    bool IsUnlocked(int i) => i == 0 || (i > 0 && i < _problems.Count && _passed.Contains(_problems[i - 1].slug));
    string SavePath => Path.Combine(Application.persistentDataPath, "save_" + _track + ".json");

    void Awake()
    {
        Instance = this;
        // Scale the font size (and thus every layout size) for the current screen BEFORE building the UI: glyphs rasterize at real pixel size, so text stays crisp on high-DPI screens
        _builtScale = ScaleFor(Screen.height) * uiScale;
        fontSize = Mathf.Max(8, Mathf.RoundToInt(fontSize * _builtScale));
        _mono = OsFont(new[] { "Consolas", "Cascadia Mono", "Menlo", "Monaco", "Courier New" }, fontSize);
        _ui = OsFont(new[] { "Segoe UI", "Microsoft YaHei", "Helvetica Neue", "PingFang SC", "Arial" }, fontSize);
        BuildUI();
        _root.SetActive(false);
        _refRoot.SetActive(false);
        _choiceRoot.SetActive(false);
        PythonRunner.Init();
    }

    // The UI is laid out for the screen it was built on; if the window changes later, rescale the canvas uniformly (layout unchanged)
    void FitScale()
    {
        if (Screen.height == _lastScreenH) return;
        _lastScreenH = Screen.height;
        _scaler.scaleFactor = ScaleFor(Screen.height) * uiScale / _builtScale;
    }

    void Update()
    {
        FitScale();
        TickRun();
        if (!IsOpen || _choiceRoot.activeSelf) return;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (kb.enterKey.wasPressedThisFrame && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed)) RunCode(submit: false);
        }
        // Activate whichever box was clicked (independent of the EventSystem click path: on some machines clicking an InputField does not focus it)
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            var p = mouse.position.ReadValue();
            FocusIfHit(_code, p);
            FocusIfHit(_stdin, p);
            if (_refRoot.activeSelf) FocusIfHit(_refField, p);
        }
        _timerLabel.text = TimeUp ? "Time's up" : _runCompleted ? "Completed" : $"{Mathf.CeilToInt(SecondsLeft) / 60:00}:{Mathf.CeilToInt(SecondsLeft) % 60:00} left";
        _timerLabel.color = SecondsLeft < 30f ? new Color(1f, 0.45f, 0.4f) : Color.white;
        _runBtn.interactable = !_running && _problem != null;
        _submitBtn.interactable = !_running && _problem != null && !TimeUp;
    }

    void FocusIfHit(InputField f, Vector2 screenPos)
    {
        if (f.isFocused || !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)f.transform, screenPos)) return;
        f.ActivateInputField();
        f.MoveTextEnd(false);
    }

    // ------------------------------------------------------------------ Open / close / stage selection

    public void Open(string track)
    {
        if (IsOpen) return;
        _root.SetActive(true);
        SetPlayerLocked(true);
        _output.text = "";

        if (_track != track)
        {
            _track = track;
            _problems = LoadTrack(track);
            RebuildStageBar();
            var save = LoadSave();
            if (save != null && (save.passed.Count > 0 || save.elapsedSeconds > 1f))
            {
                // a save exists: ask whether to continue or start over
                int left = Mathf.Max(0, trackTimeLimitSeconds - Mathf.CeilToInt(save.elapsedSeconds));
                _choiceText.text = $"Saved game found: {save.passed.Count}/{_problems.Count} stages cleared, {left / 60:00}:{left % 60:00} left.";
                _choiceRoot.SetActive(true);
                _pendingSave = save;
                return;
            }
            StartFresh();
        }
        SelectStage(_current);
    }

    SaveData _pendingSave;

    void ChooseContinue()
    {
        _choiceRoot.SetActive(false);
        ApplySave(_pendingSave);
        _pendingSave = null;
        PickDefaultStage();
        SelectStage(_current);
    }

    void ChooseNewGame()
    {
        _choiceRoot.SetActive(false);
        _pendingSave = null;
        try { if (File.Exists(SavePath)) File.Delete(SavePath); } catch (Exception e) { Debug.LogWarning("CodeEditorUI: failed to delete the save: " + e.Message); }
        StartFresh();
        SelectStage(_current);
    }

    void StartFresh()
    {
        _passed.Clear();
        _passedCode.Clear();
        _codeCache.Clear();
        _stdinCache.Clear();
        _runElapsed = 0f;
        _runCompleted = false;
        _timeUpFired = false;
        _runStarted = true;
        _shownSlug = null;
        PickDefaultStage();
    }

    // Default to the first unlocked stage that is not passed yet; if all are passed, stay on the Boss
    void PickDefaultStage()
    {
        _current = -1;
        for (int i = 0; i < _problems.Count; i++) if (IsUnlocked(i) && !_passed.Contains(_problems[i].slug)) { _current = i; break; }
        if (_current < 0) _current = _problems.Count - 1;
    }

    void SelectStage(int i)
    {
        if (i < 0 || i >= _problems.Count || !IsUnlocked(i)) return;
        if (_shownSlug != null) { _codeCache[_shownSlug] = _code.text; _stdinCache[_shownSlug] = _stdin.text; }
        _current = i;
        var p = _problem;
        _shownSlug = p.slug;
        _title.text = p.title;
        _problemText.text = string.IsNullOrEmpty(p.sample_output) ? p.description
            : p.description + "\n\nSample input is pre-filled in the Input box. Expected output for it:\n" + p.sample_output.TrimEnd();
        _code.text = _codeCache.TryGetValue(p.slug, out var cached) ? cached : p.starter_code;
        _stdin.text = _stdinCache.TryGetValue(p.slug, out var cachedIn) ? cachedIn : p.sample_input;
        _output.text = "";
        _refRoot.SetActive(false);
        _status.text = _passed.Contains(p.slug) ? "Already accepted. You can still re-run or improve it."
            : "Run uses the Input box.  Submit judges with hidden input.";
        // No Python installed: explain in the status line, show the download button, and put the full install steps in the Output box
        bool noPython = PythonRunner.PythonPath == null;
        _pyBtn.gameObject.SetActive(noPython);
        if (noPython)
        {
            _status.text = "Python 3 not found. Click Get Python, install it, then restart the game.";
            _output.text = PythonRunner.InstallHint;
        }
        RefreshStageBar();
        _codeHelper.ResetHistory();
        _stdinHelper.ResetHistory();
        _code.ActivateInputField();
        _codeHelper.MoveCaretToEnd();
    }

    public void Close()
    {
        if (_choiceRoot.activeSelf) return;   // the Continue / New game choice is still pending, do not close
        if (_shownSlug != null) { _codeCache[_shownSlug] = _code.text; _stdinCache[_shownSlug] = _stdin.text; }
        _root.SetActive(false);
        _refRoot.SetActive(false);
        SetPlayerLocked(false);
        SaveLocal();
    }

    void SetPlayerLocked(bool locked)
    {
        var fpc = FindAnyObjectByType<FirstPersonController>();
        if (fpc != null) fpc.SetInputLocked(locked);
        else { Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = locked; }
    }

    // ------------------------------------------------------------------ Problems (Resources/Tracks/<track>.json)

    [Serializable] class TrackFile { public List<Problem> problems = new List<Problem>(); }

    static List<Problem> LoadTrack(string track)
    {
        var asset = Resources.Load<TextAsset>("Tracks/" + track);
        if (asset != null)
        {
            var list = JsonUtility.FromJson<TrackFile>(asset.text).problems;
            list.Sort((a, b) => a.stage.CompareTo(b.stage));
            if (list.Count > 0) return list;
        }
        Debug.LogWarning($"CodeEditorUI: Resources/Tracks/{track}.json is missing or empty; using the built-in sample problem.");
        return new List<Problem>
        {
            new Problem
            {
                slug = "builtin-a-plus-b", stage = 1,
                title = "A + B (built-in fallback)",
                description = "Read two integers a and b from standard input (separated by a space) and print their sum.",
                stdin = "17 25\n", expected_output = "42\n", sample_input = "3 5\n", sample_output = "8\n",
            }
        };
    }

    // ------------------------------------------------------------------ Local save

    [Serializable] class Entry { public string slug; public string text; }
    [Serializable]
    class SaveData
    {
        public float elapsedSeconds;
        public bool completed;
        public List<string> passed = new List<string>();
        public List<Entry> passedCode = new List<Entry>();
        public List<Entry> code = new List<Entry>();
        public List<Entry> stdin = new List<Entry>();
    }

    SaveData LoadSave()
    {
        try
        {
            if (!File.Exists(SavePath)) return null;
            return JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
        }
        catch (Exception e) { Debug.LogWarning("CodeEditorUI: failed to read the save: " + e.Message); return null; }
    }

    void ApplySave(SaveData s)
    {
        StartFresh();
        _runElapsed = s.elapsedSeconds;
        _runCompleted = s.completed;
        foreach (var slug in s.passed) _passed.Add(slug);
        foreach (var e in s.passedCode) _passedCode[e.slug] = e.text;
        foreach (var e in s.code) _codeCache[e.slug] = e.text;
        foreach (var e in s.stdin) _stdinCache[e.slug] = e.text;
    }

    void SaveLocal()
    {
        if (!_runStarted) return;
        if (_shownSlug != null && IsOpen) { _codeCache[_shownSlug] = _code.text; _stdinCache[_shownSlug] = _stdin.text; }
        var s = new SaveData { elapsedSeconds = _runElapsed, completed = _runCompleted };
        s.passed.AddRange(_passed);
        foreach (var kv in _passedCode) s.passedCode.Add(new Entry { slug = kv.Key, text = kv.Value });
        foreach (var kv in _codeCache) s.code.Add(new Entry { slug = kv.Key, text = kv.Value });
        foreach (var kv in _stdinCache) s.stdin.Add(new Entry { slug = kv.Key, text = kv.Value });
        try { File.WriteAllText(SavePath, JsonUtility.ToJson(s, true)); }
        catch (Exception e) { Debug.LogWarning("CodeEditorUI: failed to write the save: " + e.Message); }
    }

    void OnApplicationQuit() => SaveLocal();

    // ------------------------------------------------------------------ Track timer

    /// <summary>Call after reviving: grants a fresh full time budget (stage progress is kept).</summary>
    public void RestartRun()
    {
        if (!_runStarted) return;
        _runElapsed = 0f;
        _timeUpFired = false;
        _runCompleted = false;
        SaveLocal();
    }

    // Runs while the game runs, even with the panel closed; fires TrackTimeUp exactly once
    void TickRun()
    {
        if (!_runStarted || _runCompleted || _choiceRoot.activeSelf) return;
        if (Keyboard.current != null && Keyboard.current[debugTimeUpKey].wasPressedThisFrame) _runElapsed = trackTimeLimitSeconds;
        if (TimeUp)
        {
            if (_timeUpFired) return;
            _timeUpFired = true;
            SaveLocal();
            TrackTimeUp?.Invoke(_track);
            return;
        }
        _runElapsed += Time.unscaledDeltaTime;
        _saveTimer += Time.unscaledDeltaTime;
        if (_saveTimer >= 10f) { _saveTimer = 0f; SaveLocal(); }
    }

    void OnGUI()
    {
        if (!_runStarted || IsOpen) return;   // the panel header already shows the countdown while open
        string t = TimeUp ? "Time's up" : _runCompleted ? "Completed" : $"{Mathf.CeilToInt(SecondsLeft) / 60:00}:{Mathf.CeilToInt(SecondsLeft) % 60:00}";
        float k = ScaleFor(Screen.height);
        GUI.Label(new Rect(Screen.width - 200 * k, 12 * k, 184 * k, 30 * k), t, new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22 * k), alignment = TextAnchor.MiddleRight, normal = { textColor = TimeUp ? new Color(1f, 0.45f, 0.4f) : Color.white } });
    }

    // ------------------------------------------------------------------ Stage bar / reference code

    // Detach before Destroy: Destroy only takes effect at the end of the frame, and the rebuild in the same frame must not count the old children
    static void ClearChildren(Transform t)
    {
        var old = new List<Transform>();
        foreach (Transform c in t) old.Add(c);
        foreach (var c in old) { c.SetParent(null); Destroy(c.gameObject); }
    }

    void RebuildStageBar()
    {
        ClearChildren(_stageBar);
        _stageBtns.Clear();
        for (int i = 0; i < _problems.Count; i++)
        {
            int idx = i;
            bool boss = i == _problems.Count - 1 && _problems.Count > 1;
            _stageBtns.Add(MakeButton("Stage" + i, _stageBar, boss ? "BOSS" : $"{i + 1}", () => SelectStage(idx), Color.gray));
        }
        // Ref buttons: view your own accepted solutions from earlier stages
        ClearChildren(_refBar.transform);
        var lbl = Label("RefLabel", _refBar.transform, _ui, fontSize - 2, new Color(0.7f, 0.7f, 0.7f), TextAnchor.MiddleLeft);
        lbl.text = "Your earlier solutions:";
        lbl.gameObject.AddComponent<LayoutElement>().preferredWidth = fontSize * 10;
        for (int i = 0; i < _problems.Count - 1; i++)
        {
            int idx = i;
            MakeButton("Ref" + i, _refBar.transform, $"Ref {i + 1}", () => ShowReference(idx), new Color(0.4f, 0.35f, 0.6f));
        }
    }

    void RefreshStageBar()
    {
        for (int i = 0; i < _stageBtns.Count; i++)
        {
            var b = _stageBtns[i];
            bool passed = _passed.Contains(_problems[i].slug);
            bool unlocked = IsUnlocked(i);
            b.interactable = unlocked;
            b.targetGraphic.color = i == _current ? new Color(0.25f, 0.45f, 0.7f) : passed ? new Color(0.2f, 0.55f, 0.35f) : unlocked ? new Color(0.35f, 0.35f, 0.4f) : new Color(0.2f, 0.2f, 0.22f);
            bool boss = i == _problems.Count - 1 && _problems.Count > 1;
            b.GetComponentInChildren<Text>().text = (boss ? "BOSS" : $"{i + 1}") + (passed ? " ✓" : unlocked ? "" : " 🔒");
        }
        // The Ref row always keeps its place (fixed layout); a Ref is clickable only if that stage is passed and before the current one
        int k = 0;
        foreach (Transform c in _refBar.transform)
        {
            var b = c.GetComponent<Button>();
            if (b == null) continue;
            b.interactable = k < _current && _passed.Contains(_problems[k].slug);
            k++;
        }
    }

    void ShowReference(int stageIndex)
    {
        var p = _problems[stageIndex];
        _refRoot.SetActive(true);
        _refTitle.text = p.title;
        _refField.text = _passedCode.TryGetValue(p.slug, out var code) ? code : "(not solved yet)";
    }

    void InsertReference()
    {
        if (!_refRoot.activeSelf) return;
        string block = "\n# ---- from: " + _refTitle.text + " ----\n" + _refField.text + "\n";
        _code.text = _code.text.TrimEnd() + "\n" + block;
        _refRoot.SetActive(false);
        _code.ActivateInputField();
        _codeHelper.MoveCaretToEnd();
    }

    // ------------------------------------------------------------------ Run

    /// <summary>submit = false: run with the player's own Input and just show the output. submit = true: run with the hidden input, judge, and unlock the next stage.</summary>
    async void RunCode(bool submit)
    {
        if (_running || _problem == null || (submit && TimeUp)) return;
        var problem = _problem;
        _running = true;
        _status.text = submit ? "Judging..." : "Running...";
        _output.text = "";
        string code = _code.text;
        _codeCache[problem.slug] = code;

        var r = await PythonRunner.RunAsync(code, submit ? problem.stdin : _stdin.text, Mathf.Max(1, problem.run_timeout_seconds));
        if (this == null) return;

        if (r.error != null)
        {
            _output.text = r.error;
            _status.text = "Runner error";
            _running = false;
            return;
        }
        string text = r.stdout;
        if (r.stderr.Length > 0) text += (text.Length > 0 ? "\n" : "") + r.stderr;
        _output.text = text;
        _running = false;

        if (!submit)
        {
            string sample = "";
            if (!string.IsNullOrEmpty(problem.sample_output) && Normalize(_stdin.text) == Normalize(problem.sample_input))
                sample = Normalize(r.stdout) == Normalize(problem.sample_output) ? "   ✓ matches the sample output" : "   ✗ differs from the sample output";
            _status.text = r.timedOut ? "Timed out" : r.exitCode != 0 ? $"Exited with code {r.exitCode}   {r.runMs} ms" : $"Done   {r.runMs} ms{sample}";
            return;
        }
        bool passed = !r.timedOut && r.exitCode == 0 && Normalize(r.stdout) == Normalize(problem.expected_output);
        if (passed)
        {
            bool first = _passed.Add(problem.slug);
            _passedCode[problem.slug] = code;
            bool isBoss = _current == _problems.Count - 1;
            _status.text = isBoss ? $"✓ Accepted   {r.runMs} ms   —   BOSS cleared, track complete!"
                : $"✓ Accepted   {r.runMs} ms   —   Stage {_current + 2} unlocked";
            RefreshStageBar();
            if (isBoss) _runCompleted = true;
            SaveLocal();
            if (isBoss && first) TrackCompleted?.Invoke(_track);
        }
        else
        {
            _status.text = r.timedOut ? "✗ Time limit exceeded" : r.exitCode != 0 ? $"✗ Runtime error (exit {r.exitCode})   {r.runMs} ms" : $"✗ Wrong answer   {r.runMs} ms";
        }
    }

    static string Normalize(string s)
    {
        var lines = (s ?? "").Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].TrimEnd();
        return string.Join("\n", lines).Trim();
    }

    // ------------------------------------------------------------------ Build UI

    static Font OsFont(string[] names, int size)
    {
        var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
        foreach (var n in names) if (installed.Contains(n)) return Font.CreateDynamicFontFromOSFont(n, size);
        return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }

    void BuildUI()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        var canvasGo = new GameObject("CodeEditorCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        // Render at real pixel size so dynamic fonts are never upscaled and blurred
        _scaler = canvasGo.GetComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        _scaler.scaleFactor = 1f;

        // ---------- Main panel on the right (every block has a fixed height; overflow scrolls inside the block) ----------
        var panel = Panel("Panel", canvasGo.transform, new Color(0.11f, 0.12f, 0.14f, 0.96f));
        _root = panel.gameObject;
        var prt = panel.rectTransform;
        prt.anchorMin = new Vector2(0.53f, 0.03f);
        prt.anchorMax = new Vector2(0.985f, 0.97f);
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        var vl = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(fontSize * 2 / 3, fontSize * 2 / 3, fontSize / 2, fontSize / 2);
        vl.spacing = fontSize / 2;
        vl.childControlWidth = vl.childControlHeight = true;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;

        // Title + countdown
        var header = Row("Header", panel.transform, fontSize * 1.7f);
        _title = Label("Title", header, _ui, fontSize + 4, Color.white, TextAnchor.MiddleLeft);
        _title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        _timerLabel = Label("Timer", header, _mono, fontSize + 2, Color.white, TextAnchor.MiddleRight);
        _timerLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = fontSize * 9;

        // Stage bar
        _stageBar = Row("Stages", panel.transform, fontSize * 2);

        // Problem description (scrollable)
        var descBox = Panel("Problem", panel.transform, new Color(0.16f, 0.17f, 0.2f));
        Fix(descBox.gameObject, fontSize * 9f);
        _problemText = ScrollableText(descBox, _ui, fontSize - 1, new Color(0.9f, 0.9f, 0.9f));

        // Code box (fixed at 18 lines, scrolls vertically)
        _code = MakeInputField("Code", panel.transform, "# write Python here");
        _codeHelper = _code.gameObject.AddComponent<CodeInputHelper>();
        Fix(_code.gameObject, fontSize * 18);

        // The player's own input (stdin)
        var inLabel = Label("InputLabel", panel.transform, _ui, fontSize - 3, new Color(0.7f, 0.7f, 0.7f), TextAnchor.LowerLeft);
        inLabel.text = "Input (stdin for Run)";
        Fix(inLabel.gameObject, fontSize);
        _stdin = MakeInputField("Stdin", panel.transform, "lines your program reads with input()");
        _stdinHelper = _stdin.gameObject.AddComponent<CodeInputHelper>();
        _stdinHelper.autoIndent = false;
        Fix(_stdin.gameObject, fontSize * 4);

        // Output
        var outLabel = Label("OutputLabel", panel.transform, _ui, fontSize - 3, new Color(0.7f, 0.7f, 0.7f), TextAnchor.LowerLeft);
        outLabel.text = "Output";
        Fix(outLabel.gameObject, fontSize);
        var outBox = Panel("Output", panel.transform, new Color(0.05f, 0.05f, 0.06f));
        Fix(outBox.gameObject, fontSize * 8);
        _output = ScrollableText(outBox, _mono, fontSize - 1, new Color(0.85f, 0.9f, 0.85f));

        // Button row (three buttons share the width) + Ref row + status row
        var bar = Row("Bar", panel.transform, fontSize * 2);
        _runBtn = MakeButton("Run", bar, "Run (Ctrl+Enter)", () => RunCode(false), new Color(0.25f, 0.45f, 0.7f));
        _submitBtn = MakeButton("Submit", bar, "Submit", () => RunCode(true), new Color(0.2f, 0.55f, 0.35f));
        MakeButton("Close", bar, "Close (Esc)", Close, new Color(0.35f, 0.35f, 0.4f));
        _refBar = Row("RefBar", panel.transform, fontSize * 1.8f).gameObject;
        var statusRow = Row("StatusRow", panel.transform, fontSize * 1.6f);
        _status = Label("Status", statusRow, _ui, fontSize - 1, new Color(0.8f, 0.8f, 0.8f), TextAnchor.MiddleLeft);
        _status.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        // Shown only when Python is missing: opens the download page
        _pyBtn = MakeButton("GetPython", statusRow, "Get Python", () => Application.OpenURL(PythonRunner.DownloadUrl), new Color(0.75f, 0.45f, 0.2f));
        var pyLe = _pyBtn.GetComponent<LayoutElement>();
        pyLe.flexibleWidth = 0;
        pyLe.preferredWidth = fontSize * 8;
        _pyBtn.gameObject.SetActive(false);

        // ---------- Continue / New game (overlaid on the main panel) ----------
        var choice = Panel("Choice", canvasGo.transform, new Color(0.08f, 0.09f, 0.11f, 0.98f));
        _choiceRoot = choice.gameObject;
        var crt = choice.rectTransform;
        crt.anchorMin = new Vector2(0.53f, 0.38f);
        crt.anchorMax = new Vector2(0.985f, 0.62f);
        crt.offsetMin = crt.offsetMax = Vector2.zero;
        var cvl = choice.gameObject.AddComponent<VerticalLayoutGroup>();
        cvl.padding = new RectOffset(fontSize * 4 / 3, fontSize * 4 / 3, fontSize, fontSize);
        cvl.spacing = fontSize;
        cvl.childControlWidth = cvl.childControlHeight = true;
        cvl.childForceExpandWidth = true;
        cvl.childForceExpandHeight = false;
        _choiceText = Label("Text", choice.transform, _ui, fontSize + 2, Color.white, TextAnchor.MiddleCenter);
        _choiceText.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        var cbar = Row("Bar", choice.transform, fontSize * 2.4f);
        MakeButton("Continue", cbar, "Continue", ChooseContinue, new Color(0.2f, 0.55f, 0.35f));
        MakeButton("New", cbar, "New game", ChooseNewGame, new Color(0.6f, 0.3f, 0.3f));

        // ---------- Reference code panel on the left ----------
        var refPanel = Panel("ReferencePanel", canvasGo.transform, new Color(0.1f, 0.1f, 0.13f, 0.96f));
        _refRoot = refPanel.gameObject;
        var rrt = refPanel.rectTransform;
        rrt.anchorMin = new Vector2(0.015f, 0.03f);
        rrt.anchorMax = new Vector2(0.515f, 0.97f);
        rrt.offsetMin = rrt.offsetMax = Vector2.zero;
        var rvl = refPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        rvl.padding = new RectOffset(fontSize * 2 / 3, fontSize * 2 / 3, fontSize / 2, fontSize / 2);
        rvl.spacing = fontSize / 2;
        rvl.childControlWidth = rvl.childControlHeight = true;
        rvl.childForceExpandWidth = true;
        rvl.childForceExpandHeight = false;
        _refTitle = Label("Title", refPanel.transform, _ui, fontSize + 2, Color.white, TextAnchor.MiddleLeft);
        Fix(_refTitle.gameObject, fontSize * 1.7f);
        _refField = MakeInputField("RefCode", refPanel.transform, "");
        _refField.readOnly = true;
        _refField.gameObject.AddComponent<CodeInputHelper>().autoIndent = false;   // sizes the text to its content and handles wheel scrolling
        _refField.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        var rbar = Row("Bar", refPanel.transform, fontSize * 2);
        MakeButton("Insert", rbar, "Insert into editor", InsertReference, new Color(0.25f, 0.45f, 0.7f));
        MakeButton("Hide", rbar, "Hide", () => _refRoot.SetActive(false), new Color(0.35f, 0.35f, 0.4f));
    }

    static Image Panel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static Transform Row(string name, Transform parent, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var hl = go.GetComponent<HorizontalLayoutGroup>();
        hl.spacing = 6;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = true;
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = height;
        le.flexibleHeight = 0;   // explicit 0: otherwise a horizontal layout group reports childForceExpandHeight as its own flexibility and grabs spare space
        return go.transform;
    }

    static Text Label(string name, Transform parent, Font font, int size, Color color, TextAnchor anchor)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = font;
        t.fontSize = size;
        t.color = color;
        t.alignment = anchor;
        t.supportRichText = false;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Truncate;
        return t;
    }

    /// <summary>Puts wheel-scrollable text inside an Image (problem description, output).</summary>
    static Text ScrollableText(Image box, Font font, int size, Color color)
    {
        var scroll = box.gameObject.AddComponent<ScrollRect>();
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(box.transform, false);
        var vrt = (RectTransform)viewport.transform;
        Stretch(vrt, size / 2f);
        var t = Label("Text", viewport.transform, font, size, color, TextAnchor.UpperLeft);
        t.verticalOverflow = VerticalWrapMode.Overflow;
        var trt = t.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0f, 1f);
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        t.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = vrt;
        scroll.content = trt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        return t;
    }

    /// <summary>Fixed height: min = preferred, flexible 0, so the layout never squeezes it.</summary>
    static void Fix(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = h;
        le.flexibleHeight = 0;
    }

    static void Stretch(RectTransform rt, float margin)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(margin, margin);
        rt.offsetMax = new Vector2(-margin, -margin);
    }

    InputField MakeInputField(string name, Transform parent, string placeholder)
    {
        // Structure: box (Image + InputField + wheel-only ScrollRect) -> Viewport (RectMask2D) -> Text (height computed from the full text by CodeInputHelper) + Placeholder
        var bg = Panel(name, parent, new Color(0.07f, 0.08f, 0.09f));
        var field = bg.gameObject.AddComponent<InputField>();
        var scroll = bg.gameObject.AddComponent<WheelOnlyScrollRect>();
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(bg.transform, false);
        var vrt = (RectTransform)viewport.transform;
        Stretch(vrt, fontSize / 2f);
        var text = Label("Text", viewport.transform, _mono, fontSize, new Color(0.92f, 0.93f, 0.9f), TextAnchor.UpperLeft);
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var trt = text.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0f, 1f);
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var ph = Label("Placeholder", viewport.transform, _mono, fontSize, new Color(0.5f, 0.5f, 0.5f), TextAnchor.UpperLeft);
        ph.text = placeholder;
        Stretch(ph.rectTransform, 0);
        field.textComponent = text;
        field.placeholder = ph;
        field.lineType = InputField.LineType.MultiLineNewline;
        field.contentType = InputField.ContentType.Standard;
        field.caretWidth = 2;
        field.customCaretColor = true;
        field.caretColor = Color.white;
        field.selectionColor = new Color(0.3f, 0.5f, 0.8f, 0.5f);
        field.characterLimit = 0;
        scroll.viewport = vrt;
        scroll.content = trt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        return field;
    }

    Button MakeButton(string name, Transform parent, string label, UnityEngine.Events.UnityAction onClick, Color color)
    {
        var img = Panel(name, parent, color);
        img.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        var btn = img.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(onClick);
        var t = Label("Label", img.transform, _ui, fontSize, Color.white, TextAnchor.MiddleCenter);
        Stretch(t.rectTransform, 4);
        t.text = label;
        return btn;
    }
}
