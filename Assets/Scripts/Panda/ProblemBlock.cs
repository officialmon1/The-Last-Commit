using UnityEngine;

namespace Panda
{
    /// <summary>A clickable block in the world. Clicking it opens the static problem card and starts a fresh hint session.</summary>
    public class ProblemBlock : MonoBehaviour
    {
        public string problemTitle = "Two Sum";

        [TextArea(3, 8)]
        public string problemBody =
            "Given an array of integers nums and an integer target, return the indices of the " +
            "two numbers that add up to target. Each input has exactly one solution, and you may " +
            "not use the same element twice.";

        [Tooltip("Optional: drag in a real screenshot of the problem. If left empty, the title/body text above is shown instead.")]
        public Sprite problemImage;

        public void OnClicked()
        {
            if (ProblemBoard.Instance != null)
                ProblemBoard.Instance.Show(this);

            if (PandaCompanion.Instance != null)
                PandaCompanion.Instance.ResetHintSession();

            if (PandaService.Instance != null)
                PandaService.Instance.SendEvent("reset");
        }
    }
}
