using System;
using UnityEngine;
using UnityEngine.UI;

namespace Panda
{
    /// <summary>
    /// Screen-space bubble showing what the panda is "saying", with up to two reply buttons.
    /// Floats above <see cref="anchor"/>'s head with a little tail; when the panda is off screen it docks to a corner.
    /// </summary>
    public class SpeechBubble : MonoBehaviour
    {
        public Text bubbleText;
        public Button button1;
        public Text button1Label;
        public Button button2;
        public Text button2Label;

        [Header("Follow the panda")]
        [Tooltip("What the bubble floats above. PandaCompanion fills this in automatically.")]
        public Transform anchor;
        [Tooltip("Gap between the top of the panda and the bubble's tail (metres).")]
        public float headGap = 0.25f;
        [Tooltip("Keep the bubble this many pixels away from the screen edge.")]
        public float screenMargin = 16f;

        public const string IdleText = "Stuck on something? Click me for a hint.";

        RectTransform _rect;
        Canvas _canvas;
        Vector2 _dockPivot, _dockAnchorMin, _dockAnchorMax, _dockPos;
        RectTransform _tail;
        Collider _anchorCollider;

        void Awake()
        {
            _rect = (RectTransform)transform;
            _canvas = GetComponentInParent<Canvas>().rootCanvas;
            _dockPivot = _rect.pivot;
            _dockAnchorMin = _rect.anchorMin;
            _dockAnchorMax = _rect.anchorMax;
            _dockPos = _rect.anchoredPosition;
            BuildTail();
            ShowText(IdleText);
        }

        void BuildTail()
        {
            var go = new GameObject("Tail", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            go.transform.SetAsFirstSibling(); // behind the text and buttons
            var bg = GetComponent<Image>();
            go.GetComponent<Image>().color = bg != null ? bg.color : Color.white;
            go.GetComponent<Image>().raycastTarget = false;
            _tail = (RectTransform)go.transform;
            _tail.anchorMin = _tail.anchorMax = new Vector2(0.5f, 0f);
            _tail.sizeDelta = new Vector2(28f, 28f);
            _tail.anchoredPosition = Vector2.zero;
            _tail.localRotation = Quaternion.Euler(0f, 0f, 45f);
            go.SetActive(false);
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (anchor == null || cam == null) { Dock(); return; }

            // right edge we may use: the code editor covers the right side of the screen while it's open
            float maxX = CodeEditorUI.IsOpen ? Screen.width * 0.52f : Screen.width;

            if (_anchorCollider == null || _anchorCollider.transform != anchor) _anchorCollider = anchor.GetComponentInChildren<Collider>();
            Vector3 head = _anchorCollider != null
                ? new Vector3(_anchorCollider.bounds.center.x, _anchorCollider.bounds.max.y, _anchorCollider.bounds.center.z)
                : anchor.position + Vector3.up * 1.2f;
            Vector3 sp = cam.WorldToScreenPoint(head + Vector3.up * headGap);

            bool onScreen = sp.z > 0f && sp.x > 0f && sp.x < maxX && sp.y > 0f && sp.y < Screen.height;
            if (!onScreen) { Dock(); return; }

            float scale = _canvas.scaleFactor;
            Vector2 size = _rect.rect.size * scale;
            float tailH = 20f * scale; // half the diagonal of the rotated tail square

            _rect.anchorMin = _rect.anchorMax = Vector2.zero;
            _rect.pivot = new Vector2(0.5f, 0f);

            float x = Mathf.Clamp(sp.x, screenMargin + size.x / 2f, maxX - screenMargin - size.x / 2f);
            float y = Mathf.Clamp(sp.y + tailH, screenMargin, Screen.height - screenMargin - size.y);
            _rect.position = new Vector3(x, y, 0f);

            // the tail points down at the panda; slide it along the bottom edge if the bubble got clamped
            _tail.gameObject.SetActive(true);
            float tailX = Mathf.Clamp((sp.x - x) / scale, -_rect.rect.width / 2f + 20f, _rect.rect.width / 2f - 20f);
            _tail.anchoredPosition = new Vector2(tailX, 0f);
        }

        void Dock()
        {
            _tail.gameObject.SetActive(false);
            _rect.pivot = _dockPivot;
            _rect.anchorMin = _dockAnchorMin;
            _rect.anchorMax = _dockAnchorMax;
            _rect.anchoredPosition = _dockPos;

            if (CodeEditorUI.IsOpen)
            {
                // the editor sits over the bottom-right corner, so tuck in just left of it instead
                float scale = _canvas.scaleFactor;
                float maxX = Screen.width * 0.52f - screenMargin;
                _rect.anchorMin = _rect.anchorMax = Vector2.zero;
                _rect.pivot = new Vector2(1f, 0f);
                _rect.position = new Vector3(maxX, screenMargin + 30f * scale, 0f);
            }
        }

        public void ShowText(string text)
        {
            bubbleText.text = text;
            HideButtons();
        }

        public void ShowOffer(Offer offer, Action onPrimary, Action onSecondary)
        {
            bubbleText.text = offer.text;
            bool isHelp = offer.kind == "help";
            SetButton(button1, button1Label, isHelp ? "Yes, a hint please" : "Yes!", onPrimary);
            SetButton(button2, button2Label, isHelp ? "No thanks" : "Not really", onSecondary);
        }

        public void Clear()
        {
            ShowText(IdleText);
        }

        void SetButton(Button button, Text label, string text, Action onClick)
        {
            label.text = text;
            button.gameObject.SetActive(true);
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => onClick?.Invoke());
        }

        void HideButtons()
        {
            button1.gameObject.SetActive(false);
            button2.gameObject.SetActive(false);
            button1.onClick.RemoveAllListeners();
            button2.onClick.RemoveAllListeners();
        }
    }
}
