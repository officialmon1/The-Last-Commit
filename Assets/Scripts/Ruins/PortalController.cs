using System.Collections;
using UnityEngine;
using Panda;

/// <summary>
/// The ancient stone gateway in the Amber Ruins. Starts dormant (dark seal, no light).
/// When the four prerequisite problems are solved it repairs every <see cref="ruins"/> entry one after another,
/// then turns on: the amber seal and light fade in and the final problem (the CodeTerminal on this object) unlocks.
/// Solving the final problem opens it: the seal dissolves and the barrier stops blocking the way.
/// Walking through the open gateway ends the game (GameEnd: clear time, leaderboard, play again).
/// </summary>
public class PortalController : MonoBehaviour
{
    public enum State { Dormant, On, Open }

    [Tooltip("Track whose levels drive the portal (same as the CodeTerminals).")] public string track = "dijkstra";
    [Tooltip("Level of the final problem (the one opened at the portal).")] public int finalLevel = 5;

    [Header("Portal parts")]
    public Renderer seal;
    public Light portalLight;
    [Tooltip("Solid collider that blocks the gateway until the final problem is solved.")] public Collider barrier;

    [Header("Repair")]
    [Tooltip("Broken ruins repaired when the four problems are solved, in this order.")] public RuinRestorer[] ruins;
    [Tooltip("Seconds between one ruin rebuilding and the next.")] public float ruinStagger = 0.45f;
    public float turnOnDuration = 2.5f;

    [Header("Escape")]
    [Tooltip("Walking into the open gateway ends the game with the end screen (clear time, leaderboard, play again).")] public bool endGameOnEnter = true;
    [Tooltip("Extra metres around the barrier that still count as 'through the gate'.")] public float enterPadding = 0.6f;

    public State Current { get; private set; } = State.Dormant;

    Material _mat;
    Color _onColor, _onEmission;
    float _onIntensity;
    bool _animating, _escaped;
    Bounds _gate;   // the barrier's volume, captured while its collider is still enabled
    Transform _player;
    static readonly Color DormantColor = new Color(0.12f, 0.1f, 0.09f, 0.18f);

    void Start()
    {
        if (seal != null)
        {
            _mat = seal.material;   // per-instance copy: the shared material asset stays untouched
            _onColor = _mat.GetColor("_BaseColor");
            _onEmission = _mat.IsKeywordEnabled("_EMISSION") ? _mat.GetColor("_EmissionColor") : _onColor;
            _mat.EnableKeyword("_EMISSION");
        }
        if (portalLight != null) _onIntensity = portalLight.intensity;
        _gate = barrier != null ? barrier.bounds : new Bounds(transform.position + Vector3.up, new Vector3(3f, 3f, 1f));
        SetGlow(0f);
        CodeEditorUI.TrackCompleted += HandleTrackCompleted;
    }

    void OnDestroy() => CodeEditorUI.TrackCompleted -= HandleTrackCompleted;

    void Update()
    {
        var editor = CodeEditorUI.Instance;
        if (editor == null) return;
        int final = finalLevel - 1;
        if (Current == State.Dormant && editor.IsUnlocked(track, final)) TurnOn();
        if (Current != State.Open && editor.IsPassed(track, final)) Open();

        // a slow breathing pulse while the portal is awake
        if (Current == State.On && portalLight != null && !_animating)
            portalLight.intensity = _onIntensity * (1f + 0.15f * Mathf.Sin(Time.time * 2.2f));

        if (Current == State.Open && endGameOnEnter && !_escaped) CheckEscape(editor);
    }

    /// <summary>The player stepping into the open gateway = escaped: freeze the run and show the end screen.</summary>
    void CheckEscape(CodeEditorUI editor)
    {
        if (_player == null)
        {
            var fpc = FindAnyObjectByType<FirstPersonController>();
            if (fpc == null) return;
            _player = fpc.transform;
        }
        var gate = _gate;
        gate.Expand(enterPadding * 2f);
        if (!gate.Contains(_player.position + Vector3.up * 0.9f)) return;   // the controller's origin is at the feet
        _escaped = true;
        GameEnd.Ensure().Show(editor != null ? editor.ElapsedSeconds : 0f);
    }

    void HandleTrackCompleted(string completedTrack)
    {
        if (completedTrack == track) Open();
    }

    /// <summary>Repairs the ruins and wakes the portal. Called automatically once the four problems are solved.</summary>
    public void TurnOn()
    {
        if (Current != State.Dormant) return;
        Current = State.On;
        StartCoroutine(TurnOnRoutine());
    }

    IEnumerator TurnOnRoutine()
    {
        _animating = true;
        float delay = 0f;
        if (ruins != null)
            foreach (var ruin in ruins)
            {
                if (ruin == null) continue;
                ruin.Restore(delay);
                delay += ruinStagger;
            }
        if (PandaCompanion.Instance != null)
            PandaCompanion.Instance.ShowCelebration("Look! The ruins are whole again and the portal is waking up. The final problem waits at the portal.");

        yield return new WaitForSeconds(delay + 0.8f);
        for (float t = 0f; t < 1f; t += Time.deltaTime / turnOnDuration)
        {
            SetGlow(t * t);
            yield return null;
        }
        SetGlow(1f);
        _animating = false;
    }

    /// <summary>Opens the gateway: the seal fades away and the barrier no longer blocks. Called when the final problem is solved.</summary>
    public void Open()
    {
        if (Current == State.Open) return;
        if (Current == State.Dormant) TurnOn();   // e.g. a saved run that was already finished
        Current = State.Open;
        if (barrier != null) barrier.enabled = false;
        StartCoroutine(OpenRoutine());
        if (PandaCompanion.Instance != null)
            PandaCompanion.Instance.ShowCelebration("You did it! The portal is open. Walk through and escape!");
    }

    IEnumerator OpenRoutine()
    {
        while (_animating) yield return null;
        _animating = true;
        for (float t = 0f; t < 1f; t += Time.deltaTime / 1.5f)
        {
            if (_mat != null)
            {
                var c = _onColor; c.a = Mathf.Lerp(_onColor.a, 0.12f, t);
                _mat.SetColor("_BaseColor", c);
                _mat.SetColor("_EmissionColor", _onEmission * Mathf.Lerp(1f, 2.5f, t));
            }
            if (portalLight != null) portalLight.intensity = _onIntensity * Mathf.Lerp(1f, 1.8f, t);
            yield return null;
        }
        _animating = false;
    }

    /// <summary>0 = dormant (dark seal, light off), 1 = fully awake amber.</summary>
    void SetGlow(float k)
    {
        if (_mat != null)
        {
            _mat.SetColor("_BaseColor", Color.Lerp(DormantColor, _onColor, k));
            _mat.SetColor("_EmissionColor", _onEmission * k);
        }
        if (portalLight != null)
        {
            portalLight.intensity = _onIntensity * k;
            portalLight.enabled = k > 0.001f;
        }
    }
}
