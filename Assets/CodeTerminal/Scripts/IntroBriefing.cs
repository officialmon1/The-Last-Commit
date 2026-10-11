using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using Panda;

/// <summary>
/// The opening scene right after Begin: Bao Bao tells the player where they woke up and what the pillars, the clock
/// and the portal mean. It is shot by its own camera: a close-up of Bao Bao while she talks, then Bao Bao's point of
/// view looking at the player's body (which gets a temporary head for the occasion), and finally the camera flies into
/// the player's eyes and hands over to the first-person controller. Next = Space / Enter / E / click, Skip = Esc.
/// Created at runtime by GameStart; nothing in the scene refers to it.
/// </summary>
[DefaultExecutionOrder(-50)]   // its LateUpdate places the camera before the speech bubble and the name tag read Camera.main
public class IntroBriefing : MonoBehaviour
{
    public static bool Playing { get; private set; }

    [Tooltip("How far in front of Bao Bao's face the close-up camera sits (metres).")] public float closeUpDistance = 1.3f;
    [Tooltip("Seconds for the camera to glide between the two shots.")] public float cutSeconds = 0.9f;
    [Tooltip("Seconds for the camera to fly into the player's eyes at the end.")] public float handOverSeconds = 1.4f;

    // What Bao Bao says, and whether the camera looks at the player (from her eyes) or at her for that line.
    static readonly (string text, bool atPlayer)[] Script =
    {
        ("Oh, you're awake. Good. I was starting to worry.", false),
        ("I'm Bao Bao. Welcome to Knight Hacks, a world you were never meant to enter.", false),
        ("You crossed over in your sleep. It happens to coders who dream a little too hard about the job they want.", false),
        ("The bad news first: you have thirty-six minutes to find your way home. After that, the forest keeps you.", false),
        ("The good news: you're a computer scientist. Deep in these ruins stand four broken pillars, and the ancient text carved into them is code.", false),
        ("Finish the program on each pillar, run it, pass its trials, and the pillar is restored. Restore all four and the portal home wakes up.", false),
        ("I can't write it for you. But Grand Bao Bao's book taught me enough to give hints. Click me at a pillar when you're stuck.", false),
        ("I'll also be watching your face. When it says \"help\", I'll ask. All you have to do is say yes.", false),
        ("Now take a look at yourself. Still in one piece, mostly. That's the body you'll be walking home in.", true),
        ("The clock starts at the first pillar. Follow the path, restore the ruins, and go get that job.", true),
    };

    PandaCompanion _panda;
    FirstPersonController _player;
    Camera _playerCam, _cam;
    GameObject _head;
    int _line = -1;
    bool _atPlayer, _ending;
    float _t;                         // seconds since the current camera move started
    Vector3 _fromPos;
    Quaternion _fromRot;

    public static void Play()
    {
        if (Playing) return;
        new GameObject("Intro briefing").AddComponent<IntroBriefing>();
    }

    void Awake() => Playing = true;

    void Start()
    {
        _panda = PandaCompanion.Instance;
        _player = FindAnyObjectByType<FirstPersonController>();
        _playerCam = _player != null ? _player.Cam : null;
        if (_panda == null || _panda.speechBubble == null || _player == null || _playerCam == null) { Finish(); return; }

        _player.SetInputLocked(true);   // no walking or looking, and the cursor is free for the Next / Skip buttons
        _cam = new GameObject("Intro camera").AddComponent<Camera>();
        _cam.CopyFrom(_playerCam);
        _cam.tag = "MainCamera";        // Camera.main (speech bubble, name tag) must follow this camera while it plays
        _playerCam.enabled = false;
        var src = _playerCam.GetUniversalAdditionalCameraData();
        var dst = _cam.GetUniversalAdditionalCameraData();
        dst.renderPostProcessing = src.renderPostProcessing;
        dst.antialiasing = src.antialiasing;
        dst.volumeLayerMask = src.volumeLayerMask;
        dst.renderShadows = src.renderShadows;
        BuildHead();
        PandaPose(out _fromPos, out _fromRot);
        _cam.transform.SetPositionAndRotation(_fromPos, _fromRot);
        _t = cutSeconds;
        Next();
    }

    void Update()
    {
        if (_cam == null || _ending) return;
        var kb = Keyboard.current;
        if (kb != null)
        {
            if (kb.escapeKey.wasPressedThisFrame) { Skip(); return; }
            if (kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.eKey.wasPressedThisFrame) { Next(); return; }
        }
        var mouse = Mouse.current;
        bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();   // the bubble's buttons advance by themselves
        if (mouse != null && mouse.leftButton.wasPressedThisFrame && !overUi) Next();
    }

    void LateUpdate()
    {
        if (_cam == null) return;
        _t += Time.unscaledDeltaTime;
        float duration = _ending ? handOverSeconds : cutSeconds;
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / duration));
        Vector3 pos; Quaternion rot;
        if (_ending) { pos = _playerCam.transform.position; rot = _playerCam.transform.rotation; }   // the controller keeps this pose up to date
        else if (_atPlayer) PlayerPose(out pos, out rot);
        else PandaPose(out pos, out rot);
        _cam.transform.SetPositionAndRotation(Vector3.Lerp(_fromPos, pos, k), Quaternion.Slerp(_fromRot, rot, k));
        if (!_ending) return;
        if (_head != null && k > 0.55f) _head.SetActive(false);   // the camera is about to pass through it
        if (_t >= handOverSeconds) Finish();
    }

    // ------------------------------------------------------------------ shots

    Vector3 TowardPlayer(Vector3 from)
    {
        Vector3 d = _player.transform.position - from; d.y = 0f;
        return d.sqrMagnitude > 0.001f ? d.normalized : -_player.transform.forward;
    }

    /// <summary>Close-up of Bao Bao: in front of her face, a little below the top of the head, looking at her eyes.</summary>
    void PandaPose(out Vector3 pos, out Quaternion rot)
    {
        Vector3 head = _panda.HeadTop;
        pos = head + TowardPlayer(head) * closeUpDistance - Vector3.up * 0.25f;
        rot = Quaternion.LookRotation(head - Vector3.up * 0.22f - pos, Vector3.up);
    }

    /// <summary>Bao Bao's point of view: from her eyes toward the player's chest, so the whole body is in frame.</summary>
    void PlayerPose(out Vector3 pos, out Quaternion rot)
    {
        Vector3 head = _panda.HeadTop;
        pos = head - Vector3.up * 0.18f + TowardPlayer(head) * 0.12f;
        rot = Quaternion.LookRotation(_player.transform.position + Vector3.up * 1.25f - pos, Vector3.up);
    }

    // ------------------------------------------------------------------ flow

    void Next()
    {
        if (_ending) return;
        _line++;
        if (_line >= Script.Length) { BeginHandOver(); return; }
        var (text, atPlayer) = Script[_line];
        if (atPlayer != _atPlayer) { StartMove(); _atPlayer = atPlayer; }
        _panda.speechBubble.ShowChoice(text, _line == Script.Length - 1 ? "Let's go" : "Next", Next, "Skip intro", Skip);
    }

    void Skip()
    {
        if (!_ending) BeginHandOver();
    }

    void StartMove()
    {
        _fromPos = _cam.transform.position;
        _fromRot = _cam.transform.rotation;
        _t = 0f;
    }

    void BeginHandOver()
    {
        _ending = true;
        StartMove();
        int shown = Mathf.Clamp(_line, 0, Script.Length - 1);
        _panda.speechBubble.ShowText(Script[shown].text);   // the buttons go away while the camera flies in
    }

    void Finish()
    {
        Playing = false;
        if (_head != null) Destroy(_head);
        if (_playerCam != null) _playerCam.enabled = true;
        if (_cam != null) Destroy(_cam.gameObject);
        if (_player != null) _player.SetInputLocked(false);   // locks the cursor again: straight into first-person look
        if (_panda != null) _panda.BeginSmallTalk();
        Destroy(gameObject);
    }

    // ------------------------------------------------------------------ the player's head (first person has none)

    /// <summary>
    /// The player's body is built from primitives (FirstPersonFlowerDeath) and has no head, because the camera sits where
    /// the head would be. For the shot from Bao Bao's eyes a head and hair are added for the duration of the intro.
    /// A rigged character model would replace this and the primitive body together.
    /// </summary>
    void BuildHead()
    {
        var baseMat = Resources.Load<Material>("BaseLit");
        _head = new GameObject("Intro head");
        _head.transform.SetParent(_player.transform, false);
        Part(new Vector3(0f, 1.6f, 0f), new Vector3(0.22f, 0.26f, 0.24f), new Color(0.93f, 0.76f, 0.66f), baseMat);     // head
        Part(new Vector3(0f, 1.69f, -0.03f), new Vector3(0.235f, 0.2f, 0.25f), new Color(0.16f, 0.12f, 0.1f), baseMat);  // hair
    }

    void Part(Vector3 pos, Vector3 scale, Color color, Material baseMat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(go.GetComponent<Collider>());   // the CharacterController must not bump into its own head
        go.transform.SetParent(_head.transform, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        if (baseMat == null) return;
        var m = new Material(baseMat);
        m.SetColor("_BaseColor", color);
        go.GetComponent<Renderer>().sharedMaterial = m;
    }
}
