using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>一道题。字段名 = Neon problems 表的列名（JsonUtility 直接解析 row_to_json 的结果）。</summary>
[Serializable]
public class Problem
{
    public int id;
    public string slug;
    public string title;
    public string description;
    public string stdin = "";
    public string expected_output = "";
    public string starter_code = "";
    public int time_limit_seconds = 300;
    public int run_timeout_seconds = 5;
    public string track = "";
    public int stage = 1;
    public string sample_input = "";
    public string sample_output = "";
}

/// <summary>
/// 屏幕右侧的 Python 编辑器面板（朴素的 uGUI，运行时自己搭出来，不依赖预制体）。
///
/// 一个终端对应一条「关卡链」(track)：problems 表里 track 相同的题按 stage 排成一串，
/// 前面的关过了才解锁下一关；最后一关是 Boss。子关的读入/输出都写好了，玩家只补 TODO；
/// Boss 从零写，并且可以用 Ref 按钮调出自己前几关通过的代码（看 / 一键插进编辑器）。
/// 进度直接从 submissions 表算（本玩家、passed = true），本局内也有缓存，所以没配 Neon 也能玩一局。
///
/// 用法：<c>CodeEditorUI.Instance.Open("dijkstra")</c>。Esc 关闭，Ctrl+Enter 用自己的 Input 跑，Submit 用隐藏输入判定。
/// </summary>
public class CodeEditorUI : MonoBehaviour
{
    public static CodeEditorUI Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;
    /// <summary>一条关卡链的 Boss 通关时触发（参数 = track）。队友可以在这里接后续剧情。</summary>
    public static event Action<string> TrackCompleted;
    /// <summary>总时间用完时触发一次（参数 = track）。花瓣死亡演出接的就是它。</summary>
    public static event Action<string> TrackTimeUp;

    [Tooltip("写进 submissions.player；留空用系统用户名")] public string playerName = "";
    [Tooltip("整条关卡链的总时间（秒）。计时从第一次打开终端开始，游戏运行期间一直走，退出游戏暂停，存在 Neon 的 runs 表里")]
    public int trackTimeLimitSeconds = 36 * 60;
    public int fontSize = 18;
    [Tooltip("整体 UI 缩放（1 = 按真实像素渲染，字最清楚）")] public float uiScale = 1f;

    GameObject _root, _refRoot, _refBar;
    CanvasScaler _scaler;
    int _lastScreenH;
    Text _title, _timerLabel, _problemText, _output, _status, _refTitle;
    InputField _code, _stdin, _refField;
    Button _runBtn, _submitBtn;
    Transform _stageBar;
    Font _mono, _ui;

    // 当前关卡链
    string _track;
    List<Problem> _problems = new List<Problem>();
    int _current = -1;
    Problem _problem => _current >= 0 && _current < _problems.Count ? _problems[_current] : null;
    readonly HashSet<int> _passed = new HashSet<int>();                 // 通过的 problem id
    readonly Dictionary<int, string> _passedCode = new Dictionary<int, string>();
    readonly List<Button> _stageBtns = new List<Button>();
    string _shownSlug;   // 编辑器里当前显示的是哪道题的代码（切题/关闭时按它缓存）

    bool _running;
    CodeInputHelper _codeHelper, _stdinHelper;
    [Tooltip("调试：按这个键直接把时间用完（看死亡演出用）")] public Key debugTimeUpKey = Key.F9;

    // 整条关卡链的计时（runs 表一行：device_id + track）
    string _deviceId;
    float _runElapsed, _saveTimer;
    int _runLimit;
    bool _runStarted, _runCompleted, _timeUpFired;
    readonly Dictionary<string, string> _codeCache = new Dictionary<string, string>();
    readonly Dictionary<string, string> _stdinCache = new Dictionary<string, string>();

    float SecondsLeft => _runStarted ? Mathf.Max(0f, _runLimit - _runElapsed) : trackTimeLimitSeconds;
    bool TimeUp => _runStarted && SecondsLeft <= 0f;
    bool IsUnlocked(int i) => i == 0 || (i > 0 && i < _problems.Count && _passed.Contains(_problems[i - 1].id));

    void Awake()
    {
        Instance = this;
        if (string.IsNullOrEmpty(playerName)) playerName = Environment.UserName;
        _deviceId = SystemInfo.deviceUniqueIdentifier;   // 机器码：每台电脑一份进度，不用登录
        _mono = OsFont(new[] { "Consolas", "Cascadia Mono", "Courier New" }, fontSize);
        _ui = OsFont(new[] { "Segoe UI", "Microsoft YaHei", "微软雅黑", "Arial" }, fontSize);
        BuildUI();
        _root.SetActive(false);
        _refRoot.SetActive(false);
        PythonRunner.Init();
    }

    // 面板是按 1080p 固定排的；屏幕更矮时整体等比缩小，布局本身不变
    void FitScale()
    {
        if (Screen.height == _lastScreenH) return;
        _lastScreenH = Screen.height;
        _scaler.scaleFactor = uiScale * Mathf.Min(1f, Screen.height / 1080f);
    }

    void Update()
    {
        FitScale();
        TickRun();
        if (!IsOpen) return;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (kb.enterKey.wasPressedThisFrame && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed)) RunCode(submit: false);
        }
        // 点到哪个框就激活哪个框（不依赖 EventSystem 的点击路径，实测在某些机器上点 InputField 不会获得焦点）
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

    // ------------------------------------------------------------------ 打开 / 关闭 / 选关

    public async void Open(string track)
    {
        if (IsOpen) return;
        _root.SetActive(true);
        SetPlayerLocked(true);
        _status.text = "Loading…";
        _output.text = "";

        if (_track != track)
        {
            var list = await LoadTrack(track);
            if (!IsOpen) return;   // 读取期间被关掉了
            _track = track;
            _problems = list;
            _passed.Clear();
            _passedCode.Clear();
            await LoadProgress();
            if (!IsOpen) return;
            await LoadRun();
            if (!IsOpen) return;
            // 默认停在第一个没通过的已解锁关，全过了就停在 Boss
            _current = -1;
            for (int i = 0; i < _problems.Count; i++) if (IsUnlocked(i) && !_passed.Contains(_problems[i].id)) { _current = i; break; }
            if (_current < 0) _current = _problems.Count - 1;
        }
        RebuildStageBar();
        SelectStage(_current);
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
        _status.text = _passed.Contains(p.id) ? "Already accepted. You can still re-run or improve it."
            : NeonDb.Available ? "Run uses the Input box.  Submit judges with hidden input." : "Run uses the Input box.  Submit judges.  (Neon not configured: progress is not saved)";
        RefreshStageBar();
        _codeHelper.ResetHistory();
        _stdinHelper.ResetHistory();
        _code.ActivateInputField();
        _codeHelper.MoveCaretToEnd();
    }

    public void Close()
    {
        if (_shownSlug != null) { _codeCache[_shownSlug] = _code.text; _stdinCache[_shownSlug] = _stdin.text; }
        _root.SetActive(false);
        _refRoot.SetActive(false);
        SetPlayerLocked(false);
        SaveRun();
    }

    void SetPlayerLocked(bool locked)
    {
        var fpc = FindAnyObjectByType<FirstPersonController>();
        if (fpc != null) fpc.SetInputLocked(locked);
        else { Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = locked; }
    }

    static async Task<List<Problem>> LoadTrack(string track)
    {
        var list = new List<Problem>();
        if (NeonDb.Available)
        {
            try
            {
                foreach (var j in await NeonDb.Query("select row_to_json(p)::text as j from problems p where track = $1 order by stage, id", track))
                    list.Add(JsonUtility.FromJson<Problem>(j));
            }
            catch (Exception e) { Debug.LogWarning("CodeEditorUI: 读取题目失败：" + e.Message); }
        }
        if (list.Count == 0)
        {
            Debug.LogWarning($"CodeEditorUI: track '{track}' 没有题（或 Neon 不可用），用内置示例题。");
            list.Add(new Problem
            {
                id = -1, slug = "builtin-a-plus-b", track = track, stage = 1,
                title = "A + B (built-in fallback)",
                description = "Read two integers a and b from standard input (separated by a space) and print their sum.\n\nExample\nInput:  3 5\nOutput: 8",
                stdin = "17 25\n", expected_output = "42\n", starter_code = "",
            });
        }
        return list;
    }

    [Serializable] class PassedRow { public int problem_id; public string code; }

    async Task LoadProgress()
    {
        if (!NeonDb.Available) return;
        try
        {
            var rows = await NeonDb.Query(
                "select row_to_json(t)::text as j from (select distinct on (s.problem_id) s.problem_id, s.code from submissions s join problems p on p.id = s.problem_id where p.track = $1 and s.device_id = $2 and s.passed order by s.problem_id, s.created_at desc) t",
                _track, _deviceId);
            foreach (var j in rows)
            {
                var r = JsonUtility.FromJson<PassedRow>(j);
                _passed.Add(r.problem_id);
                _passedCode[r.problem_id] = r.code;
            }
        }
        catch (Exception e) { Debug.LogWarning("CodeEditorUI: 读取进度失败：" + e.Message); }
    }

    // ------------------------------------------------------------------ 总计时（runs 表）

    [Serializable] class RunRow { public int time_limit_seconds; public float elapsed_seconds; public string completed_at; }

    async Task LoadRun()
    {
        _runLimit = trackTimeLimitSeconds;
        _runElapsed = 0f;
        _runCompleted = false;
        _timeUpFired = false;
        _saveTimer = 0f;
        if (NeonDb.Available)
        {
            try
            {
                var rows = await NeonDb.Query("select row_to_json(r)::text as j from runs r where device_id = $1 and track = $2", _deviceId, _track);
                if (rows.Count > 0)
                {
                    var r = JsonUtility.FromJson<RunRow>(rows[0]);
                    _runLimit = r.time_limit_seconds;
                    _runElapsed = r.elapsed_seconds;
                    _runCompleted = !string.IsNullOrEmpty(r.completed_at);
                }
                else
                {
                    await NeonDb.Exec("insert into runs (device_id, player, track, time_limit_seconds) values ($1, $2, $3, $4::int)", _deviceId, playerName, _track, _runLimit.ToString());
                }
            }
            catch (Exception e) { Debug.LogWarning("CodeEditorUI: 读取计时失败：" + e.Message); }
        }
        _runStarted = true;
    }

    /// <summary>复活后调用：重新给一整段时间（通关记录保留）。</summary>
    public void RestartRun()
    {
        if (!_runStarted) return;
        _runElapsed = 0f;
        _timeUpFired = false;
        _runCompleted = false;
        SaveRun();
    }

    // 游戏运行期间一直走，面板关着也走；时间到只触发一次 TrackTimeUp
    void TickRun()
    {
        if (!_runStarted || _runCompleted) return;
        if (Keyboard.current != null && Keyboard.current[debugTimeUpKey].wasPressedThisFrame) _runElapsed = _runLimit;
        if (TimeUp)
        {
            if (_timeUpFired) return;
            _timeUpFired = true;
            SaveRun();
            TrackTimeUp?.Invoke(_track);
            return;
        }
        _runElapsed += Time.unscaledDeltaTime;
        _saveTimer += Time.unscaledDeltaTime;
        if (_saveTimer >= 5f) { _saveTimer = 0f; SaveRun(); }
    }

    const string SaveRunSql = "update runs set elapsed_seconds = $1::real, updated_at = now(), completed_at = case when $2::boolean then now() else completed_at end where device_id = $3 and track = $4";

    string[] SaveRunArgs(bool completed) => new[] { _runElapsed.ToString("F1", System.Globalization.CultureInfo.InvariantCulture), completed ? "true" : "false", _deviceId, _track };

    async void SaveRun(bool completed = false)
    {
        if (!NeonDb.Available || !_runStarted) return;
        try { await NeonDb.Exec(SaveRunSql, SaveRunArgs(completed)); }
        catch (Exception e) { Debug.LogWarning("CodeEditorUI: 保存计时失败：" + e.Message); }
    }

    void OnApplicationQuit()
    {
        // 退出时同步写一次，最多等 2 秒
        if (NeonDb.Available && _runStarted) NeonDb.ExecBlocking(SaveRunSql, 2000, SaveRunArgs(_runCompleted));
    }

    void OnGUI()
    {
        if (!_runStarted || IsOpen) return;   // 面板开着时标题栏里已经有倒计时
        string t = TimeUp ? "Time's up" : _runCompleted ? "Completed" : $"{Mathf.CeilToInt(SecondsLeft) / 60:00}:{Mathf.CeilToInt(SecondsLeft) % 60:00}";
        GUI.Label(new Rect(Screen.width - 200, 12, 184, 30), t, new GUIStyle(GUI.skin.label) { fontSize = 22, alignment = TextAnchor.MiddleRight, normal = { textColor = TimeUp ? new Color(1f, 0.45f, 0.4f) : Color.white } });
    }

    // ------------------------------------------------------------------ 关卡栏 / 参考代码

    // 先摘下来再 Destroy：Destroy 要到帧末才生效，同一帧里重建后遍历子节点时不能把旧的算进去
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
            var b = MakeButton("Stage" + i, _stageBar, boss ? "BOSS" : $"{i + 1}", () => SelectStage(idx), Color.gray);
            _stageBtns.Add(b);
        }
        // Ref 按钮：查看自己前几关通过的代码
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
            bool passed = _passed.Contains(_problems[i].id);
            bool unlocked = IsUnlocked(i);
            b.interactable = unlocked;
            b.targetGraphic.color = i == _current ? new Color(0.25f, 0.45f, 0.7f) : passed ? new Color(0.2f, 0.55f, 0.35f) : unlocked ? new Color(0.35f, 0.35f, 0.4f) : new Color(0.2f, 0.2f, 0.22f);
            var t = b.GetComponentInChildren<Text>();
            bool boss = i == _problems.Count - 1 && _problems.Count > 1;
            t.text = (boss ? "BOSS" : $"{i + 1}") + (passed ? " ✓" : unlocked ? "" : " 🔒");
        }
        // Ref 栏永远占着位置（布局固定），每个 Ref 只有通过了、且在当前关之前才能点
        int k = 0;
        foreach (Transform c in _refBar.transform)
        {
            var b = c.GetComponent<Button>();
            if (b == null) continue;
            b.interactable = k < _current && _passed.Contains(_problems[k].id);
            k++;
        }
    }

    void ShowReference(int stageIndex)
    {
        var p = _problems[stageIndex];
        _refRoot.SetActive(true);
        _refTitle.text = p.title;
        _refField.text = _passedCode.TryGetValue(p.id, out var code) ? code : "(not solved yet)";
    }

    void InsertReference()
    {
        if (!_refRoot.activeSelf) return;
        string t = _code.text;
        string block = "\n# ---- from: " + _refTitle.text + " ----\n" + _refField.text + "\n";
        _code.text = t.TrimEnd() + "\n" + block;
        _refRoot.SetActive(false);
        _code.ActivateInputField();
        _codeHelper.MoveCaretToEnd();
    }

    // ------------------------------------------------------------------ 运行

    /// <summary>submit = false：用玩家自己填的 Input 跑，只看输出。submit = true：用题目的隐藏输入跑并判定、存库、解锁下一关。</summary>
    async void RunCode(bool submit)
    {
        if (_running || _problem == null || (submit && TimeUp)) return;
        var problem = _problem;
        _running = true;
        _status.text = submit ? "Judging…" : "Running…";
        _output.text = "";
        string code = _code.text;
        _codeCache[problem.slug] = code;
        int secondsLeft = Mathf.CeilToInt(SecondsLeft);

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
            bool first = _passed.Add(problem.id);
            _passedCode[problem.id] = code;
            bool isBoss = _current == _problems.Count - 1;
            _status.text = isBoss ? $"✓ Accepted   {r.runMs} ms   —   BOSS cleared, track complete!"
                : $"✓ Accepted   {r.runMs} ms   —   Stage {_current + 2} unlocked";
            RefreshStageBar();
            if (isBoss && first) { _runCompleted = true; SaveRun(completed: true); TrackCompleted?.Invoke(_track); }
        }
        else
        {
            _status.text = r.timedOut ? "✗ Time limit exceeded" : r.exitCode != 0 ? $"✗ Runtime error (exit {r.exitCode})   {r.runMs} ms" : $"✗ Wrong answer   {r.runMs} ms";
        }

        if (NeonDb.Available && problem.id > 0)
        {
            try
            {
                await NeonDb.Exec(
                    "insert into submissions (problem_id, player, device_id, code, stdout, stderr, passed, timed_out, run_ms, seconds_left) values ($1::int, $2, $3, $4, $5, $6, $7::boolean, $8::boolean, $9::int, $10::int)",
                    problem.id.ToString(), playerName, _deviceId, code, r.stdout, r.stderr, passed ? "true" : "false", r.timedOut ? "true" : "false", r.runMs.ToString(), secondsLeft.ToString());
            }
            catch (Exception e) { Debug.LogWarning("CodeEditorUI: 保存提交失败：" + e.Message); }
        }
    }

    static string Normalize(string s)
    {
        var lines = (s ?? "").Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++) lines[i] = lines[i].TrimEnd();
        return string.Join("\n", lines).Trim();
    }

    // ------------------------------------------------------------------ 搭 UI

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
        // 按真实像素渲染，动态字体就不会被放大变糊
        _scaler = canvasGo.GetComponent<CanvasScaler>();
        _scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        _scaler.scaleFactor = uiScale;

        // ---------- 右侧主面板 ----------
        var panel = Panel("Panel", canvasGo.transform, new Color(0.11f, 0.12f, 0.14f, 0.96f));
        _root = panel.gameObject;
        var prt = panel.rectTransform;
        prt.anchorMin = new Vector2(0.53f, 0.03f);
        prt.anchorMax = new Vector2(0.985f, 0.97f);
        prt.offsetMin = prt.offsetMax = Vector2.zero;
        var vl = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(12, 12, 10, 10);
        vl.spacing = 8;
        vl.childControlWidth = vl.childControlHeight = true;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;

        // 标题 + 倒计时
        var header = Row("Header", panel.transform, 30);
        _title = Label("Title", header, _ui, fontSize + 4, Color.white, TextAnchor.MiddleLeft);
        _title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        _timerLabel = Label("Timer", header, _mono, fontSize + 2, Color.white, TextAnchor.MiddleRight);
        _timerLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = 160;

        // 关卡栏
        _stageBar = Row("Stages", panel.transform, fontSize * 2);

        // 题目描述
        var descBox = Panel("Problem", panel.transform, new Color(0.16f, 0.17f, 0.2f));
        Fix(descBox.gameObject, fontSize * 9f);
        _problemText = ScrollableText(descBox, _ui, fontSize - 1, new Color(0.9f, 0.9f, 0.9f));

        // 代码框
        _code = MakeInputField("Code", panel.transform, "# write Python here");
        _codeHelper = _code.gameObject.AddComponent<CodeInputHelper>();
        var codeLe = _code.gameObject.AddComponent<LayoutElement>();
        codeLe.preferredHeight = codeLe.minHeight = fontSize * 18;   // 代码框也固定 18 行高，多了就上下滚（1080p 正好放得下整个面板）
        codeLe.flexibleHeight = 0;

        // 玩家自己的输入（stdin）
        var inLabel = Label("InputLabel", panel.transform, _ui, fontSize - 3, new Color(0.7f, 0.7f, 0.7f), TextAnchor.LowerLeft);
        inLabel.text = "Input (stdin for Run)";
        Fix(inLabel.gameObject, fontSize);
        _stdin = MakeInputField("Stdin", panel.transform, "lines your program reads with input()");
        _stdinHelper = _stdin.gameObject.AddComponent<CodeInputHelper>();
        _stdinHelper.autoIndent = false;
        Fix(_stdin.gameObject, fontSize * 4);

        // 输出
        var outLabel = Label("OutputLabel", panel.transform, _ui, fontSize - 3, new Color(0.7f, 0.7f, 0.7f), TextAnchor.LowerLeft);
        outLabel.text = "Output";
        Fix(outLabel.gameObject, fontSize);
        var outBox = Panel("Output", panel.transform, new Color(0.05f, 0.05f, 0.06f));
        Fix(outBox.gameObject, fontSize * 8);
        _output = ScrollableText(outBox, _mono, fontSize - 1, new Color(0.85f, 0.9f, 0.85f));

        // 按钮行（三个按钮平分宽度）+ Ref 行 + 状态行
        var bar = Row("Bar", panel.transform, fontSize * 2);
        _runBtn = MakeButton("Run", bar, "Run (Ctrl+Enter)", () => RunCode(false), new Color(0.25f, 0.45f, 0.7f));
        _submitBtn = MakeButton("Submit", bar, "Submit", () => RunCode(true), new Color(0.2f, 0.55f, 0.35f));
        MakeButton("Close", bar, "Close (Esc)", Close, new Color(0.35f, 0.35f, 0.4f));
        _refBar = Row("RefBar", panel.transform, fontSize * 1.8f).gameObject;
        _status = Label("Status", panel.transform, _ui, fontSize - 1, new Color(0.8f, 0.8f, 0.8f), TextAnchor.MiddleLeft);
        Fix(_status.gameObject, fontSize * 1.5f);

        // ---------- 左侧参考代码面板 ----------
        var refPanel = Panel("ReferencePanel", canvasGo.transform, new Color(0.1f, 0.1f, 0.13f, 0.96f));
        _refRoot = refPanel.gameObject;
        var rrt = refPanel.rectTransform;
        rrt.anchorMin = new Vector2(0.015f, 0.03f);
        rrt.anchorMax = new Vector2(0.515f, 0.97f);
        rrt.offsetMin = rrt.offsetMax = Vector2.zero;
        var rvl = refPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        rvl.padding = new RectOffset(12, 12, 10, 10);
        rvl.spacing = 8;
        rvl.childControlWidth = rvl.childControlHeight = true;
        rvl.childForceExpandWidth = true;
        rvl.childForceExpandHeight = false;
        _refTitle = Label("Title", refPanel.transform, _ui, fontSize + 2, Color.white, TextAnchor.MiddleLeft);
        Fix(_refTitle.gameObject, 30);
        _refField = MakeInputField("RefCode", refPanel.transform, "");
        _refField.readOnly = true;
        _refField.gameObject.AddComponent<CodeInputHelper>().autoIndent = false;   // 负责按内容撑高度 + 滚轮
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
        hl.spacing = 8;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = true;
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = height;
        le.flexibleHeight = 0;   // 显式写 0：否则横向布局组会把 childForceExpandHeight 当成自己可伸缩，去抢剩余空间
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

    /// <summary>在一个 Image 里放一段可以用滚轮滚动的文字（题面太长时用）。</summary>
    static Text ScrollableText(Image box, Font font, int size, Color color)
    {
        var scroll = box.gameObject.AddComponent<ScrollRect>();
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(box.transform, false);
        var vrt = (RectTransform)viewport.transform;
        Stretch(vrt, 8);
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

    /// <summary>固定高度：最小 = 期望，布局再挤也不会被压扁。</summary>
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
        // 结构：框(Image + InputField + 只认滚轮的 ScrollRect) → Viewport(RectMask2D) → Text(高度随内容撑开) + Placeholder
        var bg = Panel(name, parent, new Color(0.07f, 0.08f, 0.09f));
        var field = bg.gameObject.AddComponent<InputField>();
        var scroll = bg.gameObject.AddComponent<WheelOnlyScrollRect>();
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewport.transform.SetParent(bg.transform, false);
        var vrt = (RectTransform)viewport.transform;
        Stretch(vrt, 8);
        var text = Label("Text", viewport.transform, _mono, fontSize, new Color(0.92f, 0.93f, 0.9f), TextAnchor.UpperLeft);
        text.verticalOverflow = VerticalWrapMode.Overflow;
        var trt = text.rectTransform;
        trt.anchorMin = new Vector2(0f, 1f);
        trt.anchorMax = new Vector2(1f, 1f);
        trt.pivot = new Vector2(0f, 1f);
        trt.offsetMin = trt.offsetMax = Vector2.zero;   // 高度由 CodeInputHelper 按完整文本算
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
