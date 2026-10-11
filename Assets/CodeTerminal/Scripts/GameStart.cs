using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Outermost game state. The player can walk around from the first frame, but the level pillars (every CodeTerminal in the scene)
/// stay hidden until the game is started: press the start key, or call <see cref="StartGame"/> from any menu button.
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

    CodeTerminal[] _pillars = Array.Empty<CodeTerminal>();
    GUIStyle _style;

    void Awake()
    {
        Instance = this;
        Started = false;
        _pillars = FindObjectsByType<CodeTerminal>();
        foreach (var p in _pillars) p.gameObject.SetActive(false);
    }

    void Update()
    {
        if (Started) return;
        var kb = Keyboard.current;
        if (kb != null && kb[startKey].wasPressedThisFrame) StartGame();
    }

    /// <summary>Shows the level pillars. Safe to call from a UI button; does nothing the second time.</summary>
    public void StartGame()
    {
        if (Started) return;
        Started = true;
        foreach (var p in _pillars) if (p != null) p.gameObject.SetActive(true);
        GameStarted?.Invoke();
    }

    void OnGUI()
    {
        if (Started) return;
        float k = CodeEditorUI.ScaleFor(Screen.height);   // scale up on high-DPI (Retina) screens
        _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        _style.fontSize = Mathf.RoundToInt(28 * k);
        GUI.Label(new Rect(0, Screen.height * 0.12f, Screen.width, 50 * k), prompt, _style);
    }
}
