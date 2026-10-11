using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Panda;

namespace PandaEditor
{
    /// <summary>
    /// One-shot pass over the already-wired SampleScene: moves the panda onto the left side of the
    /// default camera view, tones down its patrol so it stays on-screen, and gives the hand-placed
    /// UI (speech bubble, reply buttons, face-tracking readout) rounded corners and a forest palette
    /// to match the redesigned CodeEditorUI panel. Safe to re-run. Run once via
    /// Tools > Panda > Apply Forest UI Theme (or -executeMethod PandaEditor.ForestTheme.Apply).
    /// </summary>
    public static class ForestTheme
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string SpriteDir = "Assets/UI";
        const string SpritePath = SpriteDir + "/RoundedPanel.png";
        const int SpriteSize = 64;
        const int SpriteRadius = 18;

        [MenuItem("Tools/Panda/Apply Forest UI Theme")]
        public static void Apply()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Sprite rounded = GetOrCreateRoundedSprite();

            // ---------------- panda: left side of the default view, patrol kept on-screen ----------------
            var panda = GameObject.Find("Panda");
            if (panda != null)
            {
                var pos = panda.transform.localPosition;
                panda.transform.localPosition = new Vector3(-1.6f, pos.y, pos.z);
                var companion = panda.GetComponent<PandaCompanion>();
                if (companion != null)
                {
                    companion.patrolDistance = 0.9f;
                    EditorUtility.SetDirty(companion);
                    PrefabUtility.RecordPrefabInstancePropertyModifications(companion);
                }
                EditorUtility.SetDirty(panda.transform);
                PrefabUtility.RecordPrefabInstancePropertyModifications(panda.transform);
            }
            else Debug.LogWarning("[ForestTheme] No 'Panda' GameObject found; skipping panda placement.");

            // ---------------- face tracking readout: corner chip instead of dead-center ----------------
            var faceStatus = GameObject.Find("FaceStatus");
            if (faceStatus != null)
            {
                var rt = (RectTransform)faceStatus.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(20f, -20f);
                rt.sizeDelta = new Vector2(360f, 48f);

                var img = faceStatus.GetComponent<Image>() ?? faceStatus.AddComponent<Image>();
                img.sprite = rounded;
                img.type = Image.Type.Sliced;
                img.color = new Color(0.05f, 0.09f, 0.07f, 0.72f);
                // Image sits on the parent GameObject; the "Text" child already renders after it, on top

                var textTr = faceStatus.transform.Find("Text");
                if (textTr != null)
                {
                    var trt = (RectTransform)textTr;
                    trt.anchorMin = Vector2.zero;
                    trt.anchorMax = Vector2.one;
                    trt.anchoredPosition = Vector2.zero;
                    trt.sizeDelta = new Vector2(-24f, -12f);
                    var txt = textTr.GetComponent<Text>();
                    if (txt != null) txt.color = new Color(0.93f, 0.95f, 0.88f);
                }
            }
            else Debug.LogWarning("[ForestTheme] No 'FaceStatus' GameObject found; skipping.");

            // ---------------- speech bubble: rounded parchment panel + themed reply buttons ----------------
            var bubble = GameObject.Find("SpeechBubble");
            if (bubble != null)
            {
                var img = bubble.GetComponent<Image>();
                if (img != null)
                {
                    img.sprite = rounded;
                    img.type = Image.Type.Sliced;
                    img.color = new Color(0.97f, 0.95f, 0.85f, 0.98f);
                }
                var bubbleComp = bubble.GetComponent<SpeechBubble>();
                if (bubbleComp != null && bubbleComp.bubbleText != null) bubbleComp.bubbleText.color = new Color(0.09f, 0.14f, 0.1f);

                void ThemeReplyButton(Button b, Color bg, Color fg)
                {
                    if (b == null) return;
                    var bImg = b.GetComponent<Image>();
                    if (bImg != null) { bImg.sprite = rounded; bImg.type = Image.Type.Sliced; bImg.color = bg; }
                    var lbl = b.GetComponentInChildren<Text>();
                    if (lbl != null) lbl.color = fg;
                }
                if (bubbleComp != null)
                {
                    ThemeReplyButton(bubbleComp.button1, new Color(0.235f, 0.431f, 0.294f), Color.white);
                    ThemeReplyButton(bubbleComp.button2, new Color(0.55f, 0.62f, 0.5f), new Color(0.1f, 0.14f, 0.1f));
                }
            }
            else Debug.LogWarning("[ForestTheme] No 'SpeechBubble' GameObject found; skipping.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("FOREST_THEME_APPLIED");
        }

        /// <summary>Generates (once) a small persisted rounded-rect sprite asset used as a 9-sliced panel background.</summary>
        static Sprite GetOrCreateRoundedSprite()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            if (existing != null) return existing;

            if (!AssetDatabase.IsValidFolder(SpriteDir))
                AssetDatabase.CreateFolder("Assets", "UI");

            var tex = new Texture2D(SpriteSize, SpriteSize, TextureFormat.RGBA32, false);
            var pixels = new Color32[SpriteSize * SpriteSize];
            for (int y = 0; y < SpriteSize; y++)
                for (int x = 0; x < SpriteSize; x++)
                    pixels[y * SpriteSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(CornerAlpha(x, y) * 255f));
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes(SpritePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(SpritePath, ImportAssetOptions.ForceUpdate);
            var importer = (TextureImporter)AssetImporter.GetAtPath(SpritePath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spriteBorder = new Vector4(SpriteRadius, SpriteRadius, SpriteRadius, SpriteRadius);
            importer.spritePixelsPerUnit = 100f;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
        }

        static float CornerAlpha(int x, int y)
        {
            float fx = x + 0.5f, fy = y + 0.5f;
            bool inCornerX = fx < SpriteRadius || fx > SpriteSize - SpriteRadius;
            bool inCornerY = fy < SpriteRadius || fy > SpriteSize - SpriteRadius;
            if (!(inCornerX && inCornerY)) return 1f;
            float cx = fx < SpriteRadius ? SpriteRadius : SpriteSize - SpriteRadius;
            float cy = fy < SpriteRadius ? SpriteRadius : SpriteSize - SpriteRadius;
            float dist = Mathf.Sqrt((fx - cx) * (fx - cx) + (fy - cy) * (fy - cy));
            return Mathf.Clamp01(SpriteRadius - dist + 0.5f);
        }
    }
}
