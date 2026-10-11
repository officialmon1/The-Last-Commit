using System;
using UnityEngine;
using UnityEngine.UI;

namespace Panda
{
    /// <summary>
    /// Screen-space bubble showing what the panda is "saying", with up to two reply buttons, in the forest/amber
    /// theme (amber frame, green parchment fill, storybook font, leaf accents).
    /// Floats above <see cref="anchor"/>'s head while it's walking/following (smoothed, so the text does not shake);
    /// while the code editor is open the panda is parked in the bottom-left corner and the bubble is pinned to the
    /// top-right of the free area instead. Docks to a corner if the panda is off screen.
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

        public const string IdleText = "Stuck on something? Click me for a hint.";

        RectTransform _rect;
        Canvas _canvas;
        Vector2 _dockPivot, _dockAnchorMin, _dockAnchorMax, _dockPos;
        RectTransform _tail;
        Collider _anchorCollider;
        bool _attached;        // floating above the panda (true) or docked in the corner (false)
        float _switchTime;     // last time it switched between the two
        Vector2 _smooth;       // smoothed screen position of the head point

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

            if (CodeEditorUI.IsOpen)
            {
                // Coding: the panda is parked whole in the bottom-left corner, so the bubble sits still in the top-right of the
                // free area (just left of the editor panel, just below the hints panel). Pinned, so the text never moves.
                float scale = _canvas.scaleFactor;
                _tail.gameObject.SetActive(false);
                _rect.anchorMin = _rect.anchorMax = Vector2.zero;
                _rect.pivot = new Vector2(1f, 1f);
                _rect.position = new Vector3(Screen.width * 0.52f - screenMargin * scale, Screen.height * 0.66f - screenMargin * scale, 0f);
                _attached = false;
                return;
            }

            // Walking/following: float above the head. The anchor point ignores the walking bob and the screen position is
            // smoothed, otherwise the text shakes while the panda trots; attaching/docking has hysteresis so the bubble can't
            // flip back and forth when the panda hovers at the edge of the screen.
            var panda = PandaCompanion.Instance;
            Vector3 headTop;
            if (panda != null && anchor == panda.transform) headTop = panda.HeadTop;
            else
            {
                if (_anchorCollider == null || _anchorCollider.transform != anchor) _anchorCollider = anchor.GetComponentInChildren<Collider>();
                headTop = _anchorCollider != null
                    ? new Vector3(_anchorCollider.bounds.center.x, _anchorCollider.bounds.max.y, _anchorCollider.bounds.center.z)
                    : anchor.position + Vector3.up * 1.2f;
            }
            Vector3 sp = cam.WorldToScreenPoint(headTop + Vector3.up * headGap);

            float inset = _attached ? -60f : 60f;   // attached: let it drift a little past the edge before docking; docked: must be well inside to come back
            bool onScreen = sp.z > 0f && sp.x > inset && sp.x < Screen.width - inset && sp.y > inset && sp.y < Screen.height - inset;
            if (onScreen != _attached && Time.unscaledTime - _switchTime > 0.5f)
            {
                _attached = onScreen;
                _switchTime = Time.unscaledTime;
                _smooth = sp;
            }
            if (!_attached) { Dock(); return; }
            _smooth = Vector2.Lerp(_smooth, sp, 1f - Mathf.Exp(-Time.unscaledDeltaTime * 10f));

            float k = _canvas.scaleFactor;
            Vector2 size = _rect.rect.size * k;
            float tailH = 20f * k; // half the diagonal of the rotated tail square

            _rect.anchorMin = _rect.anchorMax = Vector2.zero;
            _rect.pivot = new Vector2(0.5f, 0f);
            float x = Mathf.Clamp(_smooth.x, screenMargin + size.x / 2f, Screen.width - screenMargin - size.x / 2f);
            float y = Mathf.Clamp(_smooth.y + tailH, screenMargin, Screen.height - screenMargin - size.y);
            _rect.position = new Vector3(x, y, 0f);

            // the tail points down at the panda; slide it along the bottom edge if the bubble got clamped
            _tail.gameObject.SetActive(true);
            float tailX = Mathf.Clamp((_smooth.x - x) / k, -_rect.rect.width / 2f + 20f, _rect.rect.width / 2f - 20f);
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
