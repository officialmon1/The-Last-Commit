using System.Collections.Generic;
using UnityEngine;

namespace Panda
{
    /// <summary>
    /// Lives on the 3D panda. Patrols side to side, reacts to the backend's live face/hand
    /// read of the player (mirrors the HELP_* / PROGRESS offers from the web prototype),
    /// and asks the backend for a hint when clicked. Before hinting it judges the code quietly:
    /// a solution that already passes gets "go submit" instead of a hint. Hint history is kept
    /// per level by CodeEditorUI (shown in its hints panel, saved locally), up to CodeEditorUI.MaxHints.
    /// </summary>
    public class PandaCompanion : MonoBehaviour
    {
        public static PandaCompanion Instance { get; private set; }

        [Header("Movement")]
        public float patrolDistance = 1.5f;
        public float patrolSpeed = 0.6f;
        public float bobHeight = 0.08f;
        public float bobSpeed = 1.4f;

        [Header("UI")]
        public SpeechBubble speechBubble;

        Vector3 _basePos;
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
            _basePos = transform.position;
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
            float x = Mathf.Sin(Time.time * patrolSpeed) * patrolDistance;
            float y = Mathf.Abs(Mathf.Sin(Time.time * bobSpeed)) * bobHeight;
            transform.position = _basePos + new Vector3(x, y, 0f);

            // face the direction of travel
            float dx = Mathf.Cos(Time.time * patrolSpeed) * patrolSpeed;
            if (Mathf.Abs(dx) > 0.001f)
            {
                var target = transform.position + new Vector3(Mathf.Sign(dx), 0f, 0f);
                var look = Quaternion.LookRotation(target - transform.position, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, look, Time.deltaTime * 2f);
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
