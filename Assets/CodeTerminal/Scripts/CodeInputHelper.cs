using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 只响应滚轮、不响应拖拽的 ScrollRect：拖拽留给 InputField 做文字选择。
/// </summary>
public class WheelOnlyScrollRect : ScrollRect
{
    public override void OnBeginDrag(PointerEventData eventData) { }
    public override void OnDrag(PointerEventData eventData) { }
    public override void OnEndDrag(PointerEventData eventData) { }
}

/// <summary>
/// 给 uGUI 的 InputField 补上代码编辑器该有的东西（挂在 InputField 同一个物体上）：
/// Ctrl+Z / Ctrl+Y(Ctrl+Shift+Z) 撤销重做、Ctrl+C/V/X/A、Tab = 4 空格、
/// 回车自动缩进（上一行以 ':' 结尾再多缩 4 格）、光标移动时让 ScrollRect 跟着滚。
/// </summary>
[RequireComponent(typeof(InputField))]
public class CodeInputHelper : MonoBehaviour
{
    public bool autoIndent = true;
    [Tooltip("连续输入多少秒内算一步撤销")] public float undoGroupSeconds = 0.5f;

    InputField _field;
    ScrollRect _scroll;
    Text _text;

    struct State { public string text; public int caret; }
    readonly List<State> _undo = new List<State>();
    readonly List<State> _redo = new List<State>();
    State _current;
    float _lastChangeTime = -10f;
    bool _applying, _changedThisFrame;
    int _tabPending, _lastCaret = -1, _lastLen = -1;

    void Awake()
    {
        _field = GetComponent<InputField>();
        _scroll = GetComponent<ScrollRect>();
        _text = _field.textComponent;
        _field.onValueChanged.AddListener(OnChanged);
        ResetHistory();
    }

    /// <summary>程序设置了新内容后调用：清空撤销栈，把当前内容当作起点。</summary>
    public void ResetHistory()
    {
        _undo.Clear();
        _redo.Clear();
        _current = new State { text = _field.text, caret = _field.caretPosition };
        _lastChangeTime = -10f;
    }

    /// <summary>ActivateInputField 会在下一帧全选，所以晚两帧再把光标挪到末尾。</summary>
    public void MoveCaretToEnd() => _caretEndPending = 2;
    int _caretEndPending;

    void OnChanged(string t)
    {
        if (_applying) return;
        bool burst = _changedThisFrame;   // 同一帧里第二次以上的变化 = 粘贴之类的批量输入，不做自动缩进
        _changedThisFrame = true;
        int caret = _field.caretPosition;
        bool newline = t.Length == _current.text.Length + 1 && caret > 0 && caret <= t.Length && t[caret - 1] == '\n';

        // Tab 键可能被 InputField 插成 \t：统一变 4 个空格
        if (t.IndexOf('\t') >= 0)
        {
            int before = 0;
            for (int i = 0; i < Mathf.Min(caret, t.Length); i++) if (t[i] == '\t') before++;
            t = t.Replace("\t", "    ");
            caret += before * 3;
            Apply(t, caret);
            _tabPending = 0;
        }
        // 回车自动缩进：只在恰好多了一个 '\n' 时做
        else if (autoIndent && newline && !burst)
        {
            int lineStart = caret >= 2 ? t.LastIndexOf('\n', caret - 2) + 1 : 0;   // 上一行的开头
            string prev = t.Substring(lineStart, caret - 1 - lineStart);
            int indent = 0;
            while (indent < prev.Length && prev[indent] == ' ') indent++;
            if (prev.TrimEnd().EndsWith(":")) indent += 4;
            if (indent > 0)
            {
                t = t.Insert(caret, new string(' ', indent));
                caret += indent;
                Apply(t, caret);
            }
        }

        // 记录撤销：间隔超过 undoGroupSeconds 才开新的一步；换行总是单独一步（像普通编辑器那样按行撤销）
        if (newline || Time.unscaledTime - _lastChangeTime > undoGroupSeconds) { _undo.Add(_current); _redo.Clear(); }
        _lastChangeTime = newline ? -10f : Time.unscaledTime;
        _current = new State { text = t, caret = caret };
    }

    void Apply(string t, int caret)
    {
        _applying = true;
        _field.text = t;
        caret = Mathf.Clamp(caret, 0, t.Length);
        _field.caretPosition = _field.selectionAnchorPosition = _field.selectionFocusPosition = caret;
        _applying = false;
        _lastCaret = -1;
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && _field.isFocused && !_field.readOnly)
        {
            bool ctrl = kb.leftCtrlKey.isPressed || kb.rightCtrlKey.isPressed;
            bool shift = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            if (ctrl)
            {
                if (kb.zKey.wasPressedThisFrame) { if (shift) Redo(); else Undo(); }
                else if (kb.yKey.wasPressedThisFrame) Redo();
                else if (kb.aKey.wasPressedThisFrame) SelectAll();
                else if (kb.cKey.wasPressedThisFrame) Copy();
                else if (kb.xKey.wasPressedThisFrame && !_changedThisFrame) Cut();     // InputField 自己处理过了就不重复
                else if (kb.vKey.wasPressedThisFrame && !_changedThisFrame) Paste();
            }
            else if (kb.tabKey.wasPressedThisFrame) _tabPending = 2;
        }
        UpdateContentHeight();
        if (_caretEndPending > 0 && _field.isFocused && --_caretEndPending == 0)
        {
            _field.MoveTextEnd(false);
            _lastCaret = -1;
            if (_undo.Count == 0) _current.caret = _field.text.Length;   // 撤销回起点时光标也在末尾
        }
        // Tab：InputField 可能什么都不插，两帧后还没看到 \t 就自己插 4 个空格
        if (_tabPending > 0 && --_tabPending == 0 && _field.isFocused)
        {
            int caret = Mathf.Clamp(_field.caretPosition, 0, _field.text.Length);
            Apply(_field.text.Insert(caret, "    "), caret + 4);
            OnChanged(_field.text);
        }
        FollowCaret();
    }

    void LateUpdate() => _changedThisFrame = false;

    // ------------------------------------------------------------------ 快捷键

    void Undo()
    {
        if (_undo.Count == 0) return;
        _redo.Add(_current);
        _current = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        Apply(_current.text, _current.caret);
        _lastChangeTime = -10f;
    }

    void Redo()
    {
        if (_redo.Count == 0) return;
        _undo.Add(_current);
        _current = _redo[_redo.Count - 1];
        _redo.RemoveAt(_redo.Count - 1);
        Apply(_current.text, _current.caret);
        _lastChangeTime = -10f;
    }

    void Selection(out int a, out int b)
    {
        a = Mathf.Min(_field.selectionAnchorPosition, _field.selectionFocusPosition);
        b = Mathf.Max(_field.selectionAnchorPosition, _field.selectionFocusPosition);
        a = Mathf.Clamp(a, 0, _field.text.Length);
        b = Mathf.Clamp(b, 0, _field.text.Length);
    }

    void SelectAll()
    {
        _field.selectionAnchorPosition = 0;
        _field.selectionFocusPosition = _field.text.Length;
    }

    void Copy()
    {
        Selection(out int a, out int b);
        if (b > a) GUIUtility.systemCopyBuffer = _field.text.Substring(a, b - a);
    }

    void Cut()
    {
        Selection(out int a, out int b);
        if (b <= a) return;
        GUIUtility.systemCopyBuffer = _field.text.Substring(a, b - a);
        Apply(_field.text.Remove(a, b - a), a);
        OnChanged(_field.text);
    }

    void Paste()
    {
        string clip = GUIUtility.systemCopyBuffer;
        if (string.IsNullOrEmpty(clip)) return;
        clip = clip.Replace("\r\n", "\n").Replace("\r", "\n");
        Selection(out int a, out int b);
        string t = _field.text;
        if (b > a) t = t.Remove(a, b - a);
        Apply(t.Insert(a, clip), a + clip.Length);
        OnChanged(_field.text);
    }

    // ------------------------------------------------------------------ 内容高度

    int _lastHeightLen = -1, _followFrames;
    float _lastViewW = -1f;

    // Text 的高度要按「完整文本」算：InputField 只画得下 Text 矩形里的行，
    // 如果让 Text 自己按已画出的内容撑高度，空文本会永远撑不起来（0 行 → 0 高 → 0 行）。
    void UpdateContentHeight()
    {
        if (_scroll == null || _text == null) return;
        string t = _field.text;
        float w = _scroll.viewport.rect.width;
        if (t.Length == _lastHeightLen && Mathf.Approximately(w, _lastViewW)) return;
        _lastHeightLen = t.Length;
        _lastViewW = w;
        _followFrames = 2;   // 文本变了，行信息要下一帧才准，再跟一次光标
        var settings = _text.GetGenerationSettings(new Vector2(w, 0f));
        float h = _text.cachedTextGeneratorForLayout.GetPreferredHeight(t.Length == 0 ? " " : t, settings) / _text.pixelsPerUnit;
        _scroll.content.sizeDelta = new Vector2(0f, h + _text.fontSize);   // 多留一行，光标在末尾时不贴边
    }

    // ------------------------------------------------------------------ 视图跟随光标

    void FollowCaret()
    {
        if (_scroll == null || _text == null || !_field.isFocused) return;
        int caret = _field.caretPosition;
        if (caret == _lastCaret && _field.text.Length == _lastLen && _followFrames == 0) return;
        if (_followFrames > 0) _followFrames--;
        _lastCaret = caret;
        _lastLen = _field.text.Length;

        var gen = _text.cachedTextGenerator;
        if (gen == null || gen.lineCount == 0) return;
        int line = 0;
        for (int i = 0; i < gen.lineCount; i++) if (gen.lines[i].startCharIdx <= caret) line = i; else break;
        float top = -gen.lines[line].topY;                 // 相对文字顶端向下的距离
        float bottom = top + gen.lines[line].height;
        float viewH = _scroll.viewport.rect.height;
        float contentH = _scroll.content.rect.height;
        float offset = _scroll.content.anchoredPosition.y;  // 已经向上卷了多少
        if (top < offset) offset = top;
        else if (bottom > offset + viewH) offset = bottom - viewH;
        offset = Mathf.Clamp(offset, 0f, Mathf.Max(0f, contentH - viewH));
        _scroll.content.anchoredPosition = new Vector2(_scroll.content.anchoredPosition.x, offset);
    }
}
