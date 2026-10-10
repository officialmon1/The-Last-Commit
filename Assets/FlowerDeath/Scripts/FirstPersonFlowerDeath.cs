using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 第一人称花瓣死亡演示：按空格死亡 → 双手抬到眼前 → 看着自己的手化作花瓣 →
/// 消散从手沿手臂蔓延到全身，视线低下去看躯干和腿消散 → 膝盖发软镜头下沉 → 黑屏。
///
/// 场景、地面、双手和身体都在运行时用基本几何体拼出来（没有外部模型资源）。
/// 换成真正的带骨骼模型时：把模型放在 Player 下、把相机挂到头骨上、隐藏头部，
/// 然后让 FlowerDissolve.target = Player、dissolveOrigin = 双手中点即可，溶解和花瓣逻辑不用改。
/// 空格 = 死亡   R = 复活   鼠标 = 视角   Esc = 释放鼠标
/// </summary>
public class FirstPersonFlowerDeath : MonoBehaviour
{
    [Header("Timing")]
    public float raiseDuration = 1.0f;
    public float lookDuration = 1.3f;
    public float dissolveDuration = 6.0f;
    public float mouseSensitivity = 0.08f;

    const float EyeHeight = 1.62f;
    Vector3 _eyePos = new Vector3(0f, EyeHeight, 0.11f);   // 眼睛在头部前侧，低头才看得到胸口和腿

    Camera _cam;
    FirstPersonController _fpc;   // 场景里有移动模块时复用它的 Player 和相机
    Transform _player, _hands, _handsCenter;
    Transform[] _shoulders = new Transform[2], _elbows = new Transform[2], _upperArms = new Transform[2];
    FlowerDissolve _dissolve;
    string _status;
    float _yaw, _pitch, _fade;
    bool _dead;
    float _trembleWeight;
    Vector3 _lastBreathe;
    Quaternion _baseRot;

    // 手部姿势（相对相机）。手的局部坐标：手指朝 +Y，掌心朝 -Z（举起时朝向脸）。
    static readonly Vector3 RestPos = new Vector3(0f, -0.2f, 0.38f);
    static readonly Quaternion RestRot = Quaternion.Euler(55f, 0f, 0f);      // 手指朝前下方，只在画面底部露出一点
    static readonly Vector3 RaisedPos = new Vector3(0f, -0.24f, 0.44f);
    static readonly Quaternion RaisedRot = Quaternion.Euler(-14f, 0f, 0f);   // 掌心朝脸，指尖微微后仰

    void Start()
    {
        BuildWorld();

        _fpc = FindAnyObjectByType<FirstPersonController>();
        if (_fpc != null)
        {
            _player = _fpc.transform;
            _cam = _fpc.Cam;
            _eyePos = _cam.transform.localPosition;
        }
        else
        {
            _cam = Camera.main;
            _player = new GameObject("Player").transform;
            _player.position = Vector3.zero;
            _cam.transform.SetParent(_player, false);
            _cam.transform.localPosition = _eyePos;
            Cursor.lockState = CursorLockMode.Locked;
        }
        _cam.nearClipPlane = 0.03f;
        _yaw = 0f; _pitch = 4f;

        var skin = Lit(new Color(0.93f, 0.76f, 0.66f), 0.42f);
        var cloth = Lit(new Color(0.3f, 0.38f, 0.66f), 0.12f);
        var pants = Lit(new Color(0.17f, 0.17f, 0.21f), 0.1f);
        _hands = BuildHands(_cam.transform, skin, cloth);
        _hands.localPosition = RestPos;
        _hands.localRotation = _baseRot = RestRot;
        _handsCenter = new GameObject("HandsCenter").transform;
        _handsCenter.SetParent(_hands, false);
        _handsCenter.localPosition = new Vector3(0f, 0.04f, 0f);
        BuildBody(_player, skin, cloth, pants);
        foreach (var c in _player.GetComponentsInChildren<Collider>())
            if (!(c is CharacterController)) Destroy(c);   // 花瓣不该撞到自己（保留移动用的 CharacterController）

        _dissolve = _player.gameObject.AddComponent<FlowerDissolve>();
        _dissolve.target = _player;
        _dissolve.dissolveOrigin = _handsCenter;      // 从手开始向全身蔓延
        _dissolve.duration = dissolveDuration;
        _dissolve.petalAmount = 1100;
        _dissolve.petalSize = new Vector2(0.026f, 0.036f);   // 第一人称离手很近，花瓣要比 Godot 版小得多
        _dissolve.noiseScale = 9f;
        _dissolve.heightWeight = 0.72f;
        _dissolve.edgeWidth = 0.03f;
        _dissolve.wind = new Vector3(0.9f, 0.3f, 0.6f);
        _dissolve.DissolveFinished += () => _status = "已消散。按 R 复活";

        _status = "K：死亡   R：复活";
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && !CodeEditorUI.IsOpen)
        {
            if (kb.kKey.wasPressedThisFrame && !_dead) StartCoroutine(Die());
            if (kb.rKey.wasPressedThisFrame) Revive();
        }

        if (_fpc != null)
        {
            // 移动模块负责视角；死亡演出期间由这里驱动它的俯仰角
            if (_dead) _fpc.Pitch = _pitch;
        }
        else
        {
            if (kb != null && kb.escapeKey.wasPressedThisFrame) Cursor.lockState = CursorLockMode.None;
            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) Cursor.lockState = CursorLockMode.Locked;
            if (!_dead && mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                var d = mouse.delta.ReadValue() * mouseSensitivity;
                _yaw += d.x;
                _pitch = Mathf.Clamp(_pitch - d.y, -80f, 80f);
            }
            _player.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _cam.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // 活着时轻微的呼吸摆动；临死时手在发抖
        float t = Time.time;
        var breathe = new Vector3(0f, Mathf.Sin(t * 1.4f) * 0.005f, 0f);
        var tremble = new Vector3(Mathf.Sin(t * 31f), Mathf.Sin(t * 27f + 1f), Mathf.Sin(t * 23f + 2f)) * 1.4f * _trembleWeight;
        _hands.localPosition += breathe - _lastBreathe;
        _lastBreathe = breathe;
        _hands.localRotation = _baseRot * Quaternion.Euler(tremble);
    }

    void LateUpdate()
    {
        // 上臂：永远从肩膀指向小臂的肘端，手怎么动都连着
        for (int i = 0; i < 2; i++)
        {
            var a = _shoulders[i].position;
            var b = _elbows[i].position;
            var ua = _upperArms[i];
            ua.position = (a + b) * 0.5f;
            ua.rotation = Quaternion.FromToRotation(Vector3.up, b - a);
            var s = ua.localScale; s.y = Vector3.Distance(a, b) * 0.5f; ua.localScale = s;
        }
    }

    IEnumerator Die()
    {
        _dead = true;
        _status = "……";
        if (_fpc != null) { _fpc.inputLocked = true; _pitch = _fpc.Pitch; }
        // 1) 抬手到眼前
        float t = 0f;
        var p0 = _hands.localPosition - _lastBreathe;
        var r0 = _baseRot;
        float pitch0 = _pitch;
        while (t < raiseDuration)
        {
            t += Time.deltaTime;
            float k = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / raiseDuration), 3f);   // ease-out
            _hands.localPosition = Vector3.Lerp(p0, RaisedPos, k) + _lastBreathe;
            _baseRot = Quaternion.Slerp(r0, RaisedRot, k);
            _pitch = Mathf.Lerp(pitch0, 12f, k);             // 视线落到手上
            _trembleWeight = k * 0.6f;
            yield return null;
        }
        // 2) 看着自己的手
        t = 0f;
        while (t < lookDuration)
        {
            t += Time.deltaTime;
            _trembleWeight = 0.6f + 0.6f * (t / lookDuration);
            yield return null;
        }
        // 3) 消散：手 → 手臂 → 身体。手没了以后低头看身体，最后膝盖发软镜头下沉、黑屏
        _dissolve.Play();
        _status = "消散中…";
        float D = _dissolve.duration;
        t = 0f;
        while (_dissolve.IsPlaying)
        {
            t += Time.deltaTime;
            float k = t / D;
            _trembleWeight = Mathf.Max(0f, 1.2f - k * 3f);
            _pitch = Mathf.Lerp(12f, 64f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.45f, k)));
            float sink = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f, 1f, k));
            _cam.transform.localPosition = _eyePos + new Vector3(0f, Mathf.Lerp(0f, -0.7f, sink), 0.05f * sink);
            _fade = Mathf.InverseLerp(0.78f, 1f, k);
            yield return null;
        }
        _trembleWeight = 0f;
        _fade = 1f;
    }

    void Revive()
    {
        StopAllCoroutines();
        _dissolve.ResetDissolve();
        _dead = false;
        _trembleWeight = 0f;
        _fade = 0f;
        _hands.localPosition = RestPos + _lastBreathe;
        _baseRot = RestRot;
        _cam.transform.localPosition = _eyePos;
        _pitch = 4f;
        if (_fpc != null) { _fpc.Pitch = _pitch; _fpc.inputLocked = false; }
        _status = "K：死亡   R：复活";
    }

    void OnGUI()
    {
        if (_fade > 0f)
        {
            GUI.color = new Color(0f, 0f, 0f, _fade);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }
        GUI.Label(new Rect(16, 12, 600, 30), _status, new GUIStyle(GUI.skin.label) { fontSize = 20, normal = { textColor = new Color(1, 1, 1, 0.9f) } });
    }

    // ------------------------------------------------------------------ 场景搭建

    static Material Lit(Color c, float smooth = 0.3f)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        m.SetColor("_BaseColor", c);
        m.SetFloat("_Smoothness", smooth);
        return m;
    }

    static GameObject Prim(PrimitiveType type, Material m, Vector3 pos, Vector3 scale, Transform parent = null, Vector3 rotDeg = default)
    {
        var go = GameObject.CreatePrimitive(type);
        go.GetComponent<Renderer>().sharedMaterial = m;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localRotation = Quaternion.Euler(rotDeg);
        go.transform.localScale = scale;
        return go;
    }

    void BuildWorld()
    {
        var ground = Lit(new Color(0.22f, 0.26f, 0.24f), 0.05f);
        Prim(PrimitiveType.Plane, ground, Vector3.zero, Vector3.one * 4f);   // 40m x 40m，自带 MeshCollider 让花瓣落地
        var stone = Lit(new Color(0.45f, 0.42f, 0.4f), 0.2f);
        var rng = new System.Random(7);
        for (int i = 0; i < 14; i++)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f, d = 3f + (float)rng.NextDouble() * 9f;
            float h = 0.8f + (float)rng.NextDouble() * 2.5f;
            Prim(PrimitiveType.Cube, stone, new Vector3(Mathf.Cos(a) * d, h * 0.5f, Mathf.Sin(a) * d), new Vector3(0.6f, h, 0.6f));
        }
    }

    // ------------------------------------------------------------------ 身体

    void BuildBody(Transform player, Material skin, Material cloth, Material pants)
    {
        var body = new GameObject("Body").transform;
        body.SetParent(player, false);
        // 躯干、腰、胸口（低头时能看到）
        Prim(PrimitiveType.Capsule, cloth, new Vector3(0f, 1.2f, 0f), new Vector3(0.36f, 0.3f, 0.22f), body);
        Prim(PrimitiveType.Sphere, cloth, new Vector3(0f, 1.42f, 0.0f), new Vector3(0.4f, 0.16f, 0.24f), body);   // 肩胸
        Prim(PrimitiveType.Capsule, pants, new Vector3(0f, 0.9f, 0f), new Vector3(0.34f, 0.12f, 0.22f), body);     // 胯
        var shoe = Lit(new Color(0.12f, 0.1f, 0.1f), 0.3f);
        foreach (float side in new[] { -1f, 1f })
        {
            Prim(PrimitiveType.Capsule, pants, new Vector3(side * 0.1f, 0.48f, 0.01f), new Vector3(0.15f, 0.44f, 0.15f), body);  // 腿
            Prim(PrimitiveType.Cube, shoe, new Vector3(side * 0.1f, 0.045f, 0.07f), new Vector3(0.11f, 0.08f, 0.27f), body);      // 鞋
            int i = side < 0 ? 0 : 1;
            _shoulders[i] = new GameObject("Shoulder").transform;
            _shoulders[i].SetParent(body, false);
            _shoulders[i].localPosition = new Vector3(side * 0.2f, 1.44f, 0f);
            Prim(PrimitiveType.Sphere, cloth, Vector3.zero, Vector3.one * 0.13f, _shoulders[i]);
            _upperArms[i] = Prim(PrimitiveType.Capsule, cloth, Vector3.zero, new Vector3(0.095f, 0.15f, 0.095f), body).transform;
        }
    }

    // ------------------------------------------------------------------ 手

    /// <summary>一双第一人称的手，挂在相机下。手的局部坐标：手指朝 +Y，掌心朝 -Z（举起时朝向相机）。</summary>
    Transform BuildHands(Transform cam, Material skin, Material sleeve)
    {
        var root = new GameObject("Hands").transform;
        root.SetParent(cam, false);
        foreach (float side in new[] { -1f, 1f })
        {
            var hand = new GameObject(side < 0 ? "LeftHand" : "RightHand").transform;
            hand.SetParent(root, false);
            hand.localPosition = new Vector3(side * 0.15f, 0f, 0f);
            hand.localRotation = Quaternion.Euler(0f, side * 8f, side * -16f);   // 两手微微向外张开、掌心略朝内

            // 手掌：药丸形 + 掌根鼓起
            Prim(PrimitiveType.Capsule, skin, new Vector3(0f, 0.0f, 0f), new Vector3(0.082f, 0.05f, 0.027f), hand);
            Prim(PrimitiveType.Sphere, skin, new Vector3(0f, -0.03f, -0.002f), new Vector3(0.078f, 0.055f, 0.032f), hand);
            Prim(PrimitiveType.Sphere, skin, new Vector3(0f, 0.04f, 0.004f), new Vector3(0.08f, 0.03f, 0.03f), hand);   // 指根关节隆起

            // 四指：三节指骨、逐节变细、自然弯向掌心（-Z）
            float[] baseX = { -0.031f, -0.0105f, 0.0095f, 0.029f };
            float[] len = { 0.07f, 0.084f, 0.079f, 0.061f };
            float[] spread = { -7f, -2f, 2.5f, 8f };
            float[] curl = { 16f, 13f, 15f, 22f };
            for (int f = 0; f < 4; f++)
                Finger(hand, new Vector3(baseX[f], 0.046f, 0f), Quaternion.Euler(0f, 0f, -spread[f]), len[f], 0.0085f, curl[f], skin, 3);

            // 拇指：从手掌外侧斜向外上方伸出，并向掌心方向对掌
            Finger(hand, new Vector3(side * 0.04f, -0.012f, -0.006f), Quaternion.Euler(-28f, 0f, side * -62f), 0.066f, 0.011f, 18f, skin, 2);

            // 手腕、袖口、小臂
            Prim(PrimitiveType.Capsule, skin, new Vector3(0f, -0.085f, 0.003f), new Vector3(0.062f, 0.032f, 0.042f), hand);
            Prim(PrimitiveType.Cylinder, sleeve, new Vector3(0f, -0.118f, 0.005f), new Vector3(0.092f, 0.013f, 0.072f), hand, new Vector3(-4f, 0f, 0f));
            var forearm = Prim(PrimitiveType.Capsule, sleeve, new Vector3(0f, -0.26f, 0.012f), new Vector3(0.084f, 0.155f, 0.08f), hand, new Vector3(-5f, 0f, 0f));
            int i = side < 0 ? 0 : 1;
            _elbows[i] = new GameObject("Elbow").transform;
            _elbows[i].SetParent(forearm.transform, false);
            _elbows[i].localPosition = new Vector3(0f, -0.9f, 0f);   // 小臂胶囊的下端（局部 -1 是端点，留一点重叠）
        }
        return root;
    }

    /// <summary>一根手指：segments 节胶囊 + 关节球，每节绕 X 轴向 -Z（掌心）弯 curlDeg。</summary>
    static void Finger(Transform hand, Vector3 basePos, Quaternion baseRot, float length, float radius, float curlDeg, Material skin, int segments)
    {
        float[] frac = segments == 3 ? new[] { 0.45f, 0.3f, 0.25f } : new[] { 0.55f, 0.45f };
        var cur = new GameObject("Finger").transform;
        cur.SetParent(hand, false);
        cur.localPosition = basePos;
        cur.localRotation = baseRot;
        float r = radius;
        for (int i = 0; i < segments; i++)
        {
            float segLen = length * frac[i];
            var seg = new GameObject("Seg" + i).transform;
            seg.SetParent(cur, false);
            seg.localRotation = Quaternion.Euler(-curlDeg * (i == 0 ? 0.6f : 1f), 0f, 0f);
            Prim(PrimitiveType.Capsule, skin, new Vector3(0f, segLen * 0.5f, 0f), new Vector3(r * 2f, segLen * 0.5f, r * 2f), seg);
            if (i < segments - 1)
                Prim(PrimitiveType.Sphere, skin, new Vector3(0f, segLen, 0f), Vector3.one * r * 2.15f, seg);   // 关节
            var next = new GameObject("Joint").transform;
            next.SetParent(seg, false);
            next.localPosition = new Vector3(0f, segLen, 0f);
            cur = next;
            r *= 0.88f;
        }
    }
}
