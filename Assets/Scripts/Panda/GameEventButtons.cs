using UnityEngine;
using UnityEngine.UI;

namespace Panda
{
    /// <summary>
    /// Stand-ins for real gameplay events, same role as the "Test failed" / "Solved it!" buttons
    /// in the web prototype (its own comment says "Unity will send these"). Wire these to the
    /// real test-runner / win condition later; for now they just drive the coach so you can demo it.
    /// </summary>
    public class GameEventButtons : MonoBehaviour
    {
        public Button failedButton;
        public Button solvedButton;
        public Button resetButton;
        public Text failedCountText;

        void Awake()
        {
            failedButton.onClick.AddListener(() =>
                PandaService.Instance.SendEvent("failed", resp =>
                {
                    if (resp != null) failedCountText.text = "Failed runs: " + resp.failed_runs;
                }));

            solvedButton.onClick.AddListener(() =>
                PandaService.Instance.SendEvent("solved", resp =>
                {
                    if (resp != null && resp.celebrate != null && PandaCompanion.Instance != null)
                        PandaCompanion.Instance.ShowCelebration(resp.text);
                }));

            resetButton.onClick.AddListener(() =>
            {
                PandaService.Instance.SendEvent("reset", resp =>
                {
                    failedCountText.text = "Failed runs: 0";
                });
                if (PandaCompanion.Instance != null) PandaCompanion.Instance.ResetHintSession();
            });
        }
    }
}
