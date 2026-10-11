using UnityEngine;
using UnityEngine.InputSystem;
using Panda;

/// <summary>
/// A pillar in the world that opens one level of a track. Look at it within interactDistance and press E to open the Python editor on that level.
/// The four prerequisite levels are independent; the larger final pillar unlocks after all four are passed.
/// Attach to any object with a Collider; without a Renderer it spawns a placeholder pillar with the level number floating above it.
/// On an object that already has a model (e.g. a broken ruin) the level label floats above that model instead.
/// </summary>
public class CodeTerminal : MonoBehaviour
{
    [Tooltip("Track to open: Resources/Tracks/<track>.json")] public string track = "dijkstra";
    [Tooltip("Which level of the track this pillar opens (1 = first problem)")] public int level = 1;
    [Tooltip("Larger pillar for the final problem, with prerequisite progress and a locked/ready state")] public bool finalProblem;
    public float interactDistance = 3f;
    [Tooltip("GameStart hides this pillar until the game starts. Turn off for pillars that are part of the scenery (ruins, the portal).")]
    public bool hideUntilGameStart = true;
    [Tooltip("Optional light tinted with the pillar's state (to do / solved / locked / final), so it can be spotted from afar.")]
    public Light stateLight;

    static readonly Color TodoColor = new Color(0.2f, 0.75f, 0.7f);
    static readonly Color DoneColor = new Color(0.35f, 0.8f, 0.35f);
    static readonly Color LockedColor = new Color(0.43f, 0.42f, 0.4f);
    static readonly Color FinalColor = new Color(0.9f, 0.65f, 0.22f);

    bool _inRange, _passedShown;
    int _stateShown = -1;
    GUIStyle _style;
    Material _mat;
    TextMesh _label;
    Renderer[] _model;   // the object's own model when it has one; the label follows its top

    void Awake()
    {
        if (GetComponentInChildren<Renderer>() != null)
        {
            _model = GetComponentsInChildren<Renderer>(true);
            CreateLabel(Vector3.zero);
            _label.transform.localScale = InverseScale(transform.lossyScale);
            PlaceLabelAboveModel();
            return;
        }
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

        CreateLabel(new Vector3(0f, halfHeight * 2f + 0.5f, 0f));
    }

    /// <summary>Level number floating above the pillar, turned toward the player every frame.</summary>
    void CreateLabel(Vector3 localPosition)
    {
        var lbl = new GameObject("Label", typeof(TextMesh));
        lbl.transform.SetParent(transform, false);
        lbl.transform.localPosition = localPosition;
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

    static Vector3 InverseScale(Vector3 s) => new Vector3(1f / Mathf.Max(0.0001f, s.x), 1f / Mathf.Max(0.0001f, s.y), 1f / Mathf.Max(0.0001f, s.z));

    /// <summary>Keeps the label just above whatever part of the model is visible (a repaired ruin is taller than the broken one).</summary>
    void PlaceLabelAboveModel()
    {
        bool any = false;
        Bounds b = default;
        foreach (var r in _model)
        {
            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.transform == _label.transform) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        if (!any) return;
        _label.transform.position = new Vector3(b.center.x, b.max.y + 0.7f, b.center.z);
    }

    void Update()
    {
        var cam = Camera.main;
        if (_label != null && cam != null)
        {
            var d = _label.transform.position - cam.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.001f) _label.transform.rotation = Quaternion.LookRotation(d);   // a TextMesh reads correctly when its forward points away from the viewer
            if (_model != null) PlaceLabelAboveModel();
        }
        var editor = CodeEditorUI.Instance;
        _passedShown = editor != null && editor.IsPassed(track, level - 1);
        bool unlocked = !finalProblem || (editor != null && editor.IsUnlocked(track, level - 1));
        int state = !unlocked ? 0 : _passedShown ? 2 : 1;
        if (state != _stateShown)
        {
            _stateShown = state;
            var color = !unlocked ? LockedColor : _passedShown ? DoneColor : finalProblem ? FinalColor : TodoColor;
            if (_mat != null) _mat.SetColor("_BaseColor", color);
            if (stateLight != null) stateLight.color = color;
        }
        if (_label != null) _label.text = finalProblem
            ? "FINAL\n" + (!unlocked ? $"Locked  {editor?.PrerequisitesPassed(track) ?? 0}/{level - 1}" : _passedShown ? "Solved" : "Ready")
            : level + (_passedShown ? "  Solved" : "");

        _inRange = false;
        if (CodeEditorUI.IsOpen || cam == null) return;
        if (GameStart.Instance != null && !GameStart.Started) return;   // still on the start screen
        if (GameEnd.IsShown) return;
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
            ? $"Final problem locked ({CodeEditorUI.Instance?.PrerequisitesPassed(track) ?? 0}/{level - 1}) - Solve the four broken pillars first"
            : $"{(finalProblem ? "Final problem" : "Level " + level)}{(_passedShown ? "  (solved)" : "")}   -   Press E to open";
        GUI.Label(new Rect(0, Screen.height * 0.6f, Screen.width, 40 * k), hint, _style);
    }
}
