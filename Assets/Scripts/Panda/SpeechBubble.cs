using System;
using UnityEngine;
using UnityEngine.UI;

namespace Panda
{
    /// <summary>
    /// Screen-space bubble showing what the panda is "saying", with up to two reply buttons.
    /// Floats above <see cref="anchor"/>'s head while it's walking/following; once it's parked still
    /// beside the terminal (the code editor is open), it moves to the panda's right instead, near the
    /// top of its head, clear of both the panda itself and the code editor panel. Docks to a corner if off screen.
    /// </summary>
    public class SpeechBubble : MonoBehaviour
    {
        public Text bubbleText;
        public Button button1;
        public Text button1Label;
        public Button button2;
        public Text button2Label;

        [Header("Follow the panda")]
        [Tooltip("What the bubble floats near. PandaCompanion fills this in automatically.")]
        public Transform anchor;
        [Tooltip("Gap between the panda and the bubble's tail (metres).")]
        public float headGap = 0.25f;
        [Tooltip("Keep the bubble this many pixels away from the screen edge.")]
        public float screenMargin = 16f;
        [Tooltip("Extra pixels of breathing room beyond the head's own on-screen edge, while parked at the terminal.")]
        public float parkedSideGap = 40f;

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
            ApplyForestTheme();
            BuildTail();
            BuildVineAccents();
            ShowText(IdleText);
        }

        /// <summary>Forest-green parchment bubble with an amber border, cream text and warmer button colours.</summary>
        void ApplyForestTheme()
        {
            // the root's own Image becomes the amber frame; children always render on top of their parent in
            // Unity UI, so the inset green "fill" below has to be a child of it to appear layered correctly
            var root = GetComponent<Image>();
            if (root != null)
            {
                root.color = ForestTheme.Amber;
                root.sprite = ForestTheme.PanelSprite;
                root.type = Image.Type.Sliced;
            }

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(transform, false);
            fill.transform.SetAsFirstSibling();
            var fillImg = fill.GetComponent<Image>();
            fillImg.color = ForestTheme.PanelBg;
            fillImg.sprite = ForestTheme.PanelSprite;
            fillImg.type = Image.Type.Sliced;
            fillImg.raycastTarget = false;
            var frt = (RectTransform)fill.transform;
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = new Vector2(4f, 4f);
            frt.offsetMax = new Vector2(-4f, -4f);

            var font = ForestTheme.ElegantFont();
            Style(bubbleText, font, ForestTheme.TextCream);
            Style(button1Label, font, ForestTheme.TextCream);
            Style(button2Label, font, ForestTheme.TextCream);
            var img1 = button1 != null ? button1.GetComponent<Image>() : null;
            if (img1 != null) img1.color = ForestTheme.AccentGreen;
            var img2 = button2 != null ? button2.GetComponent<Image>() : null;
            if (img2 != null) img2.color = ForestTheme.AccentMuted;
        }

        static void Style(Text t, Font font, Color color)
        {
            if (t == null) return;
            if (font != null) t.font = font;
            t.color = color;
        }

        /// <summary>Two small amber "leaf" dots in the top corners -- a light vine-like flourish without needing art assets.</summary>
        void BuildVineAccents()
        {
            ForestTheme.AddLeafAccent(transform, new Vector2(0f, 1f), new Vector2(10f, -8f));
            ForestTheme.AddLeafAccent(transform, new Vector2(1f, 1f), new Vector2(-10f, -8f));
        }

        void BuildTail()
        {
            var go = new GameObject("Tail", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            go.transform.SetAsFirstSibling(); // behind the text and buttons
            go.GetComponent<Image>().color = ForestTheme.PanelBg; // matches the green fill, not the amber frame
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

            // While walking/following, the bubble floats above the panda's head as usual. Once it's parked
            // still beside the terminal, it moves to the panda's right instead, near the top of its head --
            // clear of the panda's own model and (via maxX above) clear of the code editor panel too.
            bool parked = CodeEditorUI.IsOpen;

            if (_anchorCollider == null || _anchorCollider.transform != anchor) _anchorCollider = anchor.GetComponentInChildren<Collider>();
            Vector3 head = _anchorCollider != null
                ? new Vector3(_anchorCollider.bounds.center.x, _anchorCollider.bounds.max.y, _anchorCollider.bounds.center.z)
                : anchor.position + Vector3.up * 1.2f;
            Vector3 sp = cam.WorldToScreenPoint(parked ? head : head + Vector3.up * headGap);

            bool onScreen = sp.z > 0f && sp.x > 0f && sp.x < maxX && sp.y > 0f && sp.y < Screen.height;
            if (!onScreen) { Dock(); return; }

            float scale = _canvas.scaleFactor;
            Vector2 size = _rect.rect.size * scale;
            float tailH = 20f * scale; // half the diagonal of the rotated tail square

            _rect.anchorMin = _rect.anchorMax = Vector2.zero;
            _tail.gameObject.SetActive(true);

            if (parked)
            {
                // box sits to the right of the head, near the top, with real clearance so it never touches the
                // face; tail points left, back at the panda. The gap is measured from the head's own on-screen
                // edge (not its center), so it stays clear of the face regardless of how close/zoomed-in the
                // panda appears -- a fixed pixel gap from the center alone would get swallowed up close.
                float headHalfWidthPx = 0f;
                if (_anchorCollider != null)
                {
                    Vector3 edge = new Vector3(_anchorCollider.bounds.max.x, head.y, head.z);
                    Vector3 spEdge = cam.WorldToScreenPoint(edge);
                    headHalfWidthPx = Mathf.Abs(spEdge.x - sp.x);
                }
                _rect.pivot = new Vector2(0f, 1f);
                float x = Mathf.Clamp(sp.x + headHalfWidthPx + parkedSideGap * scale, screenMargin, maxX - screenMargin - size.x);
                float y = Mathf.Clamp(sp.y, screenMargin + size.y, Screen.height - screenMargin);
                _rect.position = new Vector3(x, y, 0f);

                _tail.anchorMin = _tail.anchorMax = new Vector2(0f, 1f);
                float tailY = Mathf.Clamp((sp.y - y) / scale, -_rect.rect.height + 20f, -20f);
                _tail.anchoredPosition = new Vector2(0f, tailY);
            }
            else
            {
                // box floats above the head, centered; tail points down at the panda
                _rect.pivot = new Vector2(0.5f, 0f);
                float x = Mathf.Clamp(sp.x, screenMargin + size.x / 2f, maxX - screenMargin - size.x / 2f);
                float y = Mathf.Clamp(sp.y + tailH, screenMargin, Screen.height - screenMargin - size.y);
                _rect.position = new Vector3(x, y, 0f);

                _tail.anchorMin = _tail.anchorMax = new Vector2(0.5f, 0f);
                float tailX = Mathf.Clamp((sp.x - x) / scale, -_rect.rect.width / 2f + 20f, _rect.rect.width / 2f - 20f);
                _tail.anchoredPosition = new Vector2(tailX, 0f);
            }
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
