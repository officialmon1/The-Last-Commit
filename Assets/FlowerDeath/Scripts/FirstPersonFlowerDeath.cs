using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using Panda;

/// <summary>
/// First-person petal death when the 36-minute clock runs out: raise both hands -> watch them turn into petals ->
/// the dissolve spreads from the hands along the arms to the whole body, the view drops to watch torso and legs go -> knees buckle, camera sinks -> fade to black.
///
/// Scene, ground, hands and body are all built from primitives at runtime (no external model assets).
/// To swap in a real rigged model: parent it under Player, attach the camera to the head bone, hide the head,
/// then set FlowerDissolve.target = Player and dissolveOrigin = the midpoint between the hands; the dissolve/petal logic needs no change.
/// Once the player is gone the game-over screen (GameEnd) offers Start over, which reloads the scene with a fresh save.
/// </summary>
public class FirstPersonFlowerDeath : MonoBehaviour
{
    [Header("Timing")]
    public float raiseDuration = 1.0f;
    public float lookDuration = 1.3f;
    public float dissolveDuration = 6.0f;
    public float mouseSensitivity = 0.08f;

    const float EyeHeight = 1.62f;
    Vector3 _eyePos = new Vector3(0f, EyeHeight, 0.11f);   // eyes sit at the front of the head so the chest and legs are visible when looking down

    Camera _cam;
    FirstPersonController _fpc;   // when the scene has a movement controller, reuse its Player and camera
    Transform _player, _hands, _handsCenter;
    Transform[] _shoulders = new Transform[2], _elbows = new Transform[2], _upperArms = new Transform[2];
    FlowerDissolve _dissolve;
    float _yaw, _pitch, _fade;
    bool _dead;
    float _trembleWeight;
    Vector3 _lastBreathe;
    Quaternion _baseRot;

    // Hand poses (relative to the camera). Hand local space: fingers along +Y, palm facing -Z (towards the face when raised).
    static readonly Vector3 RestPos = new Vector3(0f, -0.2f, 0.38f);
    static readonly Quaternion RestRot = Quaternion.Euler(55f, 0f, 0f);      // fingers point forward-down, only peeking in at the bottom of the screen
    static readonly Vector3 RaisedPos = new Vector3(0f, -0.24f, 0.44f);
    static readonly Quaternion RaisedRot = Quaternion.Euler(-14f, 0f, 0f);   // palms facing the face, fingertips leaning back slightly

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

        var skin = Lit(new Color(0.90f, 0.72f, 0.62f), 0.3f);   // matte: shiny skin reads as plastic
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
            if (!(c is CharacterController)) Destroy(c);   // petals must not collide with the player (keep the CharacterController used for movement)

        _dissolve = _player.gameObject.AddComponent<FlowerDissolve>();
        _dissolve.target = _player;
        _dissolve.dissolveOrigin = _handsCenter;      // spread from the hands over the whole body
        _dissolve.duration = dissolveDuration;
        _dissolve.petalAmount = 1100;
        _dissolve.petalSize = new Vector2(0.026f, 0.036f);   // in first person the hands are very close, so petals must be much smaller than in the Godot version
        _dissolve.noiseScale = 9f;
        _dissolve.heightWeight = 0.72f;
        _dissolve.edgeWidth = 0.03f;
        _dissolve.wind = new Vector3(0.9f, 0.3f, 0.6f);
        _dissolve.DissolveFinished += () => { _fade = 0f; GameEnd.Ensure().ShowDeath(); };   // the end screen brings its own backdrop

        CodeEditorUI.TrackTimeUp += OnTrackTimeUp;   // 36 minutes used up -> die (the only way to die)
    }

    void OnDestroy() => CodeEditorUI.TrackTimeUp -= OnTrackTimeUp;

    void OnTrackTimeUp(string track)
    {
        if (_dead) return;
        if (CodeEditorUI.IsOpen) CodeEditorUI.Instance.Close();
        StartCoroutine(Die());
    }

    void Update()
    {
        var kb = Keyboard.current;
        if (_fpc != null)
        {
            // the movement controller owns the view; during the death sequence we drive its pitch from here
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

        // gentle breathing sway while alive; trembling hands when dying
        float t = Time.time;
        var breathe = new Vector3(0f, Mathf.Sin(t * 1.4f) * 0.005f, 0f);
        var tremble = new Vector3(Mathf.Sin(t * 31f), Mathf.Sin(t * 27f + 1f), Mathf.Sin(t * 23f + 2f)) * 1.4f * _trembleWeight;
        _hands.localPosition += breathe - _lastBreathe;
        _lastBreathe = breathe;
        _hands.localRotation = _baseRot * Quaternion.Euler(tremble);
    }

    void LateUpdate()
    {
        // upper arms: always point from the shoulder to the elbow end of the forearm, so they stay connected however the hands move
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
        if (_fpc != null) { _fpc.inputLocked = true; _pitch = _fpc.Pitch; }
        // 1) raise the hands in front of the eyes
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
            _pitch = Mathf.Lerp(pitch0, 12f, k);             // look down at the hands
            _trembleWeight = k * 0.6f;
            yield return null;
        }
        // 2) look at your own hands
        t = 0f;
        while (t < lookDuration)
        {
            t += Time.deltaTime;
            _trembleWeight = 0.6f + 0.6f * (t / lookDuration);
            yield return null;
        }
        // 3) dissolve: hands -> arms -> body. Once the hands are gone look down at the body, then knees buckle, camera sinks, fade to black
        _dissolve.Play();
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
        _trembleWeight = 0f;   // already faded to black; DissolveFinished has handed over to the game-over screen
    }

    void OnGUI()
    {
        if (_fade <= 0f) return;
        GUI.color = new Color(0f, 0f, 0f, _fade);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    // ------------------------------------------------------------------ Scene setup

    static Material Lit(Color c, float smooth = 0.3f)
    {
        var m = new Material(Resources.Load<Material>("BaseLit"));   // URP Lit material from Resources so the shader is not stripped from builds
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
        // Only a ground plane, and only when the scene has no ground of its own: under the Amber Ruins terrain the flat
        // plane at y = 0 pokes through every dip in the hills as a dark slab. The level pillars are the landmarks.
        var fpc = FindAnyObjectByType<FirstPersonController>();
        Vector3 probe = (fpc != null ? fpc.transform.position : transform.position) + Vector3.up * 50f;
        foreach (var hit in Physics.RaycastAll(probe, Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore))
            if (fpc == null || !hit.transform.IsChildOf(fpc.transform)) return;   // something to stand on already exists
        var ground = Lit(new Color(0.22f, 0.26f, 0.24f), 0.05f);
        Prim(PrimitiveType.Plane, ground, Vector3.zero, Vector3.one * 4f);   // 40m x 40m; its MeshCollider lets petals land on it
    }

    // ------------------------------------------------------------------ Body

    void BuildBody(Transform player, Material skin, Material cloth, Material pants)
    {
        var body = new GameObject("Body").transform;
        body.SetParent(player, false);
        // torso, waist, chest (visible when looking down)
        Prim(PrimitiveType.Capsule, cloth, new Vector3(0f, 1.2f, 0f), new Vector3(0.36f, 0.3f, 0.22f), body);
        Prim(PrimitiveType.Sphere, cloth, new Vector3(0f, 1.42f, 0.0f), new Vector3(0.4f, 0.16f, 0.24f), body);   // shoulders/chest
        Prim(PrimitiveType.Capsule, pants, new Vector3(0f, 0.9f, 0f), new Vector3(0.34f, 0.12f, 0.22f), body);     // hips
        var shoe = Lit(new Color(0.12f, 0.1f, 0.1f), 0.3f);
        foreach (float side in new[] { -1f, 1f })
        {
            Prim(PrimitiveType.Capsule, pants, new Vector3(side * 0.1f, 0.48f, 0.01f), new Vector3(0.15f, 0.44f, 0.15f), body);  // leg
            Prim(PrimitiveType.Cube, shoe, new Vector3(side * 0.1f, 0.045f, 0.07f), new Vector3(0.11f, 0.08f, 0.27f), body);      // shoe
            int i = side < 0 ? 0 : 1;
            _shoulders[i] = new GameObject("Shoulder").transform;
            _shoulders[i].SetParent(body, false);
            _shoulders[i].localPosition = new Vector3(side * 0.2f, 1.44f, 0f);
            Prim(PrimitiveType.Sphere, cloth, Vector3.zero, Vector3.one * 0.13f, _shoulders[i]);
            _upperArms[i] = Prim(PrimitiveType.Capsule, cloth, Vector3.zero, new Vector3(0.095f, 0.15f, 0.095f), body).transform;
        }
    }

    // ------------------------------------------------------------------ Hands

    /// <summary>
    /// A pair of first-person hands under the camera, built from primitives: a cupped palm with a dome on the back, thumb
    /// and heel pads, bony knuckles, fingers in a relaxed curl that grows towards the little finger, fingertip pads and
    /// nails. Hand local space: fingers along +Y, palm facing -Z (towards the camera when raised), so the back of the hand
    /// and the nails are on +Z. The thumb and the index finger sit on the inner side (towards the middle of the view).
    /// </summary>
    Transform BuildHands(Transform cam, Material skin, Material sleeve)
    {
        var pad = Lit(new Color(0.90f, 0.65f, 0.58f), 0.3f);     // joints, pads and fingertips: a little more blood in them
        var nail = Lit(new Color(0.96f, 0.86f, 0.80f), 0.65f);
        var root = new GameObject("Hands").transform;
        root.SetParent(cam, false);
        foreach (float side in new[] { -1f, 1f })
        {
            float inner = -side;   // +x for the left hand, -x for the right: where the thumb and the index finger are
            var hand = new GameObject(side < 0 ? "LeftHand" : "RightHand").transform;
            hand.SetParent(root, false);
            hand.localPosition = new Vector3(side * 0.15f, 0f, 0f);
            hand.localRotation = Quaternion.Euler(0f, side * 8f, side * -16f);   // hands spread slightly outward, palms turned a little inward

            // palm: a cupped slab, a dome on the back, the thumb muscle, the heel on the little-finger side
            Prim(PrimitiveType.Sphere, skin, new Vector3(0f, 0.004f, 0.003f), new Vector3(0.084f, 0.1f, 0.03f), hand);
            Prim(PrimitiveType.Sphere, skin, new Vector3(0f, 0.012f, 0.012f), new Vector3(0.078f, 0.086f, 0.022f), hand);
            Prim(PrimitiveType.Sphere, pad, new Vector3(inner * 0.027f, -0.018f, -0.009f), new Vector3(0.042f, 0.056f, 0.03f), hand);   // thenar pad
            Prim(PrimitiveType.Sphere, pad, new Vector3(inner * -0.028f, -0.02f, -0.006f), new Vector3(0.03f, 0.056f, 0.026f), hand);   // hypothenar pad
            Prim(PrimitiveType.Sphere, skin, new Vector3(0f, -0.036f, 0f), new Vector3(0.074f, 0.04f, 0.03f), hand);                   // heel

            // four fingers (index .. little): real proportions, a relaxed curl, a knuckle on the back of each root
            float[] baseX = { 0.031f, 0.0105f, -0.0095f, -0.029f };   // measured towards the inner side
            float[] len = { 0.072f, 0.081f, 0.075f, 0.058f };
            float[] radius = { 0.0088f, 0.009f, 0.0085f, 0.0074f };
            float[] spread = { 8f, 2.5f, -3f, -9f };                   // degrees away from the middle finger, positive = outward
            float[] curl = { 13f, 16f, 20f, 26f };
            for (int f = 0; f < 4; f++)
            {
                float x = inner * baseX[f];
                Finger(hand, new Vector3(x, 0.046f, 0f), Quaternion.Euler(0f, 0f, side * spread[f]), len[f], radius[f], curl[f], skin, pad, nail, 3);
                Prim(PrimitiveType.Sphere, pad, new Vector3(x, 0.046f, 0.01f), new Vector3(0.021f, 0.019f, 0.018f), hand);   // knuckle
            }

            // thumb: from the inner side of the palm, pointing inward and forward, opposing towards the palm
            Finger(hand, new Vector3(inner * 0.04f, -0.012f, -0.006f), Quaternion.Euler(-28f, 0f, side * 62f), 0.062f, 0.0115f, 18f, skin, pad, nail, 2);

            // wrist, cuff, forearm
            Prim(PrimitiveType.Capsule, skin, new Vector3(0f, -0.085f, 0.003f), new Vector3(0.064f, 0.032f, 0.038f), hand);
            Prim(PrimitiveType.Cylinder, sleeve, new Vector3(0f, -0.118f, 0.005f), new Vector3(0.092f, 0.013f, 0.072f), hand, new Vector3(-4f, 0f, 0f));
            var forearm = Prim(PrimitiveType.Capsule, sleeve, new Vector3(0f, -0.26f, 0.012f), new Vector3(0.084f, 0.155f, 0.08f), hand, new Vector3(-5f, 0f, 0f));
            int i = side < 0 ? 0 : 1;
            _elbows[i] = new GameObject("Elbow").transform;
            _elbows[i].SetParent(forearm.transform, false);
            _elbows[i].localPosition = new Vector3(0f, -0.9f, 0f);   // lower end of the forearm capsule (-1 is the tip in local space; keep a little overlap)
        }
        return root;
    }

    /// <summary>
    /// One finger: `segments` tapering capsules, a bony joint between them, a soft pad and a nail on the last one.
    /// Each segment bends curlDeg around X towards -Z (the palm); the root bends a little less.
    /// </summary>
    static void Finger(Transform hand, Vector3 basePos, Quaternion baseRot, float length, float radius, float curlDeg, Material skin, Material pad, Material nail, int segments)
    {
        float[] frac = segments == 3 ? new[] { 0.42f, 0.32f, 0.26f } : new[] { 0.55f, 0.45f };
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
                Prim(PrimitiveType.Sphere, pad, new Vector3(0f, segLen, 0f), Vector3.one * r * 2.25f, seg);   // joint
            else
            {
                Prim(PrimitiveType.Sphere, pad, new Vector3(0f, segLen - r * 0.3f, -r * 0.15f), new Vector3(r * 2.1f, r * 2.3f, r * 1.8f), seg);   // fingertip pad
                Prim(PrimitiveType.Sphere, nail, new Vector3(0f, segLen * 0.55f, r * 0.8f), new Vector3(r * 1.5f, r * 2.3f, r * 0.6f), seg);      // nail on the back
            }
            var next = new GameObject("Joint").transform;
            next.SetParent(seg, false);
            next.localPosition = new Vector3(0f, segLen, 0f);
            cur = next;
            r *= 0.9f;
        }
    }
}
