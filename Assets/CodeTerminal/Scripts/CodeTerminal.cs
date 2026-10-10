using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// An interactable "terminal" in the scene. Look at it within interactDistance and press E to open the Python editor on this track.
/// Attach to any object with a Collider; without a Renderer it spawns a placeholder block.
/// </summary>
public class CodeTerminal : MonoBehaviour
{
    [Tooltip("Track to open: Resources/Tracks/<track>.json")] public string track = "dijkstra";
    public float interactDistance = 3f;
    public string hint = "Press E to use the terminal";

    bool _inRange;
    GUIStyle _style;

    void Awake()
    {
        if (GetComponentInChildren<Renderer>() == null)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Placeholder";
            cube.transform.SetParent(transform, false);
            cube.transform.localPosition = new Vector3(0f, 0.85f, 0f);
            cube.transform.localScale = new Vector3(0.8f, 1.7f, 0.5f);   // about human height so looking straight ahead hits it
            // load from Resources so URP Lit is not stripped from builds (Shader.Find returns null in a build)
            var m = new Material(Resources.Load<Material>("BaseLit"));
            m.SetColor("_BaseColor", new Color(0.2f, 0.75f, 0.7f));
            cube.GetComponent<Renderer>().sharedMaterial = m;
        }
    }

    void Update()
    {
        _inRange = false;
        if (CodeEditorUI.IsOpen || Camera.main == null) return;
        var cam = Camera.main.transform;
        // a thick ray so precise aiming is not needed
        if (Physics.SphereCast(cam.position, 0.3f, cam.forward, out var hit, interactDistance) && hit.collider.GetComponentInParent<CodeTerminal>() == this)
        {
            _inRange = true;
            var kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame && CodeEditorUI.Instance != null) CodeEditorUI.Instance.Open(track);
        }
    }

    void OnGUI()
    {
        if (!_inRange) return;
        float k = CodeEditorUI.ScaleFor(Screen.height);   // scale up on high-DPI (Retina) screens
        _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        _style.fontSize = Mathf.RoundToInt(22 * k);
        GUI.Label(new Rect(0, Screen.height * 0.6f, Screen.width, 40 * k), hint, _style);
    }
}
