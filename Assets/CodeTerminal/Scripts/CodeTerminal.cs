using UnityEngine;
using UnityEngine.InputSystem;
using Panda;

/// <summary>
/// A pillar in the world that opens one level of a track. Look at it within interactDistance and press E to open the Python editor on that level.
/// The four prerequisite levels are independent; the larger final pillar unlocks after all four are passed.
/// Attach to any object with a Collider; without a Renderer it spawns a placeholder pillar with the level number floating above it.
/// </summary>
public class CodeTerminal : MonoBehaviour
{
    [Tooltip("Track to open: Resources/Tracks/<track>.json")] public string track = "dijkstra";
    [Tooltip("Which level of the track this pillar opens (1 = first problem)")] public int level = 1;
    [Tooltip("Larger pillar for the final problem, with prerequisite progress and a locked/ready state")] public bool finalProblem;
    public float interactDistance = 3f;

    static readonly Color TodoColor = new Color(0.2f, 0.75f, 0.7f);
    static readonly Color DoneColor = new Color(0.35f, 0.8f, 0.35f);
    static readonly Color LockedColor = new Color(0.43f, 0.42f, 0.4f);
    static readonly Color FinalColor = new Color(0.9f, 0.65f, 0.22f);

    bool _inRange, _passedShown;
    int _stateShown = -1;
    GUIStyle _style;
    Material _mat;
    TextMesh _label;

    void Awake()
    {
        if (GetComponentInChildren<Renderer>() != null) return;
        var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "Pillar";
        pillar.transform.SetParent(transform, false);
        float halfHeight = finalProblem ? 1.8f : 0.9f;
        float width = finalProblem ? 1.4f : 0.7f;
        pillar.transform.localPosition = new Vector3(0f, halfHeight, 0f);
        pillar.transform.localScale = new Vector3(width, halfHeight, width);
        // load from Resources so URP Lit is not stripped from builds (Shader.Find returns null in a build)
        _mat = new Material(Resources.Load<Material>("BaseLit"));
        _mat.SetColor("_BaseColor", TodoColor);
        pillar.GetComponent<Renderer>().sharedMaterial = _mat;

        // level number floating above the pillar, turned toward the player every frame
        var lbl = new GameObject("Label", typeof(TextMesh));
        lbl.transform.SetParent(transform, false);
        lbl.transform.localPosition = new Vector3(0f, halfHeight * 2f + 0.5f, 0f);
        var tm = lbl.GetComponent<TextMesh>();
        tm.text = level.ToString();
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.fontSize = 96;
        tm.characterSize = 0.08f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        lbl.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        _label = tm;
    }

    void Update()
    {
        var cam = Camera.main;
        if (_label != null && cam != null)
        {
            var d = _label.transform.position - cam.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.001f) _label.transform.rotation = Quaternion.LookRotation(d);   // a TextMesh reads correctly when its forward points away from the viewer
        }
        var editor = CodeEditorUI.Instance;
        _passedShown = editor != null && editor.IsPassed(track, level - 1);
        bool unlocked = !finalProblem || (editor != null && editor.IsUnlocked(track, level - 1));
        int state = !unlocked ? 0 : _passedShown ? 2 : 1;
        if (state != _stateShown)
        {
            _stateShown = state;
            if (_mat != null) _mat.SetColor("_BaseColor", !unlocked ? LockedColor : _passedShown ? DoneColor : finalProblem ? FinalColor : TodoColor);
        }
        if (_label != null) _label.text = finalProblem
            ? "FINAL\n" + (!unlocked ? $"Locked  {editor?.PrerequisitesPassed(track) ?? 0}/{level - 1}" : _passedShown ? "Solved" : "Ready")
            : level + (_passedShown ? "  Solved" : "");

        _inRange = false;
        if (CodeEditorUI.IsOpen || cam == null) return;
        var ct = cam.transform;
        // a thick ray so precise aiming is not needed
        if (Physics.SphereCast(ct.position, 0.3f, ct.forward, out var hit, interactDistance) && hit.collider.GetComponentInParent<CodeTerminal>() == this)
        {
            _inRange = true;
            var kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame && CodeEditorUI.Instance != null)
            {
                BackAwayIfTooClose(hit.distance, ct);
                CodeEditorUI.Instance.Open(track, level - 1);
            }
        }
    }

    /// <summary>
    /// Panda waits at a fixed depth in front of the camera once the editor opens (PandaCompanion.editorWaitDepth).
    /// If the player pressed E while standing closer to this pillar than that, the pillar itself would sit between
    /// the camera and Panda's spot and hide her behind it. Step the player back before the editor opens -- using
    /// CharacterController.Move so it respects collisions and can't push them through a wall behind them.
    /// </summary>
    void BackAwayIfTooClose(float hitDistance, Transform cam)
    {
        float minDistance = (PandaCompanion.Instance != null ? PandaCompanion.Instance.editorWaitDepth : 2.3f) + 0.3f;
        float shortfall = minDistance - hitDistance;
        if (shortfall <= 0f) return;
        var cc = cam.GetComponentInParent<CharacterController>();
        if (cc == null) return;
        Vector3 back = -cam.forward; back.y = 0f;
        if (back.sqrMagnitude < 0.0001f) return;
        cc.Move(back.normalized * shortfall);
    }

    void OnGUI()
    {
        if (!_inRange) return;
        float k = CodeEditorUI.ScaleFor(Screen.height);   // scale up on high-DPI (Retina) screens
        _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = ForestTheme.TextCream }, font = ForestTheme.ElegantFont() };
        _style.fontSize = Mathf.RoundToInt(22 * k);
        string hint = finalProblem && _stateShown == 0
            ? $"Final problem locked ({CodeEditorUI.Instance?.PrerequisitesPassed(track) ?? 0}/{level - 1}) - Complete the four nearby pillars first"
            : $"{(finalProblem ? "Final problem" : "Level " + level)}{(_passedShown ? "  (solved)" : "")}   -   Press E to open";
        GUI.Label(new Rect(0, Screen.height * 0.6f, Screen.width, 40 * k), hint, _style);
    }
}
