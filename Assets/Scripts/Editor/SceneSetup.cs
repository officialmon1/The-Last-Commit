using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Panda;

namespace PandaEditor
{
    /// <summary>
    /// One-shot scene wiring: builds the panda companion, a clickable problem block, the
    /// problem-card/speech-bubble UI, and the backend bridge into SampleScene. Run once via
    /// Tools > Panda > Build Demo Scene (or `-executeMethod PandaEditor.SceneSetup.BuildDemoScene`).
    /// Safe to re-run: it bails out if it looks like the scene is already wired.
    /// </summary>
    public static class SceneSetup
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string PandaModelPath = "Assets/Prefabs/Characters/Panda.fbx";
        const string PandaPrefabPath = "Assets/Prefabs/Characters/Panda.prefab";

        [MenuItem("Tools/Panda/Build Demo Scene")]
        public static void BuildDemoScene()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            if (GameObject.Find("PandaService") != null)
            {
                Debug.LogWarning("[SceneSetup] Scene already looks wired up (found a 'PandaService' object). Not touching it.");
                return;
            }

            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            // ---------------- backend bridge + click router ----------------
            var serviceGO = new GameObject("PandaService", typeof(PandaService));
            var service = serviceGO.GetComponent<PandaService>();

            var camera = Object.FindAnyObjectByType<Camera>();
            var routerGO = new GameObject("ClickRouter", typeof(ClickRouter));
            var router = routerGO.GetComponent<ClickRouter>();
            router.worldCamera = camera;

            // ---------------- canvas ----------------
            var canvasGO = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);

            if (Object.FindAnyObjectByType<EventSystem>() == null)
                new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            // ---------------- problem board (full screen card) ----------------
            var problemPanel = CreatePanel(canvasGO.transform, "ProblemPanel", new Color(0f, 0f, 0f, 0.6f));
            SetRect(problemPanel.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            var card = CreatePanel(problemPanel.transform, "Card", new Color(0.07f, 0.13f, 0.1f, 0.97f));
            var cardRect = card.GetComponent<RectTransform>();
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.sizeDelta = new Vector2(900, 620);
            cardRect.anchoredPosition = Vector2.zero;

            var title = CreateText(card.transform, "Title", "Two Sum", font, 36, TextAnchor.UpperLeft, Color.white);
            SetRect(title.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(30, -110), new Vector2(-30, -20));

            var body = CreateText(card.transform, "Body",
                "Given an array of integers nums and an integer target, return the indices of the two numbers that add up to target.",
                font, 24, TextAnchor.UpperLeft, new Color(0.9f, 0.96f, 0.93f));
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Overflow;
            SetRect(body.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(30, 30), new Vector2(-30, -110));

            var problemImageGO = new GameObject("ProblemImage", typeof(RectTransform), typeof(Image));
            problemImageGO.transform.SetParent(card.transform, false);
            var problemImage = problemImageGO.GetComponent<Image>();
            problemImage.preserveAspect = true;
            SetRect(problemImageGO.GetComponent<RectTransform>(), Vector2.zero, Vector2.one, new Vector2(30, 30), new Vector2(-30, -110));
            problemImageGO.SetActive(false);

            var closeButton = CreateButton(card.transform, "CloseButton", "Close", new Color(0.18f, 0.35f, 0.28f));
            SetRect(closeButton.GetComponent<RectTransform>(), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-110, -80), new Vector2(-20, -20));

            var board = problemPanel.AddComponent<ProblemBoard>();
            board.panelRoot = problemPanel;
            board.titleText = title;
            board.bodyText = body;
            board.problemImage = problemImage;
            board.closeButton = closeButton;

            // ---------------- speech bubble (bottom right, near the panda) ----------------
            var bubblePanel = CreatePanel(canvasGO.transform, "SpeechBubble", new Color(0.96f, 1f, 0.98f, 0.97f));
            var bubbleRect = bubblePanel.GetComponent<RectTransform>();
            bubbleRect.anchorMin = bubbleRect.anchorMax = new Vector2(1f, 0f);
            bubbleRect.pivot = new Vector2(1f, 0f);
            bubbleRect.sizeDelta = new Vector2(380, 220);
            bubbleRect.anchoredPosition = new Vector2(-30, 30);

            var bubbleText = CreateText(bubblePanel.transform, "Text", SpeechBubble.IdleText, font, 22, TextAnchor.UpperLeft, new Color(0.07f, 0.13f, 0.1f));
            bubbleText.horizontalOverflow = HorizontalWrapMode.Wrap;
            bubbleText.verticalOverflow = VerticalWrapMode.Overflow;
            SetRect(bubbleText.rectTransform, new Vector2(0, 0), new Vector2(1, 1), new Vector2(16, 60), new Vector2(-16, -16));

            var btn1 = CreateButton(bubblePanel.transform, "Button1", "Yes", new Color(0.18f, 0.56f, 0.39f));
            SetRect(btn1.GetComponent<RectTransform>(), new Vector2(0, 0), new Vector2(0.5f, 0), new Vector2(16, 14), new Vector2(-6, 48));
            var btn2 = CreateButton(bubblePanel.transform, "Button2", "No", new Color(0.85f, 0.91f, 0.87f));
            btn2.GetComponentInChildren<Text>().color = Color.black;
            SetRect(btn2.GetComponent<RectTransform>(), new Vector2(0.5f, 0), new Vector2(1f, 0), new Vector2(6, 14), new Vector2(-16, 48));

            var bubble = bubblePanel.AddComponent<SpeechBubble>();
            bubble.bubbleText = bubbleText;
            bubble.button1 = btn1; bubble.button1Label = btn1.GetComponentInChildren<Text>();
            bubble.button2 = btn2; bubble.button2Label = btn2.GetComponentInChildren<Text>();

            // ---------------- stand-in event buttons (bottom left) ----------------
            var eventsPanel = CreatePanel(canvasGO.transform, "EventButtons", new Color(0.04f, 0.08f, 0.07f, 0.9f));
            var eventsRect = eventsPanel.GetComponent<RectTransform>();
            eventsRect.anchorMin = eventsRect.anchorMax = new Vector2(0f, 0f);
            eventsRect.pivot = new Vector2(0f, 0f);
            eventsRect.sizeDelta = new Vector2(260, 160);
            eventsRect.anchoredPosition = new Vector2(30, 30);

            var failedBtn = CreateButton(eventsPanel.transform, "FailedButton", "Test failed", new Color(0.4f, 0.2f, 0.2f));
            SetRect(failedBtn.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -58), new Vector2(-14, -14));
            var solvedBtn = CreateButton(eventsPanel.transform, "SolvedButton", "Solved it!", new Color(0.18f, 0.45f, 0.3f));
            SetRect(solvedBtn.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -104), new Vector2(-14, -60));
            var resetBtn = CreateButton(eventsPanel.transform, "ResetButton", "Reset", new Color(0.3f, 0.3f, 0.3f));
            SetRect(resetBtn.GetComponent<RectTransform>(), new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -150), new Vector2(-14, -106));
            var failedCountText = CreateText(eventsPanel.transform, "FailedCount", "Failed runs: 0", font, 18, TextAnchor.UpperLeft, Color.white);
            SetRect(failedCountText.rectTransform, new Vector2(0, 1), new Vector2(1, 1), new Vector2(14, -22), new Vector2(-14, -2));

            var eventButtons = eventsPanel.AddComponent<GameEventButtons>();
            eventButtons.failedButton = failedBtn;
            eventButtons.solvedButton = solvedBtn;
            eventButtons.resetButton = resetBtn;
            eventButtons.failedCountText = failedCountText;

            // ---------------- face status (top left) ----------------
            var faceStatusGO = new GameObject("FaceStatus", typeof(RectTransform));
            faceStatusGO.transform.SetParent(canvasGO.transform, false);
            var faceStatusText = CreateText(faceStatusGO.transform, "Text", "Face tracking: off", font, 18, TextAnchor.UpperLeft, Color.white);
            SetRect(faceStatusText.rectTransform, new Vector2(0, 1), new Vector2(0.5f, 1), new Vector2(20, -40), new Vector2(0, -10));
            var faceStatus = faceStatusGO.AddComponent<FaceStatusDisplay>();
            faceStatus.label = faceStatusText;

            // ---------------- the panda ----------------
            var pandaAsset = AssetDatabase.LoadAssetAtPath<GameObject>(PandaModelPath);
            GameObject pandaInstance;
            if (pandaAsset == null)
            {
                Debug.LogError($"[SceneSetup] Couldn't find the panda model at {PandaModelPath}; creating a placeholder cube instead.");
                pandaInstance = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                pandaInstance.name = "Panda";
            }
            else
            {
                pandaInstance = (GameObject)PrefabUtility.InstantiatePrefab(pandaAsset);
                pandaInstance.name = "Panda";
            }
            pandaInstance.transform.position = new Vector3(1.6f, 0f, 0f);

            var pandaCollider = pandaInstance.AddComponent<BoxCollider>();
            pandaCollider.center = new Vector3(0, 1f, 0);
            pandaCollider.size = new Vector3(1.2f, 2f, 1.2f);

            var companion = pandaInstance.AddComponent<PandaCompanion>();
            companion.speechBubble = bubble;

            if (pandaAsset != null)
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(pandaInstance, PandaPrefabPath, InteractionMode.AutomatedAction);
            }

            // ---------------- the clickable problem block ----------------
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "ProblemBlock_TwoSum";
            block.transform.position = new Vector3(-1.6f, 0.5f, 0f);
            var renderer = block.GetComponent<Renderer>();
            renderer.sharedMaterial = new Material(renderer.sharedMaterial) { color = new Color(0.25f, 0.55f, 0.4f) };
            block.AddComponent<ProblemBlock>();

            // keep board/light/camera defaults untouched
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneSetup] Demo scene wired up: PandaService, ClickRouter, UI, Panda, ProblemBlock_TwoSum.");
        }

        static GameObject CreatePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return go;
        }

        static Text CreateText(Transform parent, string name, string content, Font font, int size, TextAnchor anchor, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var t = go.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = color;
            t.text = content;
            return t;
        }

        static Button CreateButton(Transform parent, string name, string label, Color bg)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = bg;
            var btn = go.AddComponent<Button>();

            var textGO = new GameObject("Label", typeof(RectTransform));
            textGO.transform.SetParent(go.transform, false);
            var t = textGO.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = label;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            var trt = textGO.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero; trt.offsetMax = Vector2.zero;

            return btn;
        }

        static void SetRect(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
        {
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = offsetMin;
            rt.offsetMax = offsetMax;
        }
    }
}
