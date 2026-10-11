using System.Collections.Generic;
using UnityEngine;

namespace Panda
{
    /// <summary>
    /// The forest/amber palette and storybook font shared by every on-screen UI element: the speech
    /// bubble, the code editor panel, the face-tracker readout and the debug overlays. Centralised here
    /// so they all genuinely match, rather than each keeping their own slightly-different copy of the colours.
    /// </summary>
    public static class ForestTheme
    {
        public static readonly Color PanelBg = new Color(0.098f, 0.145f, 0.118f, 0.97f);
        public static readonly Color BoxBg = new Color(0.118f, 0.169f, 0.137f);
        public static readonly Color Amber = new Color(0.816f, 0.573f, 0.231f, 0.9f);
        public static readonly Color TextCream = new Color(0.945f, 0.914f, 0.824f);
        public static readonly Color TextMuted = new Color(0.749f, 0.796f, 0.722f);
        public static readonly Color AccentGreen = new Color(0.235f, 0.431f, 0.294f);
        public static readonly Color AccentMuted = new Color(0.357f, 0.322f, 0.278f);

        static Font _elegantFont;
        static bool _lookedUpFont;

        /// <summary>A friendlier, more storybook-ish font than the OS default, if one happens to be installed. Null if none are.</summary>
        public static Font ElegantFont()
        {
            if (_lookedUpFont) return _elegantFont;
            _lookedUpFont = true;
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames());
            foreach (var name in new[] { "Papyrus", "Herculanum", "Chalkboard SE", "Baskerville", "Georgia", "Garamond", "Segoe Script" })
                if (installed.Contains(name)) { _elegantFont = Font.CreateDynamicFontFromOSFont(name, 18); break; }
            return _elegantFont;
        }

        static Sprite _roundedSprite, _leafSprite;
        public static Sprite RoundedSprite(int size = 32, int radius = 9) => MakeRoundedSprite(size, radius);
        public static Sprite LeafSprite => _leafSprite ??= MakeRoundedSprite(16, 8);
        public static Sprite PanelSprite => _roundedSprite ??= MakeRoundedSprite(32, 9);

        static Sprite MakeRoundedSprite(int size, int radius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(RoundedCornerAlpha(x, y, size, radius) * 255f));
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
                SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }

        static float RoundedCornerAlpha(int x, int y, int size, int radius)
        {
            float fx = x + 0.5f, fy = y + 0.5f;
            bool inCornerX = fx < radius || fx > size - radius;
            bool inCornerY = fy < radius || fy > size - radius;
            if (!(inCornerX && inCornerY)) return 1f;
            float cx = fx < radius ? radius : size - radius;
            float cy = fy < radius ? radius : size - radius;
            float dist = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
            return Mathf.Clamp01(radius - dist + 0.5f);
        }

        /// <summary>Adds a small amber "leaf" accent dot, anchored at one corner of <paramref name="parent"/>.</summary>
        public static void AddLeafAccent(Transform parent, Vector2 anchor, Vector2 offset)
        {
            var go = new GameObject("Leaf", typeof(RectTransform), typeof(UnityEngine.UI.Image));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<UnityEngine.UI.Image>();
            img.color = Amber;
            img.sprite = LeafSprite;
            img.raycastTarget = false;
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.sizeDelta = new Vector2(14f, 14f);
            rt.anchoredPosition = offset;
        }
    }
}
