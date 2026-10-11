using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Panda;

/// <summary>
/// The end of the game: shown when the player walks through the opened portal (PortalController).
/// Shows the clear time, lets the player put a name on the online leaderboard (<see cref="Leaderboard"/>),
/// and offers Play again (fresh save, scene reloads) or Quit. Built at runtime like the code editor; no prefabs.
/// <see cref="ShowDeath"/> reuses it as the game-over screen when the clock runs out: no leaderboard, just Start over / Quit.
/// </summary>
public class GameEnd : MonoBehaviour
{
    public static GameEnd Instance { get; private set; }
    public static bool IsShown => Instance != null && Instance._root != null && Instance._root.activeSelf;

    GameObject _root, _scoreSection;
    RectTransform _panel;
    Text _title, _time, _status, _board, _againLabel;
    InputField _name;
    Button _submit;
    int _fontSize, _seconds;
    bool _submitted;
    Font _ui, _mono;

    public static GameEnd Ensure()
    {
        if (Instance == null) Instance = new GameObject("GameEnd").AddComponent<GameEnd>();
        return Instance;
    }

    void Awake()
    {
        Instance = this;
        _fontSize = Mathf.Max(10, Mathf.RoundToInt(20 * CodeEditorUI.ScaleFor(Screen.height)));
        _ui = ForestTheme.ElegantFont() ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
        _mono = null;
        foreach (var n in new[] { "Consolas", "Cascadia Mono", "Menlo", "Monaco", "Courier New" })
            if (installed.Contains(n)) { _mono = Font.CreateDynamicFontFromOSFont(n, _fontSize); break; }
        if (_mono == null) _mono = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Build();
        _root.SetActive(false);
    }

    /// <summary>Shows the end screen for a run cleared in <paramref name="seconds"/>.</summary>
    public void Show(float seconds)
    {
        if (IsShown) return;
        _seconds = Mathf.Max(1, Mathf.RoundToInt(seconds));
        _submitted = false;
        Open(true, "YOU ESCAPED THE FOREST", "Play again");
        if (PandaCompanion.Instance != null) PandaCompanion.Instance.ShowCelebration("You made it out! I knew you would.");

        _time.text = "You escaped in " + Leaderboard.FormatTime(_seconds);
        _name.text = "";
        _name.interactable = true;
        _submit.interactable = true;
        _status.text = "Enter a name to put this time on the leaderboard.";
        _board.text = "Loading the fastest escapes...";
        StartCoroutine(FocusNameWhenKeysReleased());
        Leaderboard.Fetch(this, r =>
        {
            if (this == null || _submitted) return;
            _board.text = r.error != null ? "Leaderboard unavailable: " + r.error : Render(r.entries, null);
        });
    }

    /// <summary>Game over: the 36 minutes ran out and the player became part of the forest. Only Start over (fresh run) or Quit.</summary>
    public void ShowDeath()
    {
        if (IsShown) return;
        Open(false, "YOU BECAME PART OF THE FOREST", "Start over");
        _time.text = "The thirty-six minutes ran out.";
    }

    void Open(bool withScore, string title, string again)
    {
        if (CodeEditorUI.IsOpen) CodeEditorUI.Instance.Close();
        var fpc = FindAnyObjectByType<FirstPersonController>();
        if (fpc != null) fpc.SetInputLocked(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        _title.text = title;
        _againLabel.text = again;
        _scoreSection.SetActive(withScore);
        _panel.sizeDelta = new Vector2(_fontSize * 34f, _fontSize * (withScore ? 27f : 11f));
        _root.SetActive(true);
    }

    /// <summary>
    /// The player usually walks into the gate with W held down; a focused field would fill up with w's.
    /// So the field only takes focus (empty) after a short pause once no key is held any more.
    /// </summary>
    IEnumerator FocusNameWhenKeysReleased()
    {
        float t0 = Time.unscaledTime;
        while (Time.unscaledTime - t0 < 0.6f || (Keyboard.current != null && Keyboard.current.anyKey.isPressed)) yield return null;
        if (!IsShown || _submitted) yield break;
        _name.text = "";
        _name.ActivateInputField();
    }

    void SubmitScore()
    {
        if (_submitted) return;
        string name = (_name.text ?? "").Trim();
        if (name.Length == 0) { _status.text = "Type a name first."; return; }
        if (name.Length > Leaderboard.MaxNameLength) name = name.Substring(0, Leaderboard.MaxNameLength);
        _submit.interactable = false;
        _name.interactable = false;
        _status.text = "Saving...";
        Leaderboard.Submit(this, name, _seconds, r =>
        {
            if (this == null) return;
            if (r.error != null)
            {
                _status.text = "Could not save: " + r.error;
                _submit.interactable = true;
                _name.interactable = true;
                return;
            }
            _submitted = true;
            _status.text = r.rank > 0 ? $"Saved! You are #{r.rank} of all escapes." : "Saved!";
            _board.text = Render(r.entries, name);
        });
    }

    string Render(List<Leaderboard.Entry> entries, string highlight)
    {
        if (entries == null || entries.Count == 0) return "No escapes recorded yet. Be the first!";
        var lines = new List<string> { " #  Name                  Time" };
        bool marked = false;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = !marked && highlight != null && e.name == highlight && e.seconds == _seconds;
            if (mine) marked = true;
            lines.Add($"{i + 1,2}. {Truncate(e.name, Leaderboard.MaxNameLength),-20}  {Leaderboard.FormatTime(e.seconds)}{(mine ? "   <- you" : "")}");
        }
        return string.Join("\n", lines);
    }

    static string Truncate(string s, int n) => string.IsNullOrEmpty(s) ? "?" : s.Length <= n ? s : s.Substring(0, n);

    void PlayAgain()
    {
        if (CodeEditorUI.Instance != null) CodeEditorUI.Instance.DeleteSave();   // next run starts from zero
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ------------------------------------------------------------------ UI

    void Build()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

        var canvasGo = new GameObject("GameEndCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 300;   // above the code editor and the panda UI
        canvasGo.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

        // dim everything behind
        var dim = Image("Dim", canvasGo.transform, new Color(0.03f, 0.05f, 0.04f, 0.9f), false);
        _root = dim.gameObject;
        Stretch(dim.rectTransform, 0f);

        var panel = Image("Panel", dim.transform, ForestTheme.PanelBg, true);
        var prt = _panel = panel.rectTransform;
        prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
        prt.sizeDelta = new Vector2(_fontSize * 34f, _fontSize * 27f);
        var vl = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        vl.padding = new RectOffset(_fontSize * 3 / 2, _fontSize * 3 / 2, _fontSize, _fontSize);
        vl.spacing = _fontSize / 2;
        vl.childControlWidth = vl.childControlHeight = true;
        vl.childForceExpandWidth = true;
        vl.childForceExpandHeight = false;

        var banner = Image("Banner", panel.transform, ForestTheme.Amber, true);
        Fix(banner.gameObject, _fontSize * 2.2f);
        var title = _title = Label("Title", banner.transform, _ui, _fontSize + 4, new Color(0.16f, 0.1f, 0.04f), TextAnchor.MiddleCenter);
        title.fontStyle = FontStyle.Bold;
        title.text = "YOU ESCAPED THE FOREST";
        Stretch(title.rectTransform, 4f);
        ForestTheme.AddLeafAccent(banner.transform, new Vector2(0f, 0.5f), new Vector2(10f, 0f));
        ForestTheme.AddLeafAccent(banner.transform, new Vector2(1f, 0.5f), new Vector2(-10f, 0f));

        _time = Label("Time", panel.transform, _ui, _fontSize + 6, ForestTheme.TextCream, TextAnchor.MiddleCenter);
        Fix(_time.gameObject, _fontSize * 2f);

        // everything about the leaderboard lives in one section so the game-over screen can hide it
        var score = new GameObject("Score", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        score.transform.SetParent(panel.transform, false);
        var svl = score.GetComponent<VerticalLayoutGroup>();
        svl.spacing = vl.spacing;
        svl.childControlWidth = svl.childControlHeight = true;
        svl.childForceExpandWidth = true;
        svl.childForceExpandHeight = false;
        score.GetComponent<LayoutElement>().flexibleHeight = 1f;
        _scoreSection = score;

        var nameRow = Row("NameRow", score.transform, _fontSize * 2.2f);
        var nameLabel = Label("NameLabel", nameRow, _ui, _fontSize, ForestTheme.TextMuted, TextAnchor.MiddleLeft);
        nameLabel.text = "Your name:";
        nameLabel.gameObject.AddComponent<LayoutElement>().preferredWidth = _fontSize * 6f;
        _name = MakeInputField("Name", nameRow, "up to 20 characters");
        _name.gameObject.GetComponent<LayoutElement>().flexibleWidth = 1f;
        _name.characterLimit = Leaderboard.MaxNameLength;
        _name.onSubmit.AddListener(_ => SubmitScore());
        _submit = Button("Submit", nameRow, "Save to leaderboard", SubmitScore, ForestTheme.AccentGreen);
        _submit.GetComponent<LayoutElement>().preferredWidth = _fontSize * 11f;

        _status = Label("Status", score.transform, _ui, _fontSize - 2, ForestTheme.TextMuted, TextAnchor.MiddleLeft);
        Fix(_status.gameObject, _fontSize * 1.4f);

        var boardTitle = Label("BoardTitle", score.transform, _ui, _fontSize + 2, ForestTheme.Amber, TextAnchor.MiddleLeft);
        boardTitle.fontStyle = FontStyle.Bold;
        boardTitle.text = "FASTEST ESCAPES";
        Fix(boardTitle.gameObject, _fontSize * 1.6f);

        var boardBox = Image("Board", score.transform, ForestTheme.BoxBg, true);
        boardBox.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        _board = Label("BoardText", boardBox.transform, _mono, _fontSize - 2, ForestTheme.TextCream, TextAnchor.UpperLeft);
        _board.verticalOverflow = VerticalWrapMode.Overflow;
        Stretch(_board.rectTransform, _fontSize / 2f);

        var buttons = Row("Buttons", panel.transform, _fontSize * 2.4f);
        _againLabel = Button("Again", buttons, "Play again", PlayAgain, ForestTheme.AccentGreen).GetComponentInChildren<Text>();
        Button("Quit", buttons, "Quit", Quit, ForestTheme.AccentMuted);
    }

    static Image Image(string name, Transform parent, Color color, bool rounded)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        if (rounded) { img.sprite = ForestTheme.PanelSprite; img.type = UnityEngine.UI.Image.Type.Sliced; }
        return img;
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
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static Transform Row(string name, Transform parent, float height)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        var hl = go.GetComponent<HorizontalLayoutGroup>();
        hl.spacing = 8f;
        hl.childControlWidth = hl.childControlHeight = true;
        hl.childForceExpandWidth = false;
        hl.childForceExpandHeight = true;
        var le = go.GetComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = height;
        le.flexibleHeight = 0f;
        return go.transform;
    }

    static void Fix(GameObject go, float h)
    {
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.preferredHeight = le.minHeight = h;
        le.flexibleHeight = 0f;
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
        var bg = Image(name, parent, new Color(0.071f, 0.102f, 0.082f), true);
        bg.gameObject.AddComponent<LayoutElement>();
        var field = bg.gameObject.AddComponent<InputField>();
        var text = Label("Text", bg.transform, _ui, _fontSize, ForestTheme.TextCream, TextAnchor.MiddleLeft);
        Stretch(text.rectTransform, _fontSize / 2f);
        var ph = Label("Placeholder", bg.transform, _ui, _fontSize, new Color(0.45f, 0.5f, 0.44f), TextAnchor.MiddleLeft);
        ph.text = placeholder;
        Stretch(ph.rectTransform, _fontSize / 2f);
        field.textComponent = text;
        field.placeholder = ph;
        field.lineType = InputField.LineType.SingleLine;
        field.caretWidth = 2;
        field.customCaretColor = true;
        field.caretColor = ForestTheme.TextCream;
        return field;
    }

    Button Button(string name, Transform parent, string label, UnityEngine.Events.UnityAction onClick, Color color)
    {
        var img = Image(name, parent, color, true);
        img.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        var btn = img.gameObject.AddComponent<Button>();
        btn.onClick.AddListener(onClick);
        var t = Label("Label", img.transform, _ui, _fontSize, Color.white, TextAnchor.MiddleCenter);
        Stretch(t.rectTransform, 4f);
        t.text = label;
        return btn;
    }
}
