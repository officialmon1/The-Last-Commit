using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using Panda;

/// <summary>
/// The opening scene right after Begin: Bao Bao tells the player where they woke up and what the pillars, the clock
/// and the portal mean. It is shot by its own camera in a close-up of Bao Bao while she talks; on the last line the
/// camera pushes back into the player's eyes and hands over to the first-person controller.
/// Next = Space / Enter / E / click, Skip = Esc. Created at runtime by GameStart; nothing in the scene refers to it.
/// </summary>
[DefaultExecutionOrder(-50)]   // its LateUpdate places the camera before the speech bubble and the name tag read Camera.main
public class IntroBriefing : MonoBehaviour
{
    public static bool Playing { get; private set; }

    [Tooltip("How far in front of Bao Bao's face the close-up camera sits (metres).")] public float closeUpDistance = 1.3f;
    [Tooltip("Seconds for the camera to push back into the player's eyes at the end.")] public float handOverSeconds = 1.6f;

    static readonly string[] Script =
    {
        "Oh, you're awake. Good. I was starting to worry.",
        "I'm Bao Bao. Welcome to Knight Hacks, a world you were never meant to enter.",
        "You crossed over in your sleep. It happens to coders who dream a little too hard about the job they want.",
        "The bad news first: you have thirty-six minutes to find your way home. After that, the forest keeps you.",
        "The good news: you're a computer scientist. Deep in these ruins stand four broken pillars, and the ancient text carved into them is code.",
        "Finish the program on each pillar, run it, pass its trials, and the pillar is restored. Restore all four and the portal home wakes up.",
        "I can't write it for you. But Grand Bao Bao's book taught me enough to give hints. Click me at a pillar when you're stuck.",
        "I'll also be watching your face. When it says \"help\", I'll ask. All you have to do is say yes.",
        "That's the whole briefing. Two hands, a clear head and a panda. It's enough.",
        "The clock starts at the first pillar. Follow the path, restore the ruins, and go get that job.",
    };

    PandaCompanion _panda;
    FirstPersonController _player;
    Camera _playerCam, _cam;
    GameObject _hands;                // the first-person hands, hidden while the camera is away from the eyes
    int _line = -1;
    bool _ending;
    float _t;                         // seconds since the push into the eyes started
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
        var hands = _playerCam.transform.Find("Hands");
        if (hands != null) { _hands = hands.gameObject; _hands.SetActive(false); }   // they would hang in the air in front of the player
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
        if (!_ending)
        {
            CloseUp(out var pos, out var rot);
            _cam.transform.SetPositionAndRotation(pos, rot);
            return;
        }
        // the push into the player's eyes: the controller keeps the player camera's pose up to date even while it is disabled
        _t += Time.unscaledDeltaTime;
        float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / handOverSeconds));
        _cam.transform.SetPositionAndRotation(Vector3.Lerp(_fromPos, _playerCam.transform.position, k),
                                              Quaternion.Slerp(_fromRot, _playerCam.transform.rotation, k));
        if (_t >= handOverSeconds) Finish();
    }

    /// <summary>Close-up of Bao Bao: in front of her face, a little below the top of the head, looking at her eyes.</summary>
    void CloseUp(out Vector3 pos, out Quaternion rot)
    {
        Vector3 head = _panda.HeadTop;
        Vector3 toPlayer = _player.transform.position - head; toPlayer.y = 0f;
        toPlayer = toPlayer.sqrMagnitude > 0.001f ? toPlayer.normalized : -_player.transform.forward;
        pos = head + toPlayer * closeUpDistance - Vector3.up * 0.25f;
        rot = Quaternion.LookRotation(head - Vector3.up * 0.22f - pos, Vector3.up);
    }

    void Next()
    {
        if (_ending) return;
        _line++;
        if (_line >= Script.Length) { BeginHandOver(); return; }
        _panda.speechBubble.ShowChoice(Script[_line], _line == Script.Length - 1 ? "Let's go" : "Next", Next, "Skip intro", Skip);
    }

    void Skip()
    {
        if (!_ending) BeginHandOver();
    }

    void BeginHandOver()
    {
        _ending = true;
        _t = 0f;
        _fromPos = _cam.transform.position;
        _fromRot = _cam.transform.rotation;
        _panda.speechBubble.ShowText(Script[Mathf.Clamp(_line, 0, Script.Length - 1)]);   // the buttons go away while the camera moves
    }

    void Finish()
    {
        Playing = false;
        if (_hands != null) _hands.SetActive(true);
        if (_playerCam != null) _playerCam.enabled = true;
        if (_cam != null) Destroy(_cam.gameObject);
        if (_player != null) _player.SetInputLocked(false);   // locks the cursor again: straight into first-person look
        if (_panda != null) _panda.BeginSmallTalk();
        Destroy(gameObject);
    }
}
