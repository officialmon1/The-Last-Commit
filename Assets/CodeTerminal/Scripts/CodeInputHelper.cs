using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// A ScrollRect that only reacts to the mouse wheel, never to drags: dragging is left to the InputField for text selection.
/// </summary>
public class WheelOnlyScrollRect : ScrollRect
{
    public override void OnBeginDrag(PointerEventData eventData) { }
    public override void OnDrag(PointerEventData eventData) { }
    public override void OnEndDrag(PointerEventData eventData) { }
}

/// <summary>
/// Adds the code-editor basics that uGUI's InputField lacks (attach to the same object as the InputField):
/// Ctrl+Z / Ctrl+Y (Ctrl+Shift+Z) undo/redo, Ctrl+C/V/X/A, Tab = 4 spaces,
/// auto-indent on Enter (one extra level when the previous line ends with ':'), and the ScrollRect follows the caret.
/// </summary>
[RequireComponent(typeof(InputField))]
public class CodeInputHelper : MonoBehaviour
{
    public bool autoIndent = true;
    [Tooltip("Edits closer together than this many seconds are grouped into one undo step")] public float undoGroupSeconds = 0.5f;

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

    /// <summary>Call after setting the text programmatically: clears the undo stack and makes the current text the baseline.</summary>
    public void ResetHistory()
    {
        _undo.Clear();
        _redo.Clear();
        _current = new State { text = _field.text, caret = _field.caretPosition };
        _lastChangeTime = -10f;
    }

    /// <summary>ActivateInputField selects all on the next frame, so move the caret to the end two frames later.</summary>
    public void MoveCaretToEnd() => _caretEndPending = 2;
    int _caretEndPending;

    void OnChanged(string t)
    {
        if (_applying) return;
        bool burst = _changedThisFrame;   // a second change in the same frame means a bulk edit (paste etc.): no auto-indent
        _changedThisFrame = true;
        int caret = _field.caretPosition;
        bool newline = t.Length == _current.text.Length + 1 && caret > 0 && caret <= t.Length && t[caret - 1] == '\n';

        // The InputField may insert a literal \t for Tab: normalize to 4 spaces
        if (t.IndexOf('\t') >= 0)
        {
            int before = 0;
            for (int i = 0; i < Mathf.Min(caret, t.Length); i++) if (t[i] == '\t') before++;
            t = t.Replace("\t", "    ");
            caret += before * 3;
            Apply(t, caret);
            _tabPending = 0;
        }
        // Auto-indent on Enter: only when exactly one '\n' was added
        else if (autoIndent && newline && !burst)
        {
            int lineStart = caret >= 2 ? t.LastIndexOf('\n', caret - 2) + 1 : 0;   // start of the previous line
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

        // Record undo: start a new step after undoGroupSeconds of silence; a newline always starts its own step (line-wise undo like a normal editor)
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
                else if (kb.xKey.wasPressedThisFrame && !_changedThisFrame) Cut();     // skip if the InputField already handled it this frame
                else if (kb.vKey.wasPressedThisFrame && !_changedThisFrame) Paste();
            }
            else if (kb.tabKey.wasPressedThisFrame) _tabPending = 2;
        }
        UpdateContentHeight();
        if (_caretEndPending > 0 && _field.isFocused && --_caretEndPending == 0)
        {
            _field.MoveTextEnd(false);
            _lastCaret = -1;
            if (_undo.Count == 0) _current.caret = _field.text.Length;   // undoing back to the baseline keeps the caret at the end
        }
        // Tab: the InputField may insert nothing; if no \t showed up after two frames, insert 4 spaces ourselves
        if (_tabPending > 0 && --_tabPending == 0 && _field.isFocused)
        {
            int caret = Mathf.Clamp(_field.caretPosition, 0, _field.text.Length);
            Apply(_field.text.Insert(caret, "    "), caret + 4);
            OnChanged(_field.text);
        }
        FollowCaret();
    }

    void LateUpdate() => _changedThisFrame = false;

    // ------------------------------------------------------------------ Shortcuts

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

    // ------------------------------------------------------------------ Content height

    int _lastHeightLen = -1, _followFrames;
    float _lastViewW = -1f;

    // The Text height must be computed from the FULL text: the InputField only draws the lines that fit the Text rect,
    // so if the Text sized itself from what is drawn, empty text would never grow (0 lines -> 0 height -> 0 lines).
    void UpdateContentHeight()
    {
        if (_scroll == null || _text == null) return;
        string t = _field.text;
        float w = _scroll.viewport.rect.width;
        if (t.Length == _lastHeightLen && Mathf.Approximately(w, _lastViewW)) return;
        _lastHeightLen = t.Length;
        _lastViewW = w;
        _followFrames = 2;   // text changed; line info is only accurate next frame, so follow the caret once more
        var settings = _text.GetGenerationSettings(new Vector2(w, 0f));
        float h = _text.cachedTextGeneratorForLayout.GetPreferredHeight(t.Length == 0 ? " " : t, settings) / _text.pixelsPerUnit;
        _scroll.content.sizeDelta = new Vector2(0f, h + _text.fontSize);   // one spare line so the caret at the end is not flush with the edge
    }

    // ------------------------------------------------------------------ Scroll to follow the caret

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
        float top = -gen.lines[line].topY;                 // distance down from the top of the text
        float bottom = top + gen.lines[line].height;
        float viewH = _scroll.viewport.rect.height;
        float contentH = _scroll.content.rect.height;
        float offset = _scroll.content.anchoredPosition.y;  // how far the content is scrolled up already
        if (top < offset) offset = top;
        else if (bottom > offset + viewH) offset = bottom - viewH;
        offset = Mathf.Clamp(offset, 0f, Mathf.Max(0f, contentH - viewH));
        _scroll.content.anchoredPosition = new Vector2(_scroll.content.anchoredPosition.x, offset);
    }
}
