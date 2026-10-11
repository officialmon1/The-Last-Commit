using UnityEngine;
using UnityEngine.UI;

namespace Panda
{
    /// <summary>Small readout of what the backend's face tracker currently sees, for debugging/demo.</summary>
    public class FaceStatusDisplay : MonoBehaviour
    {
        public Text label;

        void Start()
        {
            var bg = GetComponent<Image>();
            if (bg != null)
            {
                bg.color = ForestTheme.PanelBg;
                bg.sprite = ForestTheme.PanelSprite;
                bg.type = Image.Type.Sliced;
            }
            var font = ForestTheme.ElegantFont();
            if (label != null)
            {
                label.color = ForestTheme.TextCream;
                if (font != null) label.font = font;
            }

            if (PandaService.Instance != null)
                PandaService.Instance.OnStatusUpdated += s =>
                {
                    label.text = s.face_on
                        ? $"Face: {(s.calibrating ? "calibrating" : s.state)} ({s.why})"
                        : "Face tracking: off";
                };
        }
    }
}
