using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Outermost game state. The player can walk around from the first frame, but the level pillars (every CodeTerminal in the scene)
/// stay hidden until the game is started: press the start key, or call <see cref="StartGame"/> from any menu button
/// (the Amber Ruins start screen calls it from "Begin Escape"). Pillars with hideUntilGameStart off stay visible but can't be used yet.
/// Put one of these on any object in the scene.
/// </summary>
public class GameStart : MonoBehaviour
{
    public static GameStart Instance { get; private set; }
    public static bool Started { get; private set; }
    /// <summary>Fired once when the player starts the game (the pillars are visible from then on).</summary>
    public static event Action GameStarted;

    [Tooltip("Key that starts the game")] public Key startKey = Key.Enter;
    public string prompt = "Press Enter to start";
    [Tooltip("Off when a start menu calls StartGame instead (no key, no prompt).")] public bool useStartKey = true;
    [Tooltip("Canvases kept hidden until the game starts (e.g. the panda's speech bubble, so it doesn't show over the start menu).")]
    public Canvas[] hideUntilStart = Array.Empty<Canvas>();

    CodeTerminal[] _pillars = Array.Empty<CodeTerminal>();
    GUIStyle _style;

    void Awake()
    {
        Instance = this;
        Started = false;
        _pillars = Array.FindAll(FindObjectsByType<CodeTerminal>(), p => p.hideUntilGameStart);
        foreach (var p in _pillars) p.gameObject.SetActive(false);
        foreach (var c in hideUntilStart) if (c != null) c.enabled = false;
    }

    void Update()
    {
        if (Started || !useStartKey) return;
        var kb = Keyboard.current;
        if (kb != null && kb[startKey].wasPressedThisFrame) StartGame();
    }

    /// <summary>Shows the level pillars. Safe to call from a UI button; does nothing the second time.</summary>
    public void StartGame()
    {
        if (Started) return;
        Started = true;
        foreach (var p in _pillars) if (p != null) p.gameObject.SetActive(true);
        foreach (var c in hideUntilStart) if (c != null) c.enabled = true;
        if (!CodeEditorUI.IsOpen)
        {
            Cursor.lockState = CursorLockMode.Locked;   // straight into first-person look, no extra click needed
            Cursor.visible = false;
        }
        GameStarted?.Invoke();
    }

    void OnGUI()
    {
        if (Started || !useStartKey) return;
        float k = CodeEditorUI.ScaleFor(Screen.height);   // scale up on high-DPI (Retina) screens
        _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        _style.fontSize = Mathf.RoundToInt(28 * k);
        GUI.Label(new Rect(0, Screen.height * 0.12f, Screen.width, 50 * k), prompt, _style);
    }
}
