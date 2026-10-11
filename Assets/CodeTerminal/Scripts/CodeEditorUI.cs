using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using Panda;

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
/// A "track" is the list of problems in Resources/Tracks/&lt;track&gt;.json ordered by stage. Every pillar (CodeTerminal) in the world opens one
/// level of a track; the prerequisite levels can be played in any order, and all must be passed before opening the final Boss.
/// Sub-levels come with I/O written, the player fills in the TODOs;
/// the Boss is written from scratch, with Ref buttons that show the player's own accepted solutions to the other levels (view / insert into the editor).
///
/// Saves are local only: Application.persistentDataPath/save_&lt;track&gt;.json
/// (which levels are passed, current code and input per level, accepted code, Panda hints received, seconds used of the track timer).
/// The first time a pillar is opened with a save present it asks "Continue / New game".
///
/// Usage: <c>CodeEditorUI.Instance.Open("dijkstra", level)</c>. Esc closes, Ctrl+Enter runs with your own Input, Submit (Ctrl+Shift+Enter) judges with the hidden input.
/// </summary>
public class CodeEditorUI : MonoBehaviour
{
    public static CodeEditorUI Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;
    public string CurrentProblemText => !_runStarted || _pendingSave != null || _problem == null   // blank only while the Continue / New game choice is pending
        ? "" : _problem.title + "\n" + _problem.description;
    public string CurrentCode => _code != null ? _code.text : "";
    public string CurrentTestResults => _lastTestResults;
    public float MinutesLeft => SecondsLeft / 60f;
    public float IdleSeconds => Time.unscaledTime - _lastCodeEditTime;
    /// <summary>Fired when a track's Boss is cleared (argument = track). Hook story events here.</summary>
    public static event Action<string> TrackCompleted;
    /// <summary>Fired once when the track timer runs out (argument = track). The petal death sequence listens to this.</summary>
    public static event Action<string> TrackTimeUp;

    [Tooltip("Total time for the whole track (seconds). Starts the first time the terminal is opened, keeps running while the game runs, pauses when the game quits, stored in the local save")]
    public int trackTimeLimitSeconds = 36 * 60;
    [Tooltip("Font size on a 1080p screen; other resolutions scale by screen height (Retina and other high-DPI screens scale up automatically)")] public int fontSize = 18;
    [Tooltip("Extra overall scale (1 = none)")] public float uiScale = 1f;
    [Tooltip("Debug: press this key to use up the timer immediately (to preview the death sequence)")] public Key debugTimeUpKey = Key.F9;

    GameObject _root, _refRoot, _refBar, _choiceRoot, _hintsRoot;
    Text _title, _timerLabel, _problemText, _output, _status, _refTitle, _choiceBanner, _choiceText, _hintsTitle, _hintsText;
    InputField _code, _stdin, _refField;
    Button _runBtn, _submitBtn, _pyBtn, _choiceBtn1, _choiceBtn2;
    Font _mono, _ui;
    CanvasScaler _scaler;
    int _lastScreenH;

    // ---------------- Forest theme palette ----------------
    static readonly Color ForestPanelBg = new Color(0.086f, 0.129f, 0.110f, 0.97f);
    static readonly Color ForestBoxBg = new Color(0.118f, 0.169f, 0.137f);
    static readonly Color ForestInputBg = new Color(0.071f, 0.102f, 0.082f);
    static readonly Color ForestOutputBg = new Color(0.055f, 0.075f, 0.062f);
    static readonly Color ForestTextCream = new Color(0.945f, 0.914f, 0.824f);
    static readonly Color ForestTextMuted = new Color(0.749f, 0.796f, 0.722f);
    static readonly Color ForestAccentRun = new Color(0.435f, 0.635f, 0.529f);
    static readonly Color ForestAccentSubmit = new Color(0.235f, 0.431f, 0.294f);
    static readonly Color ForestAccentHint = new Color(0.816f, 0.573f, 0.231f);
    static readonly Color ForestAccentClose = new Color(0.357f, 0.322f, 0.278f);
    static readonly Color ForestAccentRef = new Color(0.47f, 0.38f, 0.29f);
    static readonly Color ForestDanger = new Color(0.86f, 0.42f, 0.33f);
    static Sprite _roundedSprite;
    static Sprite RoundedSprite => _roundedSprite ??= MakeRoundedSprite(32, 9);

    /// <summary>A small 9-sliced rounded-rect sprite, generated once at runtime (no project assets needed).</summary>
    static Sprite MakeRoundedSprite(int size, int radius)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(RoundedCornerAlpha(x, y, size, radius) * 255f));
        tex.SetPixels32(pixels);
        tex.Apply(false, true);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
            SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
    }

    static float RoundedCornerAlpha(int x, int y, int size, int radius)
    {
        float fx = x + 0.5f, fy = y + 0.5f;
        bool inCornerX = fx < radius || fx > size - radius;
        bool inCornerY = fy < radius || fy > size - radius;
        if (!(inCornerX && inCornerY)) return 1f;
        float cx = fx < radius ? radius : size - radius;
        float cy = fy < radius ? radius : size - radius;
        float dist = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
        return Mathf.Clamp01(radius - dist + 0.5f);
    }
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
    readonly Dictionary<string, List<string>> _hints = new Dictionary<string, List<string>>();   // Panda hints received per problem (up to MaxHints each)
    string _shownSlug;   // which problem's code the editor currently shows (cached under this slug on switch/close)
    bool _running;
    string _pandaProblemKey, _lastTestResults = "No runs yet.";
    float _lastCodeEditTime;

    // Track timer
    float _runElapsed, _saveTimer;
    bool _runStarted, _runCompleted, _timeUpFired;

    float SecondsLeft => Mathf.Max(0f, trackTimeLimitSeconds - _runElapsed);
    bool TimeUp => _runStarted && SecondsLeft <= 0f;
    bool AllPassed => _problems.Count > 0 && _problems.TrueForAll(p => _passed.Contains(p.slug));
    /// <summary>Whether level <paramref name="stage"/> (0-based) of a track is accepted (false until that track has been opened once).</summary>
    public bool IsPassed(string track, int stage) => _runStarted && track == _track && stage >= 0 && stage < _problems.Count && _passed.Contains(_problems[stage].slug);

    public int PrerequisitesPassed(string track)
    {
        int passed = 0;
        if (_runStarted && track == _track)
            for (int i = 0; i < _problems.Count - 1; i++) if (_passed.Contains(_problems[i].slug)) passed++;
        return passed;
    }

    public bool IsUnlocked(string track, int stage) => _runStarted && track == _track && stage >= 0 && stage < _problems.Count
        && (stage < _problems.Count - 1 || PrerequisitesPassed(track) == _problems.Count - 1);

    public const int MaxHints = 3;
    static readonly List<string> NoHints = new List<string>();
    /// <summary>Panda hints received for the current problem, oldest first (kept in the local save).</summary>
    public IReadOnlyList<string> CurrentHints => _problem != null && _hints.TryGetValue(_problem.slug, out var h) ? h : NoHints;
    public void AddHint(string text)
    {
        if (_problem == null) return;
        if (!_hints.TryGetValue(_problem.slug, out var h)) _hints[_problem.slug] = h = new List<string>();
        h.Add(text);
        RefreshHints();
        SaveLocal();
    }
    string SavePath => Path.Combine(Application.persistentDataPath, "save_" + _track + ".json");

    void Awake()
    {
        Instance = this;
        // Scale the font size (and thus every layout size) for the current screen BEFORE building the UI: glyphs rasterize at real pixel size, so text stays crisp on high-DPI screens
        _builtScale = ScaleFor(Screen.height) * uiScale;
        fontSize = Mathf.Max(8, Mathf.RoundToInt(fontSize * _builtScale));
        _mono = OsFont(new[] { "Consolas", "Cascadia Mono", "Menlo", "Monaco", "Courier New" }, fontSize);
        // the same storybook font family as the speech bubble, for every label/button/description here;
        // code itself stays on _mono above, since a flourished font would hurt readability there
        _ui = OsFont(new[] { "Papyrus", "Herculanum", "Chalkboard SE", "Baskerville", "Georgia", "Garamond", "Segoe Script",
            "Segoe UI", "Microsoft YaHei", "Helvetica Neue", "PingFang SC", "Arial" }, fontSize);
        BuildUI();
        _root.SetActive(false);
        _refRoot.SetActive(false);
        _choiceRoot.SetActive(false);
        _hintsRoot.SetActive(false);
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
            if (kb.enterKey.wasPressedThisFrame && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed))
                RunCode(submit: kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);   // Ctrl+Enter = Run, Ctrl+Shift+Enter = Submit
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
        _timerLabel.color = SecondsLeft < 30f ? ForestDanger : ForestTextCream;
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

    /// <summary>Opens level <paramref name="stage"/> (0-based) of a track. Called by the pillar (CodeTerminal) the player is looking at.</summary>
    public void Open(string track, int stage = 0)
    {
        if (IsOpen || _running) return;
        _root.SetActive(true);
        _hintsRoot.SetActive(true);
        SetPlayerLocked(true);
        _output.text = "";

        if (_track != track)
        {
            _track = track;
            _problems = LoadTrack(track);
            RebuildRefBar();
            var save = LoadSave();
            if (save != null && (save.passed.Count > 0 || save.elapsedSeconds > 1f))
            {
                // a save exists: ask whether to continue or start over
                int left = Mathf.Max(0, trackTimeLimitSeconds - Mathf.CeilToInt(save.elapsedSeconds));
                _pendingSave = save;
                _pendingStage = stage;
                ShowChoice("Welcome Back", $"Saved game found: {save.passed.Count}/{_problems.Count} levels cleared, {left / 60:00}:{left % 60:00} left.",
                    "Continue", ChooseContinue, ForestAccentSubmit, "New game", ChooseNewGame, new Color(0.55f, 0.28f, 0.24f));
                return;
            }
            StartFresh();
        }
        SelectStage(stage);
    }

    SaveData _pendingSave;
    int _pendingStage;

    void ChooseContinue()
    {
        _choiceRoot.SetActive(false);
        ApplySave(_pendingSave);
        _pendingSave = null;
        SelectStage(_pendingStage);
    }

    void ChooseNewGame()
    {
        _choiceRoot.SetActive(false);
        _pendingSave = null;
        try { if (File.Exists(SavePath)) File.Delete(SavePath); } catch (Exception e) { Debug.LogWarning("CodeEditorUI: failed to delete the save: " + e.Message); }
        StartFresh();
        SelectStage(_pendingStage);
    }

    void StartFresh()
    {
        _passed.Clear();
        _passedCode.Clear();
        _codeCache.Clear();
        _stdinCache.Clear();
        _hints.Clear();
        _runElapsed = 0f;
        _runCompleted = false;
        _timeUpFired = false;
        _runStarted = true;
        _shownSlug = null;
        _pandaProblemKey = null;
        _current = -1;
    }

    /// <summary>The two-button overlay on top of the panel (Continue / New game, Accepted / Keep editing).</summary>
    void ShowChoice(string banner, string text, string label1, Action on1, Color color1, string label2, Action on2, Color color2)
    {
        _choiceBanner.text = banner;
        _choiceText.text = text;
        SetChoiceButton(_choiceBtn1, label1, on1, color1);
        SetChoiceButton(_choiceBtn2, label2, on2, color2);
        _choiceRoot.SetActive(true);
    }

    static void SetChoiceButton(Button b, string label, Action onClick, Color color)
    {
        b.GetComponentInChildren<Text>().text = label;
        b.targetGraphic.color = color;
        b.onClick.RemoveAllListeners();
        b.onClick.AddListener(() => onClick());
    }

    void SelectStage(int i)
    {
        if (_running || i < 0 || i >= _problems.Count) return;
        if (!IsUnlocked(_track, i)) { Close(); return; }
        if (_pendingSave == null) _choiceRoot.SetActive(false);   // switching level dismisses an "Accepted" overlay
        if (_shownSlug != null) { _codeCache[_shownSlug] = _code.text; _stdinCache[_shownSlug] = _stdin.text; }
        _current = i;
        var p = _problem;
        string problemKey = _track + ":" + p.slug;
        if (_pandaProblemKey != problemKey)
        {
            _pandaProblemKey = problemKey;
            _lastTestResults = "No runs yet.";
            if (PandaCompanion.Instance != null) PandaCompanion.Instance.ResetHintSession();
            if (PandaService.Instance != null && PandaService.Instance.BackendReady)
                PandaService.Instance.SendEvent("reset");
        }
        _shownSlug = p.slug;
        _title.text = $"Level {i + 1}/{_problems.Count} · {p.title}";
        _problemText.text = string.IsNullOrEmpty(p.sample_output) ? p.description
            : p.description + "\n\nSample input is pre-filled in the Input box. Expected output for it:\n" + p.sample_output.TrimEnd();
        _code.text = _codeCache.TryGetValue(p.slug, out var cached) ? cached : p.starter_code;
        _stdin.text = _stdinCache.TryGetValue(p.slug, out var cachedIn) ? cachedIn : p.sample_input;
        _output.text = "";
        _refRoot.SetActive(false);
        _status.text = _passed.Contains(p.slug) ? "Already accepted. You can still re-run or improve it."
            : "Run uses the Input box (Ctrl+Enter).  Submit judges with hidden input (Ctrl+Shift+Enter).";
        // No Python installed: explain in the status line, show the download button, and put the full install steps in the Output box
        bool noPython = PythonRunner.PythonPath == null;
        _pyBtn.gameObject.SetActive(noPython);
        if (noPython)
        {
            _status.text = "Python 3 not found. Click Get Python, install it, then restart the game.";
            _output.text = PythonRunner.InstallHint;
        }
        RefreshRefBar();
        RefreshHints();
        _codeHelper.ResetHistory();
        _stdinHelper.ResetHistory();
        _code.ActivateInputField();
        _codeHelper.MoveCaretToEnd();
    }

    public void Close()
    {
        if (_pendingSave != null) return;   // the Continue / New game choice is still pending, do not close
        _choiceRoot.SetActive(false);
        if (_shownSlug != null) { _codeCache[_shownSlug] = _code.text; _stdinCache[_shownSlug] = _stdin.text; }
        _root.SetActive(false);
        _hintsRoot.SetActive(false);
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
    [Serializable] class HintEntry { public string slug; public List<string> hints = new List<string>(); }
    [Serializable]
    class SaveData
    {
        public float elapsedSeconds;
        public bool completed;
        public List<string> passed = new List<string>();
        public List<Entry> passedCode = new List<Entry>();
        public List<Entry> code = new List<Entry>();
        public List<Entry> stdin = new List<Entry>();
        public List<HintEntry> hints = new List<HintEntry>();
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
        foreach (var e in s.hints) _hints[e.slug] = new List<string>(e.hints);
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
        foreach (var kv in _hints) s.hints.Add(new HintEntry { slug = kv.Key, hints = new List<string>(kv.Value) });
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
        if (PandaService.Instance != null && PandaService.Instance.BackendReady)
            PandaService.Instance.SendEvent("reset");
        SaveLocal();
    }

    // Runs while the game runs, even with the panel closed; fires TrackTimeUp exactly once
    void TickRun()
    {
        if (!_runStarted || _runCompleted || _pendingSave != null) return;   // paused only while the Continue / New game choice is pending
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
        GUI.Label(new Rect(Screen.width - 200 * k, 12 * k, 184 * k, 30 * k), t, new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(22 * k), font = _ui, alignment = TextAnchor.MiddleRight, normal = { textColor = TimeUp ? ForestDanger : ForestTextCream } });
    }

    // ------------------------------------------------------------------ Ref bar / hints / reference code

    // Detach before Destroy: Destroy only takes effect at the end of the frame, and the rebuild in the same frame must not count the old children
    static void ClearChildren(Transform t)
    {
        var old = new List<Transform>();
        foreach (Transform c in t) old.Add(c);
        foreach (var c in old) { c.SetParent(null); Destroy(c.gameObject); }
    }

    // Ref buttons: view your own accepted solutions to the other levels
    void RebuildRefBar()
    {
        ClearChildren(_refBar.transform);
        var lbl = Label("RefLabel", _refBar.transform, _ui, fontSize - 2, ForestTextMuted, TextAnchor.MiddleLeft);
        lbl.text = "Your other solutions:";
        lbl.gameObject.AddComponent<LayoutElement>().preferredWidth = fontSize * 10;
        for (int i = 0; i < _problems.Count; i++)
        {
            int idx = i;
            MakeButton("Ref" + i, _refBar.transform, $"Ref {i + 1}", () => ShowReference(idx), ForestAccentRef);
        }
    }

    // The Ref row always keeps its place (fixed layout); a Ref is clickable only for another level that is already passed
    void RefreshRefBar()
    {
        int k = 0;
        foreach (Transform c in _refBar.transform)
        {
            var b = c.GetComponent<Button>();
            if (b == null) continue;
            b.interactable = k != _current && _passed.Contains(_problems[k].slug);
            k++;
        }
    }

    void RefreshHints()
    {
        var hints = CurrentHints;
        _hintsTitle.text = $"Panda's hints   {hints.Count}/{MaxHints}";
        if (hints.Count == 0)
        {
            _hintsText.text = "No hints yet for this level. Click \"Ask Panda for a Hint\" (up to " + MaxHints + " per level); every hint stays listed here.";
            return;
        }
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < hints.Count; i++) sb.Append(i + 1).Append(". ").Append(hints[i]).Append(i + 1 < hints.Count ? "\n\n" : "");
        _hintsText.text = sb.ToString();
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

    /// <summary>submit = false: run with the player's own Input. submit = true: judge the hidden input and record progress toward the final Boss.</summary>
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
            _lastTestResults = "Runner error: " + r.error;
            _running = false;
            return;
        }
        string text = r.stdout;
        if (r.stderr.Length > 0) text += (text.Length > 0 ? "\n" : "") + r.stderr;
        if (!submit) _output.text = text;
        _running = false;

        if (!submit)
        {
            string sample = "";
            bool sampleMismatch = false;
            if (!string.IsNullOrEmpty(problem.sample_output) && Normalize(_stdin.text) == Normalize(problem.sample_input))
            {
                sampleMismatch = Normalize(r.stdout) != Normalize(problem.sample_output);
                sample = Normalize(r.stdout) == Normalize(problem.sample_output) ? "   ✓ matches the sample output" : "   ✗ differs from the sample output";
            }
            _status.text = r.timedOut ? "Timed out" : r.exitCode != 0 ? $"Exited with code {r.exitCode}   {r.runMs} ms" : $"Done   {r.runMs} ms{sample}";
            _lastTestResults = _status.text + "\n" + text;
            if (r.timedOut || r.exitCode != 0 || sampleMismatch) ReportToPanda(false);
            return;
        }
        // Submit: the player only sees the verdict, never the hidden test's output (the full output still goes to Panda as context)
        bool passed = !r.timedOut && r.exitCode == 0 && Normalize(r.stdout) == Normalize(problem.expected_output);
        if (passed)
        {
            bool first = _passed.Add(problem.slug);
            _passedCode[problem.slug] = code;
            bool allDone = AllPassed;
            _status.text = $"✓ Accepted   {r.runMs} ms";
            _output.text = "Hidden test passed.";
            RefreshRefBar();
            if (allDone) _runCompleted = true;
            SaveLocal();
            ReportToPanda(true);
            if (allDone && first) TrackCompleted?.Invoke(_track);
            ShowChoice(allDone ? "All Levels Cleared" : "Level Cleared",
                allDone ? $"✓ Accepted!  Level {_current + 1} cleared.\nAll {_problems.Count} levels are done, well played!"
                    : $"✓ Accepted!  Level {_current + 1} cleared  ({_passed.Count}/{_problems.Count} done).\nLeave and walk to another pillar for the next level.",
                "Leave terminal", () => { _choiceRoot.SetActive(false); Close(); }, ForestAccentSubmit,
                "Keep editing", () => _choiceRoot.SetActive(false), ForestAccentClose);
        }
        else
        {
            _status.text = r.timedOut ? "✗ Time limit exceeded" : r.exitCode != 0 ? $"✗ Runtime error (exit {r.exitCode})   {r.runMs} ms" : $"✗ Wrong answer   {r.runMs} ms";
            _output.text = r.timedOut ? "Your program took too long on the hidden test."
                : r.exitCode != 0 && r.stderr.Length > 0 ? r.stderr
                : "Your output did not match the hidden test. Use Run with your own Input to debug.";
            ReportToPanda(false);
        }
        _lastTestResults = _status.text + "\n" + text;
    }

    /// <summary>
    /// Silent judge for Panda: runs the current code on the hidden test without touching progress.
    /// onDone(passed, summary): passed = the code is already correct, so Panda tells the player to Submit instead of spending a hint.
    /// </summary>
    public async void CheckSolution(Action<bool, string> onDone)
    {
        if (_problem == null || _running || PythonRunner.PythonPath == null) { onDone(false, _lastTestResults); return; }
        var problem = _problem;
        _running = true;
        _status.text = "Panda is checking your code...";
        var r = await PythonRunner.RunAsync(_code.text, problem.stdin, Mathf.Max(1, problem.run_timeout_seconds));
        if (this == null) return;
        _running = false;
        bool passed = r.error == null && !r.timedOut && r.exitCode == 0 && Normalize(r.stdout) == Normalize(problem.expected_output);
        string summary = r.error != null ? "Runner error: " + r.error
            : passed ? "Hidden test: PASSED. The code is already correct."
            : r.timedOut ? "Hidden test: time limit exceeded"
            : r.exitCode != 0 ? "Hidden test: runtime error\n" + r.stderr
            : "Hidden test: wrong answer (the output does not match)";
        _lastTestResults = summary;
        _status.text = passed ? "Your code passes the hidden test. Click Submit!" : "Run uses the Input box (Ctrl+Enter).  Submit judges with hidden input (Ctrl+Shift+Enter).";
        onDone(passed, summary);
    }

    void ReportToPanda(bool solved)
    {
        if (PandaService.Instance == null || !PandaService.Instance.BackendReady) return;
        string problemKey = _pandaProblemKey;
        PandaService.Instance.SendEvent(solved ? "solved" : "failed", response =>
        {
            if (this != null && problemKey == _pandaProblemKey && solved && response != null &&
                PandaCompanion.Instance != null)
                PandaCompanion.Instance.ShowCelebration(response.text);
        });
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
        var panel = Panel("Panel", canvasGo.transform, ForestPanelBg);
        _root = panel.gameObject;
        var prt = panel.rectTransform;
        prt.anchorMin = new Vector2(0.53f, 0.03f);
        prt.anchorMax = new Vector2(0.985f, 0.97f);
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        var vl = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(fontSize * 3 / 4, fontSize * 3 / 4, fontSize * 2 / 3, fontSize * 2 / 3);
        vl.spacing = fontSize * 2 / 3;
        vl.childControlWidth = vl.childControlHeight = true;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;

        // Title + countdown
        var header = Row("Header", panel.transform, fontSize * 1.7f);
        _title = Label("Title", header, _ui, fontSize + 4, ForestTextCream, TextAnchor.MiddleLeft);
        _title.fontStyle = FontStyle.Bold;
        _title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        _timerLabel = Label("Timer", header, _mono, fontSize + 2, ForestTextCream, TextAnchor.MiddleRight);
        _timerLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = fontSize * 9;

        // Problem description (scrollable)
        var descBox = Panel("Problem", panel.transform, ForestBoxBg);
        Fix(descBox.gameObject, fontSize * 9f);
        _problemText = ScrollableText(descBox, _ui, fontSize - 1, ForestTextCream);

        // Code box (fixed at 20 lines, scrolls vertically)
        _code = MakeInputField("Code", panel.transform, "# write Python here");
        _code.onValueChanged.AddListener(_ => _lastCodeEditTime = Time.unscaledTime);
        _codeHelper = _code.gameObject.AddComponent<CodeInputHelper>();
        Fix(_code.gameObject, fontSize * 20);

        // The player's own input (stdin)
        var inLabel = Label("InputLabel", panel.transform, _ui, fontSize - 3, ForestTextMuted, TextAnchor.LowerLeft);
        inLabel.text = "Input (stdin for Run)";
        Fix(inLabel.gameObject, fontSize);
        _stdin = MakeInputField("Stdin", panel.transform, "lines your program reads with input()");
        _stdinHelper = _stdin.gameObject.AddComponent<CodeInputHelper>();
        _stdinHelper.autoIndent = false;
        Fix(_stdin.gameObject, fontSize * 4);

        // Output
        var outLabel = Label("OutputLabel", panel.transform, _ui, fontSize - 3, ForestTextMuted, TextAnchor.LowerLeft);
        outLabel.text = "Output";
        Fix(outLabel.gameObject, fontSize);
        var outBox = Panel("Output", panel.transform, ForestOutputBg);
        Fix(outBox.gameObject, fontSize * 8);
        _output = ScrollableText(outBox, _mono, fontSize - 1, new Color(0.82f, 0.9f, 0.82f));

        // Button row (four buttons share the width) + Ref row + status row
        var bar = Row("Bar", panel.transform, fontSize * 2);
        _runBtn = MakeButton("Run", bar, "Run (Ctrl+Enter)", () => RunCode(false), ForestAccentRun);
        _submitBtn = MakeButton("Submit", bar, "Submit", () => RunCode(true), ForestAccentSubmit);
        var hintBtn = MakeButton("PandaHint", bar, "Ask Panda for a Hint", () =>
        {
            if (PandaCompanion.Instance != null) PandaCompanion.Instance.OnClicked();
        }, ForestAccentHint);
        // The hint button gets more width and a bolder label so it reads as a distinct, game-y action rather than another IDE button
        hintBtn.GetComponent<LayoutElement>().flexibleWidth = 1.4f;
        var hintLabel = hintBtn.GetComponentInChildren<Text>();
        hintLabel.fontStyle = FontStyle.Bold;
        hintLabel.color = new Color(0.16f, 0.1f, 0.04f);
        MakeButton("Close", bar, "Close (Esc)", Close, ForestAccentClose);
        _refBar = Row("RefBar", panel.transform, fontSize * 1.8f).gameObject;
        var statusRow = Row("StatusRow", panel.transform, fontSize * 1.6f);
        _status = Label("Status", statusRow, _ui, fontSize - 1, ForestTextMuted, TextAnchor.MiddleLeft);
        _status.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        // Shown only when Python is missing: opens the download page
        _pyBtn = MakeButton("GetPython", statusRow, "Get Python", () => Application.OpenURL(PythonRunner.DownloadUrl), ForestAccentHint);
        _pyBtn.GetComponentInChildren<Text>().color = new Color(0.16f, 0.1f, 0.04f);
        var pyLe = _pyBtn.GetComponent<LayoutElement>();
        pyLe.flexibleWidth = 0;
        pyLe.preferredWidth = fontSize * 8;
        _pyBtn.gameObject.SetActive(false);

        // ---------- Continue / New game (overlaid on the main panel) ----------
        var choice = Panel("Choice", canvasGo.transform, new Color(0.067f, 0.09f, 0.075f, 0.98f));
        _choiceRoot = choice.gameObject;
        var crt = choice.rectTransform;
        crt.anchorMin = new Vector2(0.53f, 0.38f);
        crt.anchorMax = new Vector2(0.985f, 0.62f);
        crt.offsetMin = crt.offsetMax = Vector2.zero;
        var cvl = choice.gameObject.AddComponent<VerticalLayoutGroup>();
        cvl.padding = new RectOffset(fontSize * 4 / 3, fontSize * 4 / 3, fontSize, fontSize);
        cvl.spacing = fontSize * 2 / 3;
        cvl.childControlWidth = cvl.childControlHeight = true;
        cvl.childForceExpandWidth = true;
        cvl.childForceExpandHeight = false;
        var banner = Panel("ChoiceBanner", choice.transform, ForestAccentHint);
        Fix(banner.gameObject, fontSize * 1.9f);
        _choiceBanner = Label("BannerText", banner.transform, _ui, fontSize + 3, new Color(0.16f, 0.1f, 0.04f), TextAnchor.MiddleCenter);
        _choiceBanner.fontStyle = FontStyle.Bold;
        Stretch(_choiceBanner.rectTransform, 4);
        ForestTheme.AddLeafAccent(banner.transform, new Vector2(0f, 0.5f), new Vector2(10f, 0f));
        ForestTheme.AddLeafAccent(banner.transform, new Vector2(1f, 0.5f), new Vector2(-10f, 0f));
        _choiceText = Label("Text", choice.transform, _ui, fontSize + 2, ForestTextCream, TextAnchor.MiddleCenter);
        _choiceText.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        var cbar = Row("Bar", choice.transform, fontSize * 2.4f);
        _choiceBtn1 = MakeButton("Choice1", cbar, "Continue", () => { }, ForestAccentSubmit);     // labels and actions are set per use by ShowChoice
        _choiceBtn2 = MakeButton("Choice2", cbar, "New game", () => { }, new Color(0.55f, 0.28f, 0.24f));

        // ---------- Panda hints for the current level (top left). The speech bubble gets overwritten by Panda's face reactions; this list does not ----------
        var hintsPanel = Panel("HintsPanel", canvasGo.transform, new Color(0.075f, 0.1f, 0.082f, 0.96f));
        _hintsRoot = hintsPanel.gameObject;
        var hrt = hintsPanel.rectTransform;
        hrt.anchorMin = new Vector2(0.015f, 0.66f);
        hrt.anchorMax = new Vector2(0.515f, 0.93f);
        hrt.offsetMin = hrt.offsetMax = Vector2.zero;
        var hvl = hintsPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        hvl.padding = new RectOffset(fontSize * 2 / 3, fontSize * 2 / 3, fontSize / 2, fontSize / 2);
        hvl.spacing = fontSize / 2;
        hvl.childControlWidth = hvl.childControlHeight = true;
        hvl.childForceExpandWidth = true;
        hvl.childForceExpandHeight = false;
        _hintsTitle = Label("Title", hintsPanel.transform, _ui, fontSize + 1, ForestAccentHint, TextAnchor.MiddleLeft);
        _hintsTitle.fontStyle = FontStyle.Bold;
        Fix(_hintsTitle.gameObject, fontSize * 1.5f);
        var hintsBox = Panel("Hints", hintsPanel.transform, ForestBoxBg);
        hintsBox.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        _hintsText = ScrollableText(hintsBox, _ui, fontSize - 1, ForestTextCream);

        // ---------- Reference code panel on the left, below the hints ----------
        var refPanel = Panel("ReferencePanel", canvasGo.transform, new Color(0.075f, 0.1f, 0.082f, 0.96f));
        _refRoot = refPanel.gameObject;
        var rrt = refPanel.rectTransform;
        rrt.anchorMin = new Vector2(0.015f, 0.03f);
        rrt.anchorMax = new Vector2(0.515f, 0.63f);
        rrt.offsetMin = rrt.offsetMax = Vector2.zero;
        var rvl = refPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        rvl.padding = new RectOffset(fontSize * 2 / 3, fontSize * 2 / 3, fontSize / 2, fontSize / 2);
        rvl.spacing = fontSize / 2;
        rvl.childControlWidth = rvl.childControlHeight = true;
        rvl.childForceExpandWidth = true;
        rvl.childForceExpandHeight = false;
        _refTitle = Label("Title", refPanel.transform, _ui, fontSize + 2, ForestTextCream, TextAnchor.MiddleLeft);
        Fix(_refTitle.gameObject, fontSize * 1.7f);
        _refField = MakeInputField("RefCode", refPanel.transform, "");
        _refField.readOnly = true;
        _refField.gameObject.AddComponent<CodeInputHelper>().autoIndent = false;   // sizes the text to its content and handles wheel scrolling
        _refField.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        var rbar = Row("Bar", refPanel.transform, fontSize * 2);
        MakeButton("Insert", rbar, "Insert into editor", InsertReference, ForestAccentRun);
        MakeButton("Hide", rbar, "Hide", () => _refRoot.SetActive(false), ForestAccentClose);
    }

    static Image Panel(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        img.sprite = RoundedSprite;
        img.type = Image.Type.Sliced;
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
        var bg = Panel(name, parent, ForestInputBg);
        var field = bg.gameObject.AddComponent<InputField>();
        var scroll = bg.gameObject.AddComponent<WheelOnlyScrollRect>();
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(bg.transform, false);
        var vrt = (RectTransform)viewport.transform;
        Stretch(vrt, fontSize / 2f);
        var text = Label("Text", viewport.transform, _mono, fontSize, ForestTextCream, TextAnchor.UpperLeft);
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var trt = text.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0f, 1f);
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var ph = Label("Placeholder", viewport.transform, _mono, fontSize, new Color(0.45f, 0.5f, 0.44f), TextAnchor.UpperLeft);
        ph.text = placeholder;
        Stretch(ph.rectTransform, 0);
        field.textComponent = text;
        field.placeholder = ph;
        field.lineType = InputField.LineType.MultiLineNewline;
        field.contentType = InputField.ContentType.Standard;
        field.caretWidth = 2;
        field.customCaretColor = true;
        field.caretColor = ForestTextCream;
        field.selectionColor = new Color(0.44f, 0.64f, 0.46f, 0.5f);
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
        t.verticalOverflow = VerticalWrapMode.Overflow;   // never truncate: with Truncate a label one pixel taller than the button vanishes entirely (small windows)
        t.text = label;
        return btn;
    }
}
