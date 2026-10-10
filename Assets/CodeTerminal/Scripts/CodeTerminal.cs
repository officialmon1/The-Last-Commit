using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 场景里可交互的「终端」。玩家看着它、走到 interactDistance 以内按 E，就打开 Python 编辑器并加载 track 这条关卡链。
/// 挂到任何带 Collider 的物体上都行；没有 Renderer 的话会自己生成一个方块当占位模型。
/// </summary>
public class CodeTerminal : MonoBehaviour
{
    [Tooltip("Resources/Tracks/<track>.json 里的关卡链")] public string track = "dijkstra";
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
            cube.transform.localScale = new Vector3(0.8f, 1.7f, 0.5f);   // 和人差不多高，平视就能对上
            // 从 Resources 加载保证打包时 URP Lit 不被剔除（Shader.Find 在打包版里会拿到 null）
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
        // 粗一点的射线，不用精确对准
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
        float k = CodeEditorUI.ScaleFor(Screen.height);   // 高分屏（Retina）按比例放大
        _style ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        _style.fontSize = Mathf.RoundToInt(22 * k);
        GUI.Label(new Rect(0, Screen.height * 0.6f, Screen.width, 40 * k), hint, _style);
    }
}
