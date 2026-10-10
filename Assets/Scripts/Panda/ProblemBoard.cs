using UnityEngine;
using UnityEngine.UI;

namespace Panda
{
    /// <summary>The full-screen panel that shows the (static, for now) LeetCode problem for a clicked block.</summary>
    public class ProblemBoard : MonoBehaviour
    {
        public static ProblemBoard Instance { get; private set; }

        public GameObject panelRoot;
        public Text titleText;
        public Text bodyText;
        public Image problemImage;
        public Button closeButton;

        public string CurrentProblemText { get; private set; } = "";

        void Awake()
        {
            Instance = this;
            closeButton.onClick.AddListener(Hide);
            panelRoot.SetActive(false);
        }

        public void Show(ProblemBlock block)
        {
            panelRoot.SetActive(true);
            titleText.text = block.problemTitle;
            bodyText.text = block.problemBody;
            CurrentProblemText = block.problemTitle + "\n" + block.problemBody;

            bool hasImage = block.problemImage != null;
            problemImage.gameObject.SetActive(hasImage);
            if (hasImage) problemImage.sprite = block.problemImage;
            bodyText.gameObject.SetActive(!hasImage);
        }

        public void Hide()
        {
            panelRoot.SetActive(false);
        }
    }
}
