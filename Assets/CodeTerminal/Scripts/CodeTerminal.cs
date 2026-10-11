using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A pillar in the world that opens one level of a track. Look at it within interactDistance and press E to open the Python editor on that level.
/// Levels are independent: place one pillar per level (1..5) anywhere; the player may do them in any order.
/// Attach to any object with a Collider; without a Renderer it spawns a placeholder pillar with the level number floating above it.
/// </summary>
public class CodeTerminal : MonoBehaviour
{
    [Tooltip("Track to open: Resources/Tracks/<track>.json")] public string track = "dijkstra";
    [Tooltip("Which level of the track this pillar opens (1 = first problem)")] public int level = 1;
    public float interactDistance = 3f;

    static readonly Color TodoColor = new Color(0.2f, 0.75f, 0.7f);
    static readonly Color DoneColor = new Color(0.35f, 0.8f, 0.35f);

    bool _inRange, _passedShown;
    GUIStyle _style;
    Material _mat;
    Transform _label;

    void Awake()
    {
        if (GetComponentInChildren<Renderer>() != null) return;
        var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.name = "Pillar";
        pillar.transform.SetParent(transform, false);
        pillar.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        pillar.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);   // 1.8 m tall (above eye height, so looking straight at it hits), 0.7 m wide
        // load from Resources so URP Lit is not stripped from builds (Shader.Find returns null in a build)
        _mat = new Material(Resources.Load<Material>("BaseLit"));
        _mat.SetColor("_BaseColor", TodoColor);
        pillar.GetComponent<Renderer>().sharedMaterial = _mat;

        // level number floating above the pillar, turned toward the player every frame
        var lbl = new GameObject("Label", typeof(TextMesh));
        lbl.transform.SetParent(transform, false);
        lbl.transform.localPosition = new Vector3(0f, 2.3f, 0f);
        var tm = lbl.GetComponent<TextMesh>();
        tm.text = level.ToString();
        tm.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        tm.fontSize = 96;
        tm.characterSize = 0.08f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = Color.white;
        lbl.GetComponent<MeshRenderer>().sharedMaterial = tm.font.material;
        _label = lbl.transform;
    }

    void Update()
    {
        var cam = Camera.main;
        if (_label != null && cam != null)
        {
            var d = _label.position - cam.transform.position;
            d.y = 0f;
            if (d.sqrMagnitude > 0.001f) _label.rotation = Quaternion.LookRotation(d);   // a TextMesh reads correctly when its forward points away from the viewer
        }
        bool passed = CodeEditorUI.Instance != null && CodeEditorUI.Instance.IsPassed(track, level - 1);
        if (passed != _passedShown)
        {
            _passedShown = passed;
            if (_mat != null) _mat.SetColor("_BaseColor", passed ? DoneColor : TodoColor);
        }

        _inRange = false;
        if (CodeEditorUI.IsOpen || cam == null) return;
        var ct = cam.transform;
        // a thick ray so precise aiming is not needed
        if (Physics.SphereCast(ct.position, 0.3f, ct.forward, out var hit, interactDistance) && hit.collider.GetComponentInParent<CodeTerminal>() == this)
        {
            _inRange = true;
            var kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame && CodeEditorUI.Instance != null) CodeEditorUI.Instance.Open(track, level - 1);
        }
    }

    void OnGUI()
    {
        if (!_inRange) return;
        float k = CodeEditorUI.ScaleFor(Screen.height);   // scale up on high-DPI (Retina) screens
        _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        _style.fontSize = Mathf.RoundToInt(22 * k);
        GUI.Label(new Rect(0, Screen.height * 0.6f, Screen.width, 40 * k), $"Level {level}{(_passedShown ? "  (solved)" : "")}   -   Press E to open", _style);
    }
}
