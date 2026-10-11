using System.Collections.Generic;
using UnityEngine;

namespace Panda
{
    /// <summary>
    /// Lives on the 3D panda. Follows the player around (or patrols side to side if there is
    /// no player), reacts to the backend's live face/hand read of the player (mirrors the
    /// HELP_* / PROGRESS offers from the web prototype), and asks the backend for a hint when clicked.
    /// Before hinting it judges the code quietly: a solution that already passes gets "go submit"
    /// instead of a hint. Hint history is kept per level by CodeEditorUI (shown in its hints panel,
    /// saved locally), up to CodeEditorUI.MaxHints.
    /// </summary>
    public class PandaCompanion : MonoBehaviour
    {
        public static PandaCompanion Instance { get; private set; }

        [Header("Follow")]
        [Tooltip("Who to follow. Empty = the FirstPersonController in the scene.")]
        public Transform followTarget;
        [Tooltip("Where the panda stands relative to the player: x = right, z = forward. Kept fairly far forward " +
                 "so it stays inside the camera's view instead of drifting out to the side and off screen.")]
        public Vector3 followOffset = new Vector3(-1.1f, 0f, 2.3f);
        [Tooltip("Only start walking once the panda is this far from its spot (stops it jittering).")]
        public float followSlack = 0.6f;
        public float followSpeed = 4.5f;
        public float turnSpeed = 6f;

        [Header("Model")]
        [Tooltip("Rescale the model so it is this tall (metres). 0 = keep the model's own size.")]
        public float targetHeight = 1.1f;

        [Header("Terminal wait spot (pinned to a fixed spot on screen)")]
        [Tooltip("Where across the screen (0 = left edge, 1 = right edge) the panda's feet sit.")]
        public float editorWaitViewportX = 0.22f;
        [Tooltip("Where up the screen (0 = bottom edge, 1 = top edge) the panda's feet sit.")]
        public float editorWaitViewportY = 0.15f;
        [Tooltip("How far in front of the camera (metres) that spot sits. Keep this closer than terminals are " +
                 "ever stood next to, so the panda is never hidden behind one -- bring it closer (and it'll " +
                 "happily overlap the terminal) rather than push it back and risk it vanishing behind geometry.")]
        public float editorWaitDepth = 2.3f;

        [Header("Movement")]
        public float patrolDistance = 1.5f;
        public float patrolSpeed = 0.6f;
        public float bobHeight = 0.08f;
        public float bobSpeed = 1.4f;

        [Header("UI")]
        public SpeechBubble speechBubble;

        // The model's own mesh was authored lying flat (Z-up export); this constant local
        // correction stands it upright. Compose it with every look-direction rotation below,
        // since directly overwriting transform.rotation would otherwise discard it.
        static readonly Quaternion ModelUpFix = Quaternion.Euler(-90f, 0f, 0f);

        Vector3 _basePos;
        Vector3 _groundPos;
        float _feetOffset; // how far the pivot sits above the model's feet
        bool _walking;
        long _currentOfferId = -1;
        long _handledId = 0;
        bool _hinting;
        bool _backendFailed;
        int _sessionVersion;
        float _holdBubbleUntil;   // a fresh hint stays in the bubble until this time before face reactions may replace it

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            FitModel();
            _basePos = transform.position;
            _groundPos = transform.position;
            if (followTarget == null)
            {
                var player = FindAnyObjectByType<FirstPersonController>();
                if (player != null) followTarget = player.transform;
            }
            if (followTarget != null)
            {
                _groundPos = FollowSpot();
                transform.position = _groundPos;
            }
            if (speechBubble != null && speechBubble.anchor == null) speechBubble.anchor = transform;
            if (PandaService.Instance != null)
            {
                PandaService.Instance.OnStatusUpdated += HandleStatus;
                PandaService.Instance.OnBackendReady += HandleBackendReady;
                PandaService.Instance.OnBackendFailed += HandleBackendFailed;
                if (!PandaService.Instance.BackendReady) speechBubble.ShowText("Panda is loading...");
            }
        }

        void OnDestroy()
        {
            if (PandaService.Instance != null)
            {
                PandaService.Instance.OnStatusUpdated -= HandleStatus;
                PandaService.Instance.OnBackendReady -= HandleBackendReady;
                PandaService.Instance.OnBackendFailed -= HandleBackendFailed;
            }
        }

        void HandleBackendReady()
        {
            _backendFailed = false;
            if (!_hinting && (CodeEditorUI.Instance == null || CodeEditorUI.Instance.CurrentHints.Count == 0)) speechBubble.Clear();
        }

        void HandleBackendFailed(string error)
        {
            _backendFailed = true;
            speechBubble.ShowText("Panda couldn't connect. Reopen the game to try again.");
        }

        void Update()
        {
            if (followTarget != null) { Follow(); return; }

            float x = Mathf.Sin(Time.time * patrolSpeed) * patrolDistance;
            float y = Mathf.Abs(Mathf.Sin(Time.time * bobSpeed)) * bobHeight;
            transform.position = _basePos + new Vector3(x, y + _feetOffset, 0f);

            // face the direction of travel
            float dx = Mathf.Cos(Time.time * patrolSpeed) * patrolSpeed;
            if (Mathf.Abs(dx) > 0.001f)
            {
                var target = transform.position + new Vector3(Mathf.Sign(dx), 0f, 0f);
                var look = Quaternion.LookRotation(target - transform.position, Vector3.up) * ModelUpFix;
                transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 2f);
            }
        }

        /// <summary>The spot beside the player the panda wants to stand on, dropped onto the ground.</summary>
        Vector3 FollowSpot()
        {
            Vector3 fwd = followTarget.forward; fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 spot = followTarget.position + right * followOffset.x + fwd * followOffset.z;
            spot.y = followTarget.position.y + followOffset.y;
            return GroundSnap(spot);
        }

        /// <summary>
        /// Where the panda waits while the code editor is open. Projecting a fixed viewport point into world
        /// space (rather than offsetting from the camera's position along its own axes) is what actually makes
        /// this appear in the same place on screen every time: a pure position+direction offset still shifts on
        /// screen with the camera's pitch (looking down at the terminal vs. level), because the projection back
        /// onto the screen depends on the camera's current rotation, not just where the point sits in the world.
        /// Viewport coordinates compensate for that by construction. No ground raycast either -- that was
        /// picking up whatever happened to be nearby (the terminal's own collider, a wall) and is exactly what
        /// made this drift between different terminals.
        /// </summary>
        Vector3 EditorWaitSpot()
        {
            var cam = Camera.main;
            if (cam == null) return FollowSpot();
            return cam.ViewportToWorldPoint(new Vector3(editorWaitViewportX, editorWaitViewportY, editorWaitDepth));
        }

        /// <summary>Drops a world point onto whatever's below it (ignoring the player and the panda itself).</summary>
        Vector3 GroundSnap(Vector3 spot)
        {
            var hits = Physics.RaycastAll(spot + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var hit in hits)
            {
                if (hit.transform.IsChildOf(transform) || hit.transform.IsChildOf(followTarget)) continue;
                if (hit.point.y > best) best = hit.point.y;
            }
            if (!float.IsNegativeInfinity(best)) spot.y = best + followOffset.y;
            return spot;
        }

        void Follow()
        {
            // the player can't move while the terminal is open, so snap straight to its screen-anchored
            // wait spot instead of walking over: the panda should already be waiting there, ready for hints,
            // and stay there (every frame, not just once) so it can never drift back under the editor panel.
            if (CodeEditorUI.IsOpen)
            {
                _groundPos = EditorWaitSpot();
                _walking = false;
                transform.position = _groundPos + Vector3.up * _feetOffset;
                var cam = Camera.main;
                Vector3 toCam = (cam != null ? cam.transform.position : followTarget.position) - transform.position;
                toCam.y = 0f;
                if (toCam.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(toCam, Vector3.up) * ModelUpFix;
                return;
            }

            Vector3 spot = FollowSpot();
            Vector3 flat = spot - _groundPos; flat.y = 0f;
            float dist = flat.magnitude;

            // walk over once we've fallen behind, stop when we're close again
            if (dist > followSlack) _walking = true;
            else if (dist < 0.1f) _walking = false;

            if (_walking)
            {
                // speed up if the player sprinted far ahead, so we never get lost
                float speed = followSpeed * Mathf.Max(1f, dist / 2f);
                Vector3 step = Vector3.MoveTowards(new Vector3(_groundPos.x, 0f, _groundPos.z), new Vector3(spot.x, 0f, spot.z), speed * Time.deltaTime);
                _groundPos.x = step.x;
                _groundPos.z = step.z;
            }
            _groundPos.y = Mathf.Lerp(_groundPos.y, spot.y, Time.deltaTime * 10f);

            float bob = _walking ? Mathf.Abs(Mathf.Sin(Time.time * bobSpeed * 4f)) * bobHeight : 0f;
            transform.position = _groundPos + Vector3.up * (bob + _feetOffset);

            // face where we're walking, otherwise turn to look at the player
            Vector3 lookDir = _walking ? flat : followTarget.position - transform.position;
            lookDir.y = 0f;
            if (lookDir.sqrMagnitude > 0.0001f)
            {
                var look = Quaternion.LookRotation(lookDir, Vector3.up) * ModelUpFix;
                transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * turnSpeed);
            }
        }

        /// <summary>Scales the imported model to <see cref="targetHeight"/>, works out where its feet are and fits the click collider.</summary>
        void FitModel()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;
            Bounds b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);

            if (targetHeight > 0f && b.size.y > 0.0001f)
            {
                float k = targetHeight / b.size.y;
                transform.localScale *= k;
                b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
            }

            // some exports have the pivot in the middle of the body; lift the panda so its feet touch the ground
            _feetOffset = transform.position.y - b.min.y;

            var box = GetComponent<BoxCollider>();
            if (box != null)
            {
                box.isTrigger = true; // still clickable, but the player can't bump into it
                Vector3 s = transform.lossyScale;
                Vector3 size = b.size;
                box.size = new Vector3(size.x / s.x, size.y / s.y, size.z / s.z);
                box.center = transform.InverseTransformPoint(b.center);
            }
        }

        /// <summary>Called when a different problem is opened: drops any hint request in flight and clears the bubble (hint history itself lives in CodeEditorUI, per level).</summary>
        public void ResetHintSession()
        {
            _sessionVersion++;
            _hinting = false;
            MarkHandled();
            _currentOfferId = -1;
            if (speechBubble != null) speechBubble.Clear();
        }

        /// <summary>Called by ClickRouter when the player clicks the panda.</summary>
        public void OnClicked()
        {
            AskHint();
        }

        public void ShowCelebration(string text)
        {
            _sessionVersion++;
            _hinting = false;
            _currentOfferId = -1;
            speechBubble.ShowText(text);
        }

        void AskHint()
        {
            if (_hinting) return;
            var service = PandaService.Instance;
            var editor = CodeEditorUI.Instance;
            string problemText = editor != null ? editor.CurrentProblemText
                : ProblemBoard.Instance != null ? ProblemBoard.Instance.CurrentProblemText : "";
            if (string.IsNullOrEmpty(problemText))
            {
                speechBubble.ShowText("Open a code terminal first, then ask me for a hint.");
                return;
            }
            _hinting = true;
            int session = _sessionVersion;
            MarkHandled();
            speechBubble.ShowText("Hmm, let me take a look...");

            // Judge the code quietly first: a solution that already passes gets "go submit", not a hint
            if (editor != null) editor.CheckSolution((passed, summary) =>
            {
                if (this == null || session != _sessionVersion) return;
                if (passed) Say("Your code already passes the hidden test. Nothing left to fix, go hit Submit!");
                else RequestHint(service, editor, problemText, session);
            });
            else RequestHint(service, null, problemText, session);
        }

        void RequestHint(PandaService service, CodeEditorUI editor, string problemText, int session)
        {
            if (service == null || !service.BackendReady)
            {
                Say(service == null || _backendFailed ? "Panda couldn't connect. Reopen the game to try again." : "Panda is loading...");
                return;
            }
            int used = editor != null ? editor.CurrentHints.Count : 0;
            if (used >= CodeEditorUI.MaxHints)
            {
                Say($"That was my last hint for this level ({used}/{CodeEditorUI.MaxHints}). They are all listed on the left.");
                return;
            }
            service.SendEvent("accepted");
            int level = used + 1;
            var history = editor != null ? new List<string>(editor.CurrentHints) : new List<string>();
            service.RequestHint(problemText, editor != null ? editor.CurrentCode : "",
                editor != null ? editor.CurrentTestResults : "", level, history, resp =>
            {
                if (this == null || session != _sessionVersion) return;
                if (resp == null)
                {
                    Say("Can't reach the backend. Is panda-service running?");
                    return;
                }
                if (editor != null) editor.AddHint(resp.text);
                string suffix = level == CodeEditorUI.MaxHints ? " (max)" : " (click again for more)";
                Say(resp.text + "\n\nHint " + level + "/" + CodeEditorUI.MaxHints + suffix);
            }, editor != null ? editor.MinutesLeft : 36f);
        }

        // Ends the hint request and keeps the text in the bubble for a while: a face reaction must not wipe a fresh hint
        void Say(string text)
        {
            _hinting = false;
            _holdBubbleUntil = Time.unscaledTime + 20f;
            speechBubble.ShowText(text);
        }

        void MarkHandled()
        {
            if (_currentOfferId != -1) _handledId = System.Math.Max(_handledId, _currentOfferId);
            _currentOfferId = -1;
        }

        void HandleStatus(StatusResponse status)
        {
            if (_hinting || !CodeEditorUI.IsOpen || Time.unscaledTime < _holdBubbleUntil) return;

            if (status.offer != null)
            {
                if (status.offer.id > _handledId && status.offer.id != _currentOfferId)
                {
                    _currentOfferId = status.offer.id;
                    var offer = status.offer;
                    bool isHelp = offer.kind == "help";
                    speechBubble.ShowOffer(offer,
                        onPrimary: () =>
                        {
                            if (isHelp) AskHint();
                            else { MarkHandled(); PandaService.Instance.SendEvent("accepted"); speechBubble.ShowText(offer.yes_text); }
                        },
                        onSecondary: () =>
                        {
                            if (isHelp) { MarkHandled(); PandaService.Instance.SendEvent("dismissed"); speechBubble.ShowText("Okay. I'm here if you want me."); }
                            else AskHint();
                        });
                }
            }
            else if (_currentOfferId != -1)
            {
                MarkHandled();
                speechBubble.Clear();
            }
        }
    }
}
