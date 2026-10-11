using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

[RequireComponent(typeof(UIDocument))]
public class StartScreenController : MonoBehaviour
{
    [Header("Gameplay Connection")]
    [Tooltip("Invoked when the player clicks Begin Escape.")]
    [SerializeField]
    private UnityEvent onBeginEscape = new UnityEvent();

    [Header("Optional Menu Font")]
    [Tooltip(
        "Assign an imported .ttf or .otf font for titles and buttons. " +
        "Leave empty to use the default font."
    )]
    [SerializeField]
    private Font menuFont;

    private const float DesignWidth = 680f;
    private const float MinimumDesignHeight = 800f;
    private const float ScreenPadding = 24f;

    private VisualElement root;
    private VisualElement screen;
    private VisualElement shell;

    private VisualElement menuPanel;
    private VisualElement instructionsPanel;
    private VisualElement briefingPanel;
    private VisualElement activePanel;

    private Button enterButton;
    private Button howToPlayButton;
    private Button instructionsBackButton;
    private Button beginButton;
    private Button briefingBackButton;

    private bool escapeStarted;

    private void OnEnable()
    {
        UIDocument document = GetComponent<UIDocument>();
        root = document.rootVisualElement;

        screen = root.Q<VisualElement>("start-screen");
        shell = root.Q<VisualElement>("menu-shell");

        menuPanel = root.Q<VisualElement>("menu-panel");
        instructionsPanel =
            root.Q<VisualElement>("instructions-panel");
        briefingPanel =
            root.Q<VisualElement>("briefing-panel");

        enterButton = root.Q<Button>("enter-button");
        howToPlayButton =
            root.Q<Button>("how-to-play-button");
        instructionsBackButton =
            root.Q<Button>("instructions-back-button");
        beginButton = root.Q<Button>("begin-button");
        briefingBackButton =
            root.Q<Button>("briefing-back-button");

        if (!HasRequiredElements())
        {
            Debug.LogError(
                "StartScreenController: Required UI elements " +
                "were not found. Assign the supplied " +
                "StartScreen.uxml to the UI Document Source Asset.",
                this
            );

            return;
        }

        enterButton.clicked += ShowBriefing;
        howToPlayButton.clicked += ShowInstructions;
        instructionsBackButton.clicked += ShowMenu;
        beginButton.clicked += BeginEscape;
        briefingBackButton.clicked += ShowMenu;

        root.RegisterCallback<GeometryChangedEvent>(
            OnGeometryChanged
        );

        menuPanel.RegisterCallback<GeometryChangedEvent>(
            OnGeometryChanged
        );

        instructionsPanel.RegisterCallback<GeometryChangedEvent>(
            OnGeometryChanged
        );

        briefingPanel.RegisterCallback<GeometryChangedEvent>(
            OnGeometryChanged
        );

        ApplyMenuFont();
        ShowMenu();
    }

    private void OnDisable()
    {
        if (enterButton != null)
        {
            enterButton.clicked -= ShowBriefing;
        }

        if (howToPlayButton != null)
        {
            howToPlayButton.clicked -= ShowInstructions;
        }

        if (instructionsBackButton != null)
        {
            instructionsBackButton.clicked -= ShowMenu;
        }

        if (beginButton != null)
        {
            beginButton.clicked -= BeginEscape;
        }

        if (briefingBackButton != null)
        {
            briefingBackButton.clicked -= ShowMenu;
        }

        UnregisterGeometryCallback(root);
        UnregisterGeometryCallback(menuPanel);
        UnregisterGeometryCallback(instructionsPanel);
        UnregisterGeometryCallback(briefingPanel);
    }

    private void UnregisterGeometryCallback(VisualElement element)
    {
        if (element != null)
        {
            element.UnregisterCallback<GeometryChangedEvent>(
                OnGeometryChanged
            );
        }
    }

    private bool HasRequiredElements()
    {
        return screen != null
            && shell != null
            && menuPanel != null
            && instructionsPanel != null
            && briefingPanel != null
            && enterButton != null
            && howToPlayButton != null
            && instructionsBackButton != null
            && beginButton != null
            && briefingBackButton != null;
    }

    private void OnGeometryChanged(GeometryChangedEvent evt)
    {
        FitMenuToScreen();
    }

    private void FitMenuToScreen()
    {
        if (root == null || shell == null)
        {
            return;
        }

        float screenWidth = root.contentRect.width;
        float screenHeight = root.contentRect.height;

        if (screenWidth <= 0f || screenHeight <= 0f)
        {
            return;
        }

        float panelHeight = 0f;

        if (activePanel != null)
        {
            panelHeight = activePanel.layout.height;

            if (float.IsNaN(panelHeight)
                || float.IsInfinity(panelHeight))
            {
                panelHeight = 0f;
            }
        }

        float designHeight = Mathf.Max(
            MinimumDesignHeight,
            panelHeight + 24f
        );

        float availableWidth = Mathf.Max(
            1f,
            screenWidth - ScreenPadding * 2f
        );

        float availableHeight = Mathf.Max(
            1f,
            screenHeight - ScreenPadding * 2f
        );

        float scale = Mathf.Min(
            1f,
            availableWidth / DesignWidth,
            availableHeight / designHeight
        );

        shell.style.width = DesignWidth;
        shell.style.height = designHeight;

        shell.style.left =
            (screenWidth - DesignWidth) * 0.5f;

        shell.style.top =
            (screenHeight - designHeight) * 0.5f;

        shell.style.scale = new Scale(
            new Vector3(scale, scale, 1f)
        );
    }

    private void ApplyMenuFont()
    {
        if (menuFont == null)
        {
            return;
        }

        root.Query<Label>(className: "title").ForEach(label =>
        {
            label.style.unityFontDefinition =
                FontDefinition.FromFont(menuFont);
        });

        root.Query<Label>(className: "panel-title").ForEach(label =>
        {
            label.style.unityFontDefinition =
                FontDefinition.FromFont(menuFont);
        });

        root.Query<Button>(className: "menu-button").ForEach(button =>
        {
            button.style.unityFontDefinition =
                FontDefinition.FromFont(menuFont);
        });
    }

    public void ShowMenu()
    {
        if (!HasRequiredElements())
        {
            return;
        }

        escapeStarted = false;
        screen.style.display = DisplayStyle.Flex;

        ShowOnly(menuPanel);

        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        enterButton.Focus();
    }

    private void ShowInstructions()
    {
        ShowOnly(instructionsPanel);
        instructionsBackButton.Focus();
    }

    private void ShowBriefing()
    {
        ShowOnly(briefingPanel);
        beginButton.Focus();
    }

    private void ShowOnly(VisualElement selectedPanel)
    {
        activePanel = selectedPanel;

        menuPanel.style.display =
            selectedPanel == menuPanel
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        instructionsPanel.style.display =
            selectedPanel == instructionsPanel
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        briefingPanel.style.display =
            selectedPanel == briefingPanel
                ? DisplayStyle.Flex
                : DisplayStyle.None;

        FitMenuToScreen();

        // Recheck after the newly visible panel has been laid out.
        root.schedule.Execute(FitMenuToScreen);
    }

    private void BeginEscape()
    {
        if (escapeStarted)
        {
            return;
        }

        escapeStarted = true;
        screen.style.display = DisplayStyle.None;

        Debug.Log(
            "Begin Escape clicked. Menu hidden. " +
            "Connect On Begin Escape to your gameplay controller.",
            this
        );

        onBeginEscape.Invoke();
    }
}