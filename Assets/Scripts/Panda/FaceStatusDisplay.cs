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
