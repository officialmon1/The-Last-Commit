using System.Collections.Generic;
using UnityEngine;

namespace Panda
{
    /// <summary>
    /// Lives on the 3D panda. Waits beside the player and, once they walk out of its circle, runs ahead
    /// to lead the way (or patrols side to side if there is no player); reacts to the backend's live face/hand read of the player (mirrors the
    /// HELP_* / PROGRESS offers from the web prototype), and asks the backend for a hint when clicked.
    /// Those reactions and hints only happen while a code terminal is open. Out in the forest BaoBao makes small talk
    /// instead (a fresh line every so often), congratulates the player when they leave a solved terminal, and warns them
    /// when they wander too close to the edge of the ground.
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
        [Tooltip("The panda waits still while the player is within this many metres of it (turning around or walking past it does nothing); " +
                 "once the player is further away it runs to the spot ahead of them and waits there again.")]
        public float waitRadius = 3f;
        public float followSpeed = 4.5f;
        public float turnSpeed = 6f;

        [Header("Model")]
        [Tooltip("Rescale the model so it is this tall (metres). 0 = keep the model's own size.")]
        public float targetHeight = 1.1f;

        [Header("Terminal wait spot (pinned on screen, bottom-left; may hover above the ground, never sinks below it)")]
        [Tooltip("Where across the screen (0 = left edge, 1 = right edge) the panda's feet sit while the code editor is open.")]
        public float editorWaitViewportX = 0.22f;
        [Tooltip("Where up the screen (0 = bottom edge, 1 = top edge) the panda's feet sit; 0.15 keeps the whole body in view.")]
        public float editorWaitViewportY = 0.15f;
        [Tooltip("How far in front of the camera (metres): closer = bigger on screen. Keep this closer than a pillar is ever stood next to, so the pillar can't hide the panda (CodeTerminal steps the player back to this distance).")]
        public float editorWaitDepth = 2.3f;

        [Header("Movement")]
        public float patrolDistance = 1.5f;
        public float patrolSpeed = 0.6f;
        public float bobHeight = 0.08f;
        public float bobSpeed = 1.4f;

        [Header("UI")]
        public SpeechBubble speechBubble;
        [Tooltip("Seconds between two small-talk lines while no terminal is open (a random value in this range).")]
        public Vector2 chatterInterval = new Vector2(20f, 35f);
        [Tooltip("Warn when the player is within this many metres of the edge of the ground (the 'TERRAIN |' mesh). 0 = never.")]
        public float edgeMargin = 12f;
        [Tooltip("Small name floating just above the head; empty = no tag.")] public string displayName = "BaoBao";
        [Tooltip("Height of the name tag's letters in metres.")] public float nameTagSize = 0.06f;
        TextMesh _nameTag;

        // The model's own mesh was authored lying flat (Z-up export); this constant local
        // correction stands it upright. Compose it with every look-direction rotation below,
        // since directly overwriting transform.rotation would otherwise discard it.
        static readonly Quaternion ModelUpFix = Quaternion.Euler(-90f, 0f, 0f);

        Vector3 _basePos;
        Vector3 _groundPos;
        float _feetOffset; // how far the pivot sits above the model's feet
        float _height = 1.1f; // model height after FitModel
        bool _walking;

        /// <summary>World point at the top of the panda's head without the walking bob: a steady anchor for the speech bubble.</summary>
        public Vector3 HeadTop => (followTarget != null ? _groundPos : transform.position - Vector3.up * _feetOffset) + Vector3.up * _height;
        Quaternion _idleFacing;   // where the panda turned to when it stopped (toward the player at that moment); it does not track the player afterwards
        long _currentOfferId = -1;
        long _handledId = 0;
        bool _hinting;
        bool _backendFailed;
        int _sessionVersion;
        float _holdBubbleUntil;   // a fresh hint stays in the bubble until this time before face reactions may replace it

        // Small talk while the terminal is closed. Hints and face reactions are for the terminal only.
        static readonly string[] SmallTalk =
        {
            "Knight Hacks is a strange place to wake up in. Nobody means to come here. Some of us just stayed.",
            "The four broken pillars are the way home. Restore them all and the portal wakes up.",
            "The ancient text on the pillars looks like a spell. It isn't. It's a program with pieces missing.",
            "Thirty-six minutes feels like plenty until you're staring at a hard one. Keep walking.",
            "Out here I just keep you company. At a pillar, press E, and I'll help with the code.",
            "I've read Grand Bao Bao's book cover to cover. The hints in it are good. The jokes are not.",
            "Your face tells me more than you think. When it says you're stuck, I'll offer a hint. Just say yes.",
            "Which pillar next? Whichever is closest. The order doesn't matter, the clock does.",
            "Everything in this forest was built by someone who never made it home. Don't be the next one.",
            "The portal is at the far end of the clearing, asleep for now. Four restored pillars wake it.",
            "Somewhere on the other side there's a job waiting for you. Let's make sure you're there for it.",
            "Every traveller who got out of here started the same way: with one pillar.",
        };
        static readonly string[] AfterLevel =
        {
            "That pillar is standing again! Catch your breath, then find the next one.",
            "Restored. The portal felt that, I promise. On to the next pillar.",
            "Clean work. One more piece of this world put back together.",
            "You solved it. Grand Bao Bao would have liked you. Next pillar whenever you're ready.",
        };
        const string AllClearedLine = "Every pillar stands again and the portal is open. Walk through it and go home. It was good travelling with you.";
        const string EdgeLine = "Careful, that's the edge of Knight Hacks. Past the mist there's nothing at all. Turn back toward the ruins.";

        bool _editorWasOpen, _allCleared, _quiet;
        int _passedAtOpen, _lastLine = -1;
        float _nextChatter, _edgeWarnedUntil;
        Bounds _ground;
        bool _hasGround;

        void Awake()
        {
            Instance = this;
        }

        void Start()
        {
            FitModel();
            CreateNameTag();
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
                Vector3 toPlayer = followTarget.position - _groundPos; toPlayer.y = 0f;
                if (toPlayer.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(toPlayer, Vector3.up) * ModelUpFix;
            }
            _idleFacing = transform.rotation;
            if (speechBubble != null && speechBubble.anchor == null) speechBubble.anchor = transform;
            if (PandaService.Instance != null)
            {
                PandaService.Instance.OnStatusUpdated += HandleStatus;
                PandaService.Instance.OnBackendReady += HandleBackendReady;
                PandaService.Instance.OnBackendFailed += HandleBackendFailed;
                if (!PandaService.Instance.BackendReady) speechBubble.ShowText("BaoBao is loading...");
            }
            CodeEditorUI.TrackCompleted += HandleTrackCompleted;
            CodeEditorUI.TrackTimeUp += HandleTrackTimeUp;
            _nextChatter = Time.unscaledTime + 4f;
            foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.name.StartsWith("TERRAIN |")) { _ground = r.bounds; _hasGround = true; break; }   // the walkable ground of the Amber Ruins scene
        }

        void OnDestroy()
        {
            CodeEditorUI.TrackCompleted -= HandleTrackCompleted;
            CodeEditorUI.TrackTimeUp -= HandleTrackTimeUp;
            if (_nameTag != null) Destroy(_nameTag.gameObject);
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
            if (IntroBriefing.Playing) return;   // Bao Bao is busy telling the story
            if (!CodeEditorUI.IsOpen) { SayIdle(Pick(SmallTalk)); return; }   // out in the forest: small talk, not "stuck?"
            if (!_hinting && (CodeEditorUI.Instance == null || CodeEditorUI.Instance.CurrentHints.Count == 0)) speechBubble.Clear();
        }

        void HandleTrackCompleted(string track) => _allCleared = true;
        void HandleTrackTimeUp(string track) => _quiet = true;   // the petals take over from here

        /// <summary>
        /// Everything BaoBao says while no terminal is open: a line when the player comes out of a terminal
        /// (praise if that level is now solved), small talk every chatterInterval seconds, and the edge warning.
        /// </summary>
        void Chatter()
        {
            if (speechBubble == null || _quiet || GameEnd.IsShown || IntroBriefing.Playing) return;
            if (GameStart.Instance != null && !GameStart.Started) return;   // the bubble is hidden behind the start menu anyway
            bool open = CodeEditorUI.IsOpen;
            var editor = CodeEditorUI.Instance;
            if (open != _editorWasOpen)
            {
                _editorWasOpen = open;
                if (open) { _passedAtOpen = -1; return; }
                if (_allCleared) SayIdle(AllClearedLine);
                else if (editor != null && _passedAtOpen >= 0 && editor.PassedCount > _passedAtOpen) SayIdle(Pick(AfterLevel));
                else SayIdle(Pick(SmallTalk));
                return;
            }
            if (open)
            {
                // inside the terminal the bubble belongs to the hints and the face reactions; the solved-count baseline is taken
                // once the level is really showing (after the Continue / New game choice), before anything can be solved
                if (_passedAtOpen < 0 && editor != null && !string.IsNullOrEmpty(editor.CurrentProblemText)) _passedAtOpen = editor.PassedCount;
                return;
            }
            if (_hasGround && edgeMargin > 0f && followTarget != null)
            {
                // near the edge = within edgeMargin of the ground's x/z extents (its y extent is only the hills, so a shrunken Bounds.Contains is no use)
                Vector3 p = followTarget.position - _ground.center;
                if (Mathf.Abs(p.x) > _ground.extents.x - edgeMargin || Mathf.Abs(p.z) > _ground.extents.z - edgeMargin)
                {
                    if (Time.unscaledTime >= _edgeWarnedUntil) { SayIdle(EdgeLine); _edgeWarnedUntil = Time.unscaledTime + 12f; }
                    return;
                }
            }
            if (Time.unscaledTime >= _nextChatter) SayIdle(Pick(SmallTalk));
        }

        /// <summary>The intro briefing hands the bubble back with a first small-talk line.</summary>
        public void BeginSmallTalk() => SayIdle(Pick(SmallTalk));

        void SayIdle(string text)
        {
            speechBubble.ShowText(text);
            _nextChatter = Time.unscaledTime + Random.Range(chatterInterval.x, chatterInterval.y);
        }

        string Pick(string[] lines)
        {
            if (lines.Length < 2) return lines[0];
            int i;
            do i = Random.Range(0, lines.Length); while (i == _lastLine);   // never the same line twice in a row
            _lastLine = i;
            return lines[i];
        }

        void HandleBackendFailed(string error)
        {
            _backendFailed = true;
            speechBubble.ShowText("BaoBao couldn't connect. Reopen the game to try again.");
        }

        void LateUpdate()
        {
            // after the bob/walk of Update: sit just above the head and face the camera (the bubble starts headGap higher)
            if (_nameTag == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            _nameTag.transform.position = HeadTop + Vector3.up * nameTagSize * 1.5f;
            var d = _nameTag.transform.position - cam.transform.position;
            if (d.sqrMagnitude > 0.001f) _nameTag.transform.rotation = Quaternion.LookRotation(d);   // a TextMesh reads correctly when its forward points away from the viewer
        }

        void CreateNameTag()
        {
            if (string.IsNullOrWhiteSpace(displayName)) return;
            var go = new GameObject("NameTag", typeof(TextMesh));   // a root object: it must not inherit the model's fitted scale or Z-up fix
            _nameTag = go.GetComponent<TextMesh>();
            _nameTag.text = displayName;
            _nameTag.font = ForestTheme.ElegantFont() ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _nameTag.fontSize = 64;
            _nameTag.characterSize = nameTagSize * 10f / 64f;   // TextMesh letter height ~= fontSize * characterSize / 10
            _nameTag.anchor = TextAnchor.LowerCenter;
            _nameTag.alignment = TextAlignment.Center;
            _nameTag.color = ForestTheme.TextCream;
            go.GetComponent<MeshRenderer>().sharedMaterial = _nameTag.font.material;
        }

        void Update()
        {
            Chatter();
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
        /// Where the panda waits while the code editor is open: a fixed spot on screen (bottom-left, close to the camera),
        /// so it is always fully in view. It may hover above the ground for that; it is only lifted, never lowered, when
        /// the projected point would be under the floor (looking down), so it cannot sink into the ground.
        /// </summary>
        Vector3 EditorWaitSpot()
        {
            var cam = Camera.main;
            if (cam == null) return FollowSpot();
            Vector3 spot = cam.ViewportToWorldPoint(new Vector3(editorWaitViewportX, editorWaitViewportY, editorWaitDepth));
            float ground = GroundSnap(spot).y;
            if (spot.y < ground) spot.y = ground;   // may hover above the ground, but never sink into it when the player looks down
            return spot;
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

            // Wait quietly while the player is inside the circle: turning around or walking right past the panda
            // must not move it. Only once they are further away does it run to the spot ahead of them and park again.
            Vector3 toPlayer = followTarget.position - _groundPos; toPlayer.y = 0f;
            if (!_walking)
            {
                if (toPlayer.magnitude < waitRadius)
                {
                    transform.position = _groundPos + Vector3.up * _feetOffset;
                    transform.rotation = Quaternion.Slerp(transform.rotation, _idleFacing, Time.deltaTime * turnSpeed);
                    return;
                }
                _walking = true;
            }

            Vector3 spot = FollowSpot();
            Vector3 flat = spot - _groundPos; flat.y = 0f;
            float dist = flat.magnitude;
            if (dist < 0.1f)
            {
                // arrived ahead of the player: turn to face them once, then hold still until they wander off again
                _walking = false;
                _idleFacing = toPlayer.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(toPlayer, Vector3.up) * ModelUpFix : transform.rotation;
            }
            else
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

            // face where we're walking
            if (_walking && flat.sqrMagnitude > 0.0001f)
            {
                var look = Quaternion.LookRotation(flat, Vector3.up) * ModelUpFix;
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
            _height = b.size.y;

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
            if (IntroBriefing.Playing) return;
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
                Say(service == null || _backendFailed ? "BaoBao couldn't connect. Reopen the game to try again." : "BaoBao is loading...");
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
