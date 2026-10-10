using System;
using UnityEngine;
using UnityEngine.UI;

namespace Panda
{
    /// <summary>Small screen-space bubble showing what the panda is "saying", with up to two reply buttons.</summary>
    public class SpeechBubble : MonoBehaviour
    {
        public Text bubbleText;
        public Button button1;
        public Text button1Label;
        public Button button2;
        public Text button2Label;

        public const string IdleText = "Stuck on something? Click me for a hint.";

        void Awake()
        {
            ShowText(IdleText);
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
