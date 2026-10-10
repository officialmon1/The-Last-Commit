using System.Collections.Generic;
using UnityEngine;

namespace Panda
{
    /// <summary>
    /// Lives on the 3D panda. Patrols side to side, reacts to the backend's live face/hand
    /// read of the player (mirrors the HELP_* / PROGRESS offers from the web prototype),
    /// and asks the backend for a hint when clicked.
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
        int _level;
        readonly List<string> _history = new List<string>();
        long _currentOfferId = -1;
        long _handledId = 0;
        bool _hinting;
        bool _backendFailed;
        int _sessionVersion;

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
            if (!_hinting && _history.Count == 0) speechBubble.Clear();
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

        /// <summary>Called by a new problem block being opened: a new problem means a fresh hint session.</summary>
        public void ResetHintSession()
        {
            _sessionVersion++;
            _hinting = false;
            MarkHandled();
            _level = 0;
            _history.Clear();
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
            if (service == null || !service.BackendReady)
            {
                speechBubble.ShowText(service == null || _backendFailed
                    ? "Panda couldn't connect. Reopen the game to try again." : "Panda is loading...");
                return;
            }
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
            service.SendEvent("accepted");
            int level = Mathf.Min(_level + 1, 3);
            speechBubble.ShowText("Hmm, let me take a look...");

            service.RequestHint(problemText, editor != null ? editor.CurrentCode : "",
                editor != null ? editor.CurrentTestResults : "", level, _history, resp =>
            {
                if (this == null || session != _sessionVersion) return;
                _hinting = false;
                if (resp == null)
                {
                    speechBubble.ShowText("Can't reach the backend. Is panda-service running?");
                    return;
                }
                _level = level;
                _history.Add(resp.text);
                string suffix = _level == 3 ? " (max)" : " (click again for more)";
                speechBubble.ShowText(resp.text + "\n\nHint " + _level + "/3" + suffix);
            }, editor != null ? editor.MinutesLeft : 36f);
        }

        void MarkHandled()
        {
            if (_currentOfferId != -1) _handledId = System.Math.Max(_handledId, _currentOfferId);
            _currentOfferId = -1;
        }

        void HandleStatus(StatusResponse status)
        {
            if (_hinting || !CodeEditorUI.IsOpen) return;

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
