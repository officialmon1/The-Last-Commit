using System;
using System.Collections.Generic;
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
}

/// <summary>
/// 屏幕右侧的 Python 编辑器面板（朴素的 uGUI，运行时自己搭出来，不依赖预制体）。
/// 用法：<c>CodeEditorUI.Instance.Open("题目 slug")</c>。Esc 关闭，Ctrl+Enter 运行，Tab 缩进 4 空格。
/// 题目从 Neon 的 problems 表读（读不到就用内置示例题），每次运行的结果写进 submissions 表。
/// 倒计时从第一次打开这道题开始算，关掉面板也继续走；代码会按题目缓存，重新打开不丢。
/// </summary>
public class CodeEditorUI : MonoBehaviour
{
    public static CodeEditorUI Instance { get; private set; }
    public static bool IsOpen => Instance != null && Instance._root != null && Instance._root.activeSelf;

    [Tooltip("写进 submissions.player；留空用系统用户名")] public string playerName = "";
    public int fontSize = 18;
    [Tooltip("整体 UI 缩放（1 = 按真实像素渲染，字最清楚）")] public float uiScale = 1f;

    GameObject _root;
    Text _title, _timerLabel, _problemText, _output, _status;
    InputField _code, _stdin;
    Button _runBtn, _submitBtn;
    Font _mono, _ui;

    Problem _problem;
    bool _running;
    int _tabPending;
    readonly Dictionary<string, float> _deadlines = new Dictionary<string, float>();   // slug → Time.unscaledTime 截止
    readonly Dictionary<string, string> _codeCache = new Dictionary<string, string>();

    float SecondsLeft => _problem == null ? 0f
        : _deadlines.TryGetValue(_problem.slug, out var deadline) ? Mathf.Max(0f, deadline - Time.unscaledTime)
        : _problem.time_limit_seconds;
    bool TimeUp => _problem != null && SecondsLeft <= 0f;
    bool _caretToEnd;

    void Awake()
    {
        Instance = this;
        if (string.IsNullOrEmpty(playerName)) playerName = Environment.UserName;
        _mono = OsFont(new[] { "Consolas", "Cascadia Mono", "Courier New" }, fontSize);
        _ui = OsFont(new[] { "Microsoft YaHei", "微软雅黑", "PingFang SC", "Noto Sans CJK SC", "Arial" }, fontSize);
        BuildUI();
        _root.SetActive(false);
        PythonRunner.Init();
    }

    void Update()
    {
        if (!IsOpen) return;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.escapeKey.wasPressedThisFrame) { Close(); return; }
            if (kb.enterKey.wasPressedThisFrame && (kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed)) RunCode(submit: false);
            if (kb.tabKey.wasPressedThisFrame && _code.isFocused) _tabPending = 2;
        }
        if (_caretToEnd && _code.isFocused) { _code.MoveTextEnd(false); _caretToEnd = false; }
        // 点到哪个框就激活哪个框（不依赖 EventSystem 的点击路径，实测在某些机器上点 InputField 不会获得焦点）
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            var p = mouse.position.ReadValue();
            FocusIfHit(_code, p);
            FocusIfHit(_stdin, p);
        }
        HandleTab();
        _timerLabel.text = TimeUp ? "Time's up" : $"{Mathf.CeilToInt(SecondsLeft) / 60:00}:{Mathf.CeilToInt(SecondsLeft) % 60:00} left";
        _timerLabel.color = SecondsLeft < 30f ? new Color(1f, 0.45f, 0.4f) : Color.white;
        _runBtn.interactable = !_running;
        _submitBtn.interactable = !_running && !TimeUp;
    }

    void FocusIfHit(InputField f, Vector2 screenPos)
    {
        if (f.isFocused || !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)f.transform, screenPos)) return;
        f.ActivateInputField();
        f.MoveTextEnd(false);
    }

    // Tab：InputField 可能会插入 \t，也可能什么都不插；两种情况都变成 4 个空格
    void HandleTab()
    {
        string t = _code.text;
        int tab = t.IndexOf('\t');
        if (tab >= 0)
        {
            int caret = _code.caretPosition;
            int before = 0;
            for (int i = 0; i < Mathf.Min(caret, t.Length); i++) if (t[i] == '\t') before++;
            _code.text = t.Replace("\t", "    ");
            _code.caretPosition = _code.selectionAnchorPosition = _code.selectionFocusPosition = caret + before * 3;
            _tabPending = 0;
        }
        else if (_tabPending > 0 && --_tabPending == 0)
        {
            int caret = Mathf.Clamp(_code.caretPosition, 0, t.Length);
            _code.text = t.Insert(caret, "    ");
            _code.caretPosition = _code.selectionAnchorPosition = _code.selectionFocusPosition = caret + 4;
        }
    }

    // ------------------------------------------------------------------ 打开 / 关闭

    public async void Open(string slug)
    {
        if (IsOpen) return;
        _root.SetActive(true);
        SetPlayerLocked(true);
        _status.text = "Loading problem…";
        _output.text = "";

        var p = _problem != null && _problem.slug == slug ? _problem : await LoadProblem(slug);
        if (!IsOpen) return;   // 读取期间被关掉了
        if (!_deadlines.ContainsKey(p.slug)) _deadlines[p.slug] = Time.unscaledTime + p.time_limit_seconds;
        _problem = p;
        _title.text = p.title;
        _problemText.text = p.description;
        _code.text = _codeCache.TryGetValue(p.slug, out var cached) ? cached : p.starter_code;
        _status.text = NeonDb.Available ? "Run uses the Input box.  Submit judges with hidden input." : "Run uses the Input box.  Submit judges.  (Neon not configured: nothing is saved)";
        _code.ActivateInputField();
        _caretToEnd = true;   // ActivateInputField 会全选，下一帧把光标挪到末尾
    }

    public void Close()
    {
        if (_problem != null) _codeCache[_problem.slug] = _code.text;
        _root.SetActive(false);
        SetPlayerLocked(false);
    }

    void SetPlayerLocked(bool locked)
    {
        var fpc = FindAnyObjectByType<FirstPersonController>();
        if (fpc != null) fpc.SetInputLocked(locked);
        else { Cursor.lockState = locked ? CursorLockMode.None : CursorLockMode.Locked; Cursor.visible = locked; }
    }

    static async System.Threading.Tasks.Task<Problem> LoadProblem(string slug)
    {
        if (NeonDb.Available)
        {
            try
            {
                var rows = await NeonDb.Query("select row_to_json(p)::text as j from problems p where slug = $1", slug);
                if (rows.Count > 0) return JsonUtility.FromJson<Problem>(rows[0]);
                Debug.LogWarning($"CodeEditorUI: Neon 里没有 slug = {slug} 的题，用内置示例题。");
            }
            catch (Exception e) { Debug.LogWarning("CodeEditorUI: 读取题目失败，用内置示例题：" + e.Message); }
        }
        return new Problem
        {
            slug = slug,
            title = "A + B (built-in fallback)",
            description = "Read two integers a and b from standard input (separated by a space) and print their sum.\n\nExample\nInput:  3 5\nOutput: 8",
            stdin = "17 25\n",
            expected_output = "42\n",
            starter_code = "",
        };
    }

    // ------------------------------------------------------------------ 运行

    /// <summary>submit = false：用玩家自己填的 Input 跑，只看输出。submit = true：用题目的隐藏输入跑并判定、存库。</summary>
    async void RunCode(bool submit)
    {
        if (_running || _problem == null || (submit && TimeUp)) return;
        _running = true;
        _status.text = submit ? "Judging…" : "Running…";
        _output.text = "";
        string code = _code.text;
        _codeCache[_problem.slug] = code;
        int secondsLeft = Mathf.CeilToInt(SecondsLeft);

        var r = await PythonRunner.RunAsync(code, submit ? _problem.stdin : _stdin.text, Mathf.Max(1, _problem.run_timeout_seconds));
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
        _output.text = Tail(text, 14);
        _running = false;

        if (!submit)
        {
            _status.text = r.timedOut ? "Timed out" : r.exitCode != 0 ? $"Exited with code {r.exitCode}   {r.runMs} ms" : $"Done   {r.runMs} ms";
            return;
        }
        bool passed = !r.timedOut && r.exitCode == 0 && Normalize(r.stdout) == Normalize(_problem.expected_output);
        _status.text = passed ? $"✓ Accepted   {r.runMs} ms" : r.timedOut ? "✗ Time limit exceeded" : r.exitCode != 0 ? $"✗ Runtime error (exit {r.exitCode})   {r.runMs} ms" : $"✗ Wrong answer   {r.runMs} ms";

        if (NeonDb.Available)
        {
            try
            {
                await NeonDb.Exec(
                    "insert into submissions (problem_id, player, code, stdout, stderr, passed, timed_out, run_ms, seconds_left) values ($1::int, $2, $3, $4, $5, $6::boolean, $7::boolean, $8::int, $9::int)",
                    _problem.id.ToString(), playerName, code, r.stdout, r.stderr, passed ? "true" : "false", r.timedOut ? "true" : "false", r.runMs.ToString(), secondsLeft.ToString());
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

    static string Tail(string s, int lines)
    {
        var arr = s.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        if (arr.Length <= lines) return s;
        return "…\n" + string.Join("\n", arr, arr.Length - lines, lines);
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
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = uiScale;

        // 右侧面板
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

        // 题目描述
        var descBox = Panel("Problem", panel.transform, new Color(0.16f, 0.17f, 0.2f));
        descBox.gameObject.AddComponent<LayoutElement>().preferredHeight = fontSize * 7.5f;
        _problemText = Label("Text", descBox.transform, _ui, fontSize - 1, new Color(0.9f, 0.9f, 0.9f), TextAnchor.UpperLeft);
        Stretch(_problemText.rectTransform, 8);

        // 代码框
        _code = MakeInputField("Code", panel.transform, "# write Python here");
        var codeLe = _code.gameObject.AddComponent<LayoutElement>();
        codeLe.flexibleHeight = 1;
        codeLe.minHeight = fontSize * 8;   // 窗口再小代码框也不能被挤没

        // 玩家自己的输入（stdin）
        var inLabel = Label("InputLabel", panel.transform, _ui, fontSize - 3, new Color(0.7f, 0.7f, 0.7f), TextAnchor.LowerLeft);
        inLabel.text = "Input (stdin for Run)";
        inLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = fontSize;
        _stdin = MakeInputField("Stdin", panel.transform, "lines your program reads with input()");
        _stdin.gameObject.AddComponent<LayoutElement>().preferredHeight = fontSize * 4;

        // 输出
        var outLabel = Label("OutputLabel", panel.transform, _ui, fontSize - 3, new Color(0.7f, 0.7f, 0.7f), TextAnchor.LowerLeft);
        outLabel.text = "Output";
        outLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = fontSize;
        var outBox = Panel("Output", panel.transform, new Color(0.05f, 0.05f, 0.06f));
        outBox.gameObject.AddComponent<LayoutElement>().preferredHeight = fontSize * 9;
        _output = Label("Text", outBox.transform, _mono, fontSize - 1, new Color(0.85f, 0.9f, 0.85f), TextAnchor.UpperLeft);
        Stretch(_output.rectTransform, 8);

        // 按钮行（三个按钮平分宽度）+ 状态行
        var bar = Row("Bar", panel.transform, fontSize * 2);
        _runBtn = MakeButton("Run", bar, "Run (Ctrl+Enter)", () => RunCode(false), new Color(0.25f, 0.45f, 0.7f));
        _submitBtn = MakeButton("Submit", bar, "Submit", () => RunCode(true), new Color(0.2f, 0.55f, 0.35f));
        MakeButton("Close", bar, "Close (Esc)", Close, new Color(0.35f, 0.35f, 0.4f));
        _status = Label("Status", panel.transform, _ui, fontSize - 1, new Color(0.8f, 0.8f, 0.8f), TextAnchor.MiddleLeft);
        _status.gameObject.AddComponent<LayoutElement>().preferredHeight = fontSize * 1.5f;
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
        go.GetComponent<LayoutElement>().preferredHeight = height;
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

    static void Stretch(RectTransform rt, float margin)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(margin, margin);
        rt.offsetMax = new Vector2(-margin, -margin);
    }

    InputField MakeInputField(string name, Transform parent, string placeholder)
    {
        var bg = Panel(name, parent, new Color(0.07f, 0.08f, 0.09f));
        var field = bg.gameObject.AddComponent<InputField>();
        var text = Label("Text", bg.transform, _mono, fontSize, new Color(0.92f, 0.93f, 0.9f), TextAnchor.UpperLeft);
        Stretch(text.rectTransform, 8);
        var ph = Label("Placeholder", bg.transform, _mono, fontSize, new Color(0.5f, 0.5f, 0.5f), TextAnchor.UpperLeft);
        ph.text = placeholder;
        Stretch(ph.rectTransform, 8);
        field.textComponent = text;
        field.placeholder = ph;
        field.lineType = InputField.LineType.MultiLineNewline;
        field.contentType = InputField.ContentType.Standard;
        field.caretWidth = 2;
        field.customCaretColor = true;
        field.caretColor = Color.white;
        field.selectionColor = new Color(0.3f, 0.5f, 0.8f, 0.5f);
        field.characterLimit = 0;
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
