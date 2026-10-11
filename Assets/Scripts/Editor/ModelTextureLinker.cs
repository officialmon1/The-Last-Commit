using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace PandaEditor
{
    /// <summary>
    /// The art FBXs (panda, trees, rocks...) were exported pointing at a base-colour PNG by absolute path,
    /// so Unity imports them with an empty material. This makes a URP Lit material per model using the PNG
    /// the FBX names (looked up in Assets/Materials) and remaps the model onto it.
    /// Re-run after adding new models via Tools > Panda > Link Model Textures
    /// (or `-executeMethod PandaEditor.ModelTextureLinker.LinkAll`).
    /// </summary>
    public static class ModelTextureLinker
    {
        const string ModelsRoot = "Assets/Prefabs";
        const string TexturesRoot = "Assets/Materials";
        const string OutputFolder = "Assets/Materials/Models";

        [MenuItem("Tools/Panda/Link Model Textures")]
        public static void LinkAll()
        {
            if (!AssetDatabase.IsValidFolder(OutputFolder))
                AssetDatabase.CreateFolder(TexturesRoot, "Models");

            int linked = 0;
            foreach (var guid in AssetDatabase.FindAssets("t:Model", new[] { ModelsRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (LinkModel(path)) linked++;
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[ModelTextureLinker] Linked textures for {linked} model(s).");
        }

        static bool LinkModel(string fbxPath)
        {
            var texture = FindReferencedTexture(fbxPath);
            if (texture == null)
            {
                Debug.LogWarning($"[ModelTextureLinker] {fbxPath}: no matching texture found in {TexturesRoot}, skipped.");
                return false;
            }

            var importer = (ModelImporter)AssetImporter.GetAtPath(fbxPath);
            var sourceMaterials = AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<Material>().ToArray();
            if (sourceMaterials.Length == 0) return false;

            string modelName = Path.GetFileNameWithoutExtension(fbxPath);
            string matPath = $"{OutputFolder}/{modelName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", texture);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.2f);
            SetCutout(mat, HasCutout(texture));
            EditorUtility.SetDirty(mat);

            foreach (var src in sourceMaterials)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), src.name), mat);
            importer.SaveAndReimport();
            return true;
        }

        /// <summary>Pulls the "Something_BaseColor.png" file name out of the FBX and finds that PNG in the project.</summary>
        static Texture2D FindReferencedTexture(string fbxPath)
        {
            string text = Encoding.ASCII.GetString(File.ReadAllBytes(fbxPath));
            foreach (Match m in Regex.Matches(text, @"([A-Za-z0-9_\-]+\.(?:png|jpg|jpeg|tga))", RegexOptions.IgnoreCase))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesRoot}/{m.Groups[1].Value}");
                if (tex != null) return tex;
            }
            return null;
        }

        /// <summary>True when a noticeable part of the texture is see-through (leaves, grass cards...).</summary>
        static bool HasCutout(Texture2D texture)
        {
            var copy = new Texture2D(2, 2);
            try
            {
                if (!copy.LoadImage(File.ReadAllBytes(AssetDatabase.GetAssetPath(texture)))) return false;
                var px = copy.GetPixels32();
                int clear = 0, sampled = 0;
                for (int i = 0; i < px.Length; i += 61) { sampled++; if (px[i].a < 128) clear++; }
                return sampled > 0 && clear > sampled * 0.02f;
            }
            finally { Object.DestroyImmediate(copy); }
        }

        static void SetCutout(Material mat, bool on)
        {
            mat.SetFloat("_AlphaClip", on ? 1f : 0f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.SetFloat("_Cull", on ? 0f : 2f); // leaves/grass are single planes: draw both sides
            if (on) mat.EnableKeyword("_ALPHATEST_ON"); else mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = on ? (int)UnityEngine.Rendering.RenderQueue.AlphaTest : -1;
        }
    }
}
