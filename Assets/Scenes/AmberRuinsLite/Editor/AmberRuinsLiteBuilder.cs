#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Unity 6, Built-in or URP. References the project's existing foreground models and textures.
/// Each run saves a separate scene in a new folder. No source prefab, scene or project setting is overwritten.
/// </summary>
public static class AmberRuinsLiteBuilder
{
    const string Source = "Assets/Prefabs";
    static string Output;
    static string SceneFile;
    static System.Random rng;
    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    static readonly List<Vector3> route = new List<Vector3>();
    static GameObject[] trees, willows, grasses, ruins, rocks, undergrowth;
    static Transform root;
    static float routeLength;
    static int foregroundTrees, floorDetails;
    static bool urp;
    static readonly Dictionary<Material, Material> compatibleMats = new Dictionary<Material, Material>();
    static readonly Dictionary<GameObject, Vector3> trunkFits = new Dictionary<GameObject, Vector3>();
    // Existing foreground mesh and texture assets are referenced, never copied.
    const int TreeBudget = 56;
    const float TreeSpacing = 5f;
    const float PathTreeClearance = 5.5f;
    const string FloorTexturePath = "Assets/AmberRuinsLite/Textures/ForestFloor_Albedo.png";
    const int GrassBudget = 220;
    const int UndergrowthBudget = 44;
    const int RockBudget = 26;

    [MenuItem("Tools/Amber Ruins/Create Lighter Scene (Reuse My Assets)")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogWarning("Stop Play mode before creating the scene."); return; }
        var pipeline = GraphicsSettings.currentRenderPipeline;
        urp = pipeline != null && pipeline.GetType().Name.IndexOf("Universal", StringComparison.OrdinalIgnoreCase) >= 0;
        if (pipeline != null && !urp) {
            EditorUtility.DisplayDialog("Render pipeline", "This builder supports Built-in and URP. The active project uses another pipeline.", "OK"); return;
        }
        trees = Find("Trees"); willows = Find("Willows"); grasses = Find("Grass");
        ruins = Find("Ruins"); rocks = Find("Rocks"); undergrowth = Find("Undergrowth");
        var missing = new List<string>();
        if (trees.Length == 0) missing.Add("Trees");
        if (willows.Length == 0) willows = trees;
        if (ruins.Length == 0) missing.Add("Ruins");
        if (missing.Count > 0)
        {
            EditorUtility.DisplayDialog("Missing required prefabs",
                "The existing project needs a prefab/model in these folders:\n" +
                string.Join(", ", missing.ToArray()) + "\n\nUnder Assets/Prefabs/.\n" +
                "The generator has NOT deleted or changed any assets.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

        try
        {
            rng = new System.Random(20261010);
            // Create the parent before choosing a build folder. Folder paths are not file asset paths.
            const string outputParent = "Assets/AmberRuinsLiteScenes";
            Folder(outputParent);
            Output = outputParent + "/Forest_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") +
                "_" + Guid.NewGuid().ToString("N");
            SceneFile = Output + "/Scenes/TheAmberRuins_Lite.unity";
            foregroundTrees = floorDetails = 0;
            compatibleMats.Clear();
            trunkFits.Clear();
            Folder(Output); Folder(Output + "/Scenes"); Folder(Output + "/Materials"); Folder(Output + "/Meshes");
            Folder(Output + "/Textures");
            mats.Clear();
            CreateMaterials();
            MakeRoute();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            root = new GameObject("THE AMBER RUINS | LIGHTER ENVIRONMENT").transform;
            MakeTerrain();
            MakePath();
            MakeForest();
            MakeSanctuary();
            MakeLightingAndMoon();
            MakeParticles();
            MakeBoundaries();
            MakeSafeDetailSettings();
            MakeSpawn();
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Hex("#3D2630");
            RenderSettings.fogStartDistance = AmberRuinsLiteBoundaryFog.DefaultFogStart;
            RenderSettings.fogEndDistance = AmberRuinsLiteBoundaryFog.DefaultFogEnd;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.29f, .31f, .35f);
            MakeCrimsonSky();
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, SceneFile)) throw new Exception("Scene save failed: " + SceneFile);
            AssetDatabase.Refresh();
            Selection.activeGameObject = root.gameObject;
            if (SceneView.lastActiveSceneView != null)
            {
                Vector3 look = route[8] - route[0]; look.y = 0; look.Normalize();
                SceneView.lastActiveSceneView.LookAt(route[0] + Vector3.up * 1.7f + look * 5, Quaternion.LookRotation(look), 5f);
                SceneView.lastActiveSceneView.Repaint();
            }
            Debug.Log("Amber Ruins: saved " + SceneFile + ". Path length: " + routeLength.ToString("F0") +
                      "m (~" + (routeLength / 4f).ToString("F0") + "s at 4m/s). Source assets untouched.");
            EditorUtility.DisplayDialog("Scene generated", "Scene saved at:\n" + SceneFile +
                "\n\nEstimated path: " + routeLength.ToString("F0") + " m.\n" +
                "\nDetailed tree instances: " + foregroundTrees +
                "\nBackground tree cards: 0" +
                "\nFog-hidden boundary: 80 x 80 m" +
                "\nFloor details: " + floorDetails +
                "\n\nYour original scene and source assets were preserved. Test the new scene on your Mac.", "OK");
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorUtility.DisplayDialog("Build failed", ex.Message + "\nSee Console for stack trace.", "OK");
        }
    }

    static GameObject[] Find(string category)
    {
        var found = new List<GameObject>();
        var seen = new HashSet<string>();
        string[] roots = { Source, "Assets/AmberRuins/RealisticAssets" };
        foreach (string sourceRoot in roots)
        {
            string path = sourceRoot + "/" + category;
            if (!AssetDatabase.IsValidFolder(path)) continue;
            foreach (string filter in new[] { "t:Prefab", "t:Model" })
            foreach (string guid in AssetDatabase.FindAssets(filter, new[] { path }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!seen.Add(assetPath)) continue;
                GameObject obj = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (obj != null && obj.GetComponentsInChildren<Renderer>(true).Length > 0) found.Add(obj);
            }
            if (found.Count > 0) break;
        }
        found.Sort((a, b) => string.Compare(AssetDatabase.GetAssetPath(a), AssetDatabase.GetAssetPath(b), StringComparison.Ordinal));
        return found.ToArray();
    }

    static void Folder(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("The generated folder path is empty.", nameof(path));
        path = path.Replace('\\', '/').TrimEnd('/');
        string[] parts = path.Split('/');
        if (parts.Length == 0 || parts[0] != "Assets")
            throw new ArgumentException("Expected a project-relative folder path beginning with Assets: " + path, nameof(path));
        foreach (string part in parts)
            if (string.IsNullOrWhiteSpace(part) || part == "." || part == "..")
                throw new ArgumentException("Invalid folder path segment in: " + path, nameof(path));
        if (!AssetDatabase.IsValidFolder("Assets"))
            throw new IOException("Unity could not find the project's Assets folder.");
        string parent = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string child = parent + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(child))
            {
                string createdGuid = AssetDatabase.CreateFolder(parent, parts[i]);
                if (string.IsNullOrEmpty(createdGuid) || !AssetDatabase.IsValidFolder(child))
                    throw new IOException("Unity could not create the generated folder: " + child);
            }
            parent = child;
        }
    }
    static float R(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }
    static Material Material(string name, Color color, bool glow = false, bool transparent = false)
    {
        Shader shader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
        if (shader == null) throw new InvalidOperationException("The required lighting shader was not found.");
        var mat = new Material(shader); mat.name = name; mat.enableInstancing = true;
        mat.color = color; mat.SetFloat("_Glossiness", .12f);
        if (glow) { mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor", color * 2f); }
        if (transparent)
        {
            mat.SetFloat("_Mode", 3); mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha); mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON"); mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.EnableKeyword("_ALPHABLEND_ON"); mat.renderQueue = 3000;
        }
        if (urp && transparent)
        {
            mat.SetFloat("_Surface", 1); mat.SetFloat("_Blend", 0); mat.SetFloat("_ZWrite", 0);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetColor("_BaseColor", color); mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetOverrideTag("RenderType", "Transparent"); mat.renderQueue = 3000;
        }
        AssetDatabase.CreateAsset(mat, Output + "/Materials/" + name + ".mat"); mats[name] = mat;
        return mat;
    }
    static void CreateMaterials()
    {
        Material("ForestEarth", Color.white); Material("PathEarth", Color.white);
        Material("WeatheredStone", Hex("#77766C")); Material("ShadowStone", Hex("#454741"));
        Material("DarkMoss", Hex("#34432C")); Material("AmberRune", Hex("#FFD27A"), true);
        Material("PortalAmber", new Color(1f, .57f, .13f, .58f), true, true);
        Material("MoonCrimson", Hex("#B52D42"), true);
        // One shared generated leaf-litter texture is referenced by every new scene.
        var floorTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(FloorTexturePath);
        if (floorTexture == null)
            throw new InvalidOperationException("Copy the entire updated Assets/AmberRuinsLite folder, including Textures/ForestFloor_Albedo.png, before generating the scene.");
        var importer = AssetImporter.GetAtPath(FloorTexturePath) as TextureImporter;
        if (importer != null && (importer.maxTextureSize != 1024 || !importer.mipmapEnabled ||
            importer.wrapMode != TextureWrapMode.Repeat || importer.isReadable ||
            !importer.sRGBTexture || importer.anisoLevel != 4 || importer.filterMode != FilterMode.Trilinear ||
            importer.npotScale != TextureImporterNPOTScale.ToNearest || importer.textureCompression != TextureImporterCompression.Compressed))
        {
            // Only configure the new texture supplied by this package.
            importer.maxTextureSize = 1024; importer.mipmapEnabled = true;
            importer.sRGBTexture = true; importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear; importer.anisoLevel = 4;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.isReadable = false; importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();
            floorTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(FloorTexturePath);
        }
        GroundTexture(mats["ForestEarth"], floorTexture, new Vector2(80, 80));
        // Fine dirt and worn ruts keep the walkway distinct without a flat-color strip.
        var tex = new Texture2D(256, 256, TextureFormat.RGBA32, true);
        tex.name = "TrampledPathSoil";
        var colors = new Color[256 * 256];
        for (int y = 0; y < 256; y++) for (int x = 0; x < 256; x++)
        {
            float u = x / 255f;
            float a = Mathf.PerlinNoise(x * .022f, y * .022f);
            float b = Mathf.PerlinNoise(x * .13f + 14, y * .13f + 21);
            float grain = Mathf.PerlinNoise(x * .73f + 53, y * .73f + 19);
            float ruts = Mathf.Exp(-Mathf.Pow((u - .28f) * 13f, 2)) + Mathf.Exp(-Mathf.Pow((u - .72f) * 13f, 2));
            colors[y * 256 + x] = Color.Lerp(Hex("#493A2D"), Hex("#8A765D"),
                Mathf.Clamp01(.46f * a + .33f * b + .21f * grain - .16f * ruts));
        }
        tex.SetPixels(colors); tex.wrapMode = TextureWrapMode.Repeat; tex.Apply();
        tex.filterMode = FilterMode.Trilinear; tex.anisoLevel = 4;
        AssetDatabase.CreateAsset(tex, Output + "/Textures/TrampledPathSoil.asset");
        GroundTexture(mats["PathEarth"], tex, Vector2.one);
    }
    static void GroundTexture(Material material, Texture texture, Vector2 tiling)
    {
        material.color = Color.white;
        material.mainTexture = texture; material.mainTextureScale = tiling;
        if (material.HasProperty("_BaseMap")) { material.SetTexture("_BaseMap", texture); material.SetTextureScale("_BaseMap", tiling); }
        if (material.HasProperty("_MainTex")) { material.SetTexture("_MainTex", texture); material.SetTextureScale("_MainTex", tiling); }
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .08f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .08f);
        if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0);
        EditorUtility.SetDirty(material);
    }
    static Color Hex(string h) { Color c; ColorUtility.TryParseHtmlString(h, out c); return c; }
    static Transform Group(string name, Transform parent)
    {
        var go = new GameObject(name); go.transform.SetParent(parent, false); return go.transform;
    }
    static float H(float x, float z)
    {
        float hills = .85f * Mathf.Sin(x * .067f) + .72f * Mathf.Cos(z * .073f)
                    + .65f * (Mathf.PerlinNoise(x * .065f + 25f, z * .065f + 25f) - .5f);
        float d = new Vector2(x - 1f, z - 22f).magnitude;
        return Mathf.Lerp(.3f, hills, Mathf.SmoothStep(0, 1, Mathf.Clamp01((d - 9f) / 9f)));
    }
    static void MakeTerrain()
    {
        // 160 x 160 m physical ground so distant forest has no visible void.
        const int n = 65;
        var verts = new Vector3[n * n]; var uv = new Vector2[n * n];
        var tris = new int[(n - 1) * (n - 1) * 6];
        for (int z = 0; z < n; z++) for (int x = 0; x < n; x++)
        { int i = z * n + x; float xx = -80f + x * 160f / (n - 1), zz = -80f + z * 160f / (n - 1);
          verts[i] = new Vector3(xx, H(xx, zz), zz); uv[i] = new Vector2(x / (float)(n - 1), z / (float)(n - 1)); }
        int t = 0;
        for (int z = 0; z < n - 1; z++) for (int x = 0; x < n - 1; x++)
        { int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
          tris[t++] = a; tris[t++] = c; tris[t++] = b; tris[t++] = b; tris[t++] = c; tris[t++] = d; }
        var mesh = new Mesh { name = "ForestTerrain_160m", indexFormat = IndexFormat.UInt32 };
        mesh.vertices = verts; mesh.uv = uv; mesh.triangles = tris;
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, Output + "/Meshes/ForestTerrain.asset");
        var ground = new GameObject("TERRAIN | 160m visual ground + collider");
        ground.transform.SetParent(root, false);
        ground.AddComponent<MeshFilter>().sharedMesh = mesh;
        ground.AddComponent<MeshRenderer>().sharedMaterial = mats["ForestEarth"];
        ground.AddComponent<MeshCollider>().sharedMesh = mesh;
    }
    static Vector3 Catmull(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
    {
        return .5f * ((2f * b) + (-a + c) * t +
            (2f * a - 5f * b + 4f * c - d) * t * t +
            (-a + 3f * b - 3f * c + d) * t * t * t);
    }
    static void MakeRoute()
    {
        // Deliberate serpentine path; mostly within +-36m. Last turn conceals sanctuary.
        var pts = new[] {
            new Vector3(-30,0,-35), new Vector3(-18,0,-29), new Vector3(12,0,-32),
            new Vector3(30,0,-22), new Vector3(14,0,-12), new Vector3(-22,0,-15),
            new Vector3(-30,0,-2), new Vector3(9,0,0), new Vector3(28,0,10),
            new Vector3(12,0,16), new Vector3(-7,0,10), new Vector3(0,0,19),
            new Vector3(1,0,22)
        };
        route.Clear(); routeLength = 0;
        for (int i = 0; i < pts.Length - 1; i++)
        for (int s = 0; s < 24; s++)
        {
            float t = s / 24f;
            Vector3 p = Catmull(pts[Mathf.Max(0, i - 1)], pts[i], pts[i + 1], pts[Mathf.Min(pts.Length - 1, i + 2)], t);
            p.y = H(p.x, p.z);
            if (route.Count > 0) routeLength += Vector3.Distance(route[route.Count - 1], p);
            route.Add(p);
        }
        var last = pts[pts.Length - 1]; last.y = H(last.x, last.z);
        routeLength += Vector3.Distance(route[route.Count - 1], last); route.Add(last);
    }
    static float DistPath(float x, float z)
    {
        float best = float.MaxValue;
        var p = new Vector2(x, z);
        // Sampling every third point saves CPU in large vegetation placement loops.
        for (int i = 0; i < route.Count; i += 3)
        {
            var q = new Vector2(route[i].x, route[i].z);
            float d = (p - q).sqrMagnitude; if (d < best) best = d;
        }
        return Mathf.Sqrt(best);
    }
    static void MakePath()
    {
        var group = Group("WINDING PATH | continuous walkable ribbon", root);
        int n = route.Count;
        var verts = new Vector3[n * 2]; var uv = new Vector2[n * 2]; var tris = new int[(n - 1) * 6];
        float distance = 0;
        for (int i = 0; i < n; i++)
        {
            Vector3 p = route[i];
            if (i > 0) distance += Vector3.Distance(route[i - 1], p);
            Vector3 prev = route[Mathf.Max(0, i - 1)], next = route[Mathf.Min(n - 1, i + 1)];
            Vector3 tangent = next - prev; tangent.y = 0; tangent.Normalize();
            Vector3 side = new Vector3(-tangent.z, 0, tangent.x);
            float width = 2.2f + .3f * Mathf.Sin(i * .3f);
            for (int j = 0; j < 2; j++)
            {
                Vector3 v = p + side * (j == 0 ? -width * .5f : width * .5f);
                v.y = H(v.x, v.z) + .045f; verts[i * 2 + j] = v;
                uv[i * 2 + j] = new Vector2(j, distance * .25f);
            }
            if (i < n - 1)
            {
                int k = i * 6, a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris[k] = a; tris[k + 1] = c; tris[k + 2] = b;
                tris[k + 3] = b; tris[k + 4] = c; tris[k + 5] = d;
            }
        }
        var mesh = new Mesh { name = "CurvedForestPath", indexFormat = IndexFormat.UInt32 };
        mesh.vertices = verts; mesh.uv = uv; mesh.triangles = tris; mesh.RecalculateNormals();
        AssetDatabase.CreateAsset(mesh, Output + "/Meshes/CurvedForestPath.asset");
        var obj = new GameObject("Walkable trampled earth"); obj.transform.SetParent(group, false);
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        obj.AddComponent<MeshRenderer>().sharedMaterial = mats["PathEarth"];
        // Ground terrain collider supports walking; path ribbon itself is visual only.
    }
    // Measure model geometry in the upright placement frame before applying random yaw.
    // Lower LODs, particles and inactive helper objects must not change its reference size.
    static bool TryModelBounds(GameObject model, Transform frame, out Bounds bounds)
    {
        bounds = new Bounds(); bool has = false;
        var managed = new HashSet<Renderer>(); var firstLOD = new HashSet<Renderer>();
        foreach (var group in model.GetComponentsInChildren<LODGroup>(true))
        {
            LOD[] lods = group.GetLODs();
            foreach (var lod in lods) foreach (var renderer in lod.renderers)
                if (renderer != null) managed.Add(renderer);
            if (lods.Length > 0) foreach (var renderer in lods[0].renderers)
                if (renderer != null) firstLOD.Add(renderer);
        }
        foreach (var renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            if (!renderer.gameObject.activeInHierarchy) continue;
            if (managed.Contains(renderer) && !firstLOD.Contains(renderer)) continue;
            if (!renderer.enabled && !firstLOD.Contains(renderer)) continue;
            Bounds local;
            if (renderer is SkinnedMeshRenderer skinned) local = skinned.localBounds;
            else if (renderer is MeshRenderer)
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                local = filter.sharedMesh.bounds;
            }
            else continue;
            Matrix4x4 toFrame = frame.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 p = local.center + Vector3.Scale(local.extents, new Vector3(
                    (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                p = toFrame.MultiplyPoint3x4(p);
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z)) return false;
                if (!has) { bounds = new Bounds(p, Vector3.zero); has = true; }
                else bounds.Encapsulate(p);
            }
        }
        return has;
    }
    static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    static bool TryFitScale(Vector3 size, float targetHeight, float maxFootprint, out float scale)
    {
        scale = 0;
        double footprint = Math.Sqrt((double)size.x * size.x + (double)size.z * size.z);
        if (!Finite(size.y) || targetHeight <= 0 || (size.y <= 0 && footprint <= 0)) return false;
        double fit = size.y > 0 ? targetHeight / (double)size.y : double.PositiveInfinity;
        // Cap the diagonal horizontal span; uniformly scale all axes to preserve proportions.
        if (maxFootprint > 0 && footprint > 0) fit = Math.Min(fit, maxFootprint / footprint);
        scale = (float)fit;
        return Finite(scale) && scale > 0;
    }
    static GameObject Place(GameObject prefab, Transform parent, string name, float x, float z,
        float targetHeight, bool tintWillow = false, int willowColor = 0, float maxFootprint = 0)
    {
        GameObject model = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (model == null) return null;
        // Keep the imported prefab's axis correction (often +/-90 degrees on FBX roots).
        // A separate Y-up placement parent rotates around the forest's vertical axis.
        // It also keeps the trunk collider upright.
        Vector3 sourcePosition = prefab.transform.localPosition;
        Quaternion sourceRotation = prefab.transform.localRotation;
        Vector3 sourceScale = prefab.transform.localScale;
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.position = new Vector3(x, H(x, z), z);
        obj.transform.rotation = Quaternion.identity;
        model.transform.SetParent(obj.transform, false);
        model.transform.localPosition = sourcePosition;
        model.transform.localRotation = sourceRotation;
        model.transform.localScale = sourceScale;
        Bounds bounds; float factor;
        if (!TryModelBounds(model, obj.transform, out bounds) ||
            !TryFitScale(bounds.size, targetHeight, maxFootprint, out factor))
        {
            Debug.LogWarning("Amber Ruins: skipped " + prefab.name + " because its model bounds cannot be sized safely.", prefab);
            UnityEngine.Object.DestroyImmediate(obj); return null;
        }
        // No minimum shrink factor: centimeter/millimeter imports can need very small scales.
        obj.transform.localScale = Vector3.one * factor;
        obj.transform.rotation = Quaternion.Euler(0, R(0, 360), 0);
        float bottom = obj.transform.TransformPoint(new Vector3(bounds.center.x, bounds.min.y, bounds.center.z)).y;
        model.transform.position += Vector3.up * (H(x, z) - bottom - .08f);
        // Retain useful authored primitive collision on this instance only.
        // Avoid cooking the detailed render mesh as a physics mesh.
        foreach (Collider c in obj.GetComponentsInChildren<Collider>(true))
        {
            if (c.isTrigger || !(c is BoxCollider || c is SphereCollider || c is CapsuleCollider))
                UnityEngine.Object.DestroyImmediate(c);
            else c.enabled = true;
        }
        foreach (Rigidbody b in obj.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(b);
        foreach (Animator a in obj.GetComponentsInChildren<Animator>(true)) a.enabled = false;
        foreach (Light l in obj.GetComponentsInChildren<Light>(true)) l.enabled = false;
        foreach (Renderer renderer in obj.GetComponentsInChildren<Renderer>(true)) {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.sharedMaterials = CompatibleMaterials(renderer.sharedMaterials);
        }
        // Palette is already defined by the user's tree materials. Do not retint whole tree models.
        // tintWillow retained in signature for compatibility with previous builder calls.
        return obj;
    }
    static Material[] CompatibleMaterials(Material[] source)
    {
        var result = (Material[])source.Clone();
        for (int i = 0; i < source.Length; i++)
        {
            Material original = source[i]; if (original == null) continue;
            string tag = original.shader != null ? original.GetTag("RenderPipeline", false, "") : "";
            bool wrong = original.shader == null || !original.shader.isSupported ||
                (urp && tag != "UniversalPipeline") || (!urp && tag != "");
            if (!wrong) continue;
            if (compatibleMats.TryGetValue(original, out Material cached)) { result[i] = cached; continue; }
            Shader shader = Shader.Find(urp ? "Universal Render Pipeline/Lit" : "Standard");
            var copy = new Material(shader) { name = original.name + "_LiteCompatible", enableInstancing = true };
            Texture texture = original.HasProperty("_BaseMap") ? original.GetTexture("_BaseMap") : original.mainTexture;
            Color color = original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") : original.HasProperty("_Color") ? original.color : Color.white;
            copy.color = color; copy.mainTexture = texture;
            if (urp) { copy.SetTexture("_BaseMap", texture); copy.SetColor("_BaseColor", color); }
            if (texture != null)
            {
                string prop = original.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
                copy.mainTextureScale = original.GetTextureScale(prop); copy.mainTextureOffset = original.GetTextureOffset(prop);
            }
            foreach (string prop in new[] { "_BumpMap", "_MetallicGlossMap", "_OcclusionMap", "_EmissionMap" })
                if (original.HasProperty(prop) && copy.HasProperty(prop)) copy.SetTexture(prop, original.GetTexture(prop));
            if (original.HasProperty("_BumpMap") && original.GetTexture("_BumpMap") != null) copy.EnableKeyword("_NORMALMAP");
            if (original.HasProperty("_MetallicGlossMap") && original.GetTexture("_MetallicGlossMap") != null) copy.EnableKeyword("_METALLICGLOSSMAP");
            foreach (string prop in new[] { "_Metallic", "_Glossiness", "_Smoothness", "_Cutoff", "_BumpScale" })
                if (original.HasProperty(prop) && copy.HasProperty(prop)) copy.SetFloat(prop, original.GetFloat(prop));
            bool cutout = original.IsKeywordEnabled("_ALPHATEST_ON") || original.renderQueue == 2450 ||
                (original.shader != null && (original.shader.name.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0 || original.shader.name.IndexOf("Leaf", StringComparison.OrdinalIgnoreCase) >= 0));
            if (cutout) SetCutout(copy, .35f);
            if (original.IsKeywordEnabled("_EMISSION") && original.HasProperty("_EmissionColor"))
            { copy.EnableKeyword("_EMISSION"); copy.SetColor("_EmissionColor", original.GetColor("_EmissionColor")); }
            // A small material asset shares the original texture references; it does not duplicate texture files.
            AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(Output + "/Materials/Compatible.mat"));
            compatibleMats[original] = copy; result[i] = copy;
        }
        return result;
    }
    static void SetCutout(Material material, float cutoff)
    {
        material.SetFloat("_Cutoff", cutoff); material.SetFloat("_ZWrite", 1);
        material.SetFloat("_SrcBlend", (float)BlendMode.One); material.SetFloat("_DstBlend", (float)BlendMode.Zero);
        material.EnableKeyword("_ALPHATEST_ON"); material.DisableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON"); material.renderQueue = 2450;
        material.SetOverrideTag("RenderType", "TransparentCutout");
        if (urp) { material.SetFloat("_AlphaClip", 1); material.SetFloat("_Surface", 0); material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT"); }
        else material.SetFloat("_Mode", 1);
    }
    static bool TreeGeometry(Renderer renderer)
    { return renderer != null && (renderer is MeshRenderer || renderer is SkinnedMeshRenderer); }
    static void PrepareTreeLOD(GameObject obj, bool landmark)
    {
        var all = obj.GetComponentsInChildren<Renderer>(true);
        var near = new HashSet<Renderer>(); var middle = new HashSet<Renderer>();
        var managed = new HashSet<Renderer>(); bool hasMiddle = false;
        foreach (var old in obj.GetComponentsInChildren<LODGroup>(true))
        {
            LOD[] lods = old.GetLODs();
            foreach (var lod in lods) foreach (var renderer in lod.renderers)
                if (renderer != null) managed.Add(renderer);
            var localNear = new HashSet<Renderer>();
            if (lods.Length > 0) foreach (var renderer in lods[0].renderers)
                if (TreeGeometry(renderer)) { near.Add(renderer); localNear.Add(renderer); }
            // The final source LOD can itself be a billboard. Reuse an interior
            // mesh LOD only, and never add a generated silhouette/card.
            bool localMiddle = false;
            if (lods.Length > 2) foreach (var renderer in lods[1].renderers)
                if (TreeGeometry(renderer)) { middle.Add(renderer); localMiddle = true; }
            if (localMiddle) hasMiddle = true;
            else middle.UnionWith(localNear);
            UnityEngine.Object.DestroyImmediate(old);
        }
        foreach (var renderer in all)
            if (TreeGeometry(renderer) && !managed.Contains(renderer)) { near.Add(renderer); middle.Add(renderer); }
        if (near.Count == 0) foreach (var renderer in all)
            if (TreeGeometry(renderer)) { near.Add(renderer); middle.Add(renderer); }
        foreach (var renderer in all)
        {
            renderer.enabled = near.Contains(renderer) || middle.Contains(renderer);
            renderer.shadowCastingMode = landmark ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }
        Renderer[] n = new List<Renderer>(near).ToArray(), m = new List<Renderer>(middle).ToArray();
        var group = obj.AddComponent<LODGroup>(); group.fadeMode = LODFadeMode.None;
        // Keep actual tree meshes through the fog. The tiny cull threshold is
        // well beyond the fog range, rather than switching to a black card.
        if (hasMiddle) group.SetLODs(new[] { new LOD(landmark ? .4f : .32f, n), new LOD(.005f, m) });
        else group.SetLODs(new[] { new LOD(.005f, n) });
        group.RecalculateBounds();
    }
    static bool HasSolidCollider(GameObject obj)
    {
        foreach (var c in obj.GetComponentsInChildren<Collider>(true))
            if (c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy) return true;
        return false;
    }
    static void AddPropCollider(GameObject obj)
    {
        if (HasSolidCollider(obj)) return;
        if (!TryModelBounds(obj, obj.transform, out Bounds bounds)) return;
        var c = obj.AddComponent<BoxCollider>(); c.center = bounds.center;
        float minimum = .08f / obj.transform.lossyScale.y;
        c.size = Vector3.Max(bounds.size, Vector3.one * minimum); c.isTrigger = false;
    }
    static void AddTreeCollider(GameObject obj, GameObject prefab)
    {
        if (HasSolidCollider(obj)) return;
        if (!TryModelBounds(obj, obj.transform, out Bounds bounds)) return;
        float scale = obj.transform.lossyScale.y;
        if (!trunkFits.TryGetValue(prefab, out Vector3 fit))
        {
            // Read only the lower trunk region once per source prefab. Editor
            // mesh access does not require changing the source import settings.
            var near = new HashSet<Renderer>(); var managed = new HashSet<Renderer>();
            foreach (var group in obj.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                foreach (var lod in lods) foreach (var r in lod.renderers) if (r != null) managed.Add(r);
                if (lods.Length > 0) foreach (var r in lods[0].renderers) if (r != null) near.Add(r);
            }
            bool has = false; Bounds band = new Bounds();
            float top = bounds.min.y + bounds.size.y * .12f;
            foreach (var filter in obj.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (filter.sharedMesh == null || renderer == null || !renderer.enabled ||
                    !filter.gameObject.activeInHierarchy || (managed.Contains(renderer) && !near.Contains(renderer))) continue;
                Matrix4x4 toFrame = obj.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                try
                {
                    Vector3[] vertices = filter.sharedMesh.vertices;
                    int stride = Mathf.Max(1, vertices.Length / 12000);
                    for (int i = 0; i < vertices.Length; i += stride)
                    {
                        Vector3 p = toFrame.MultiplyPoint3x4(vertices[i]);
                        if (p.y > top || !Finite(p.x) || !Finite(p.z)) continue;
                        if (!has) { band = new Bounds(p, Vector3.zero); has = true; }
                        else band.Encapsulate(p);
                    }
                }
                catch (UnityException) { /* Fall back to geometry bounds below. */ }
            }
            fit = has ? new Vector3(band.center.x, band.center.z, Mathf.Max(band.extents.x, band.extents.z)) :
                new Vector3(bounds.center.x, bounds.center.z, .38f / scale);
            trunkFits[prefab] = fit;
        }
        float radius = Mathf.Clamp(fit.z * scale, .18f, 1.05f);
        float height = Mathf.Max(radius * 2, Mathf.Min(bounds.size.y * scale, 4f));
        var c = obj.AddComponent<CapsuleCollider>(); c.direction = 1;
        c.radius = radius / scale; c.height = height / scale;
        c.center = new Vector3(fit.x, bounds.min.y + height / (2 * scale), fit.y);
        c.isTrigger = false;
    }
    static void RemoveFoliageColliders(GameObject obj)
    {
        foreach (var c in obj.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
    }
    static void CullSmall(GameObject obj,float distance)
    {var detail=obj.AddComponent<AmberRuinsLiteDetail>();detail.renderDistance=distance;}
    static void MakeSafeDetailSettings()
    {
        // Only object settings in this scene are changed. Global quality and the project's URP asset are untouched.
        foreach(var light in root.GetComponentsInChildren<Light>(true))if(light.type!=LightType.Directional)light.shadows=LightShadows.None;
    }
    static void MakeCrimsonSky()
    {
        string path = Output + "/Materials/CrimsonCloudSky.shader";
        string source =
@"Shader ""AmberRuinsLite/CrimsonCloudSky__NAME__"" {
 Properties {
  _Horizon(""Fog horizon"",Color)=(.24,.15,.19,1)
  _Crimson(""Crimson sky"",Color)=(.47,.16,.23,1)
  _Zenith(""Plum zenith"",Color)=(.094,.055,.133,1)
  _Cloud(""Cloud color"",Color)=(.15,.098,.137,1)
 }
 SubShader {
  Tags { ""Queue""=""Background"" ""RenderType""=""Background"" ""PreviewType""=""Skybox"" __TAG__ }
  Cull Off ZWrite Off
  Pass {
   PROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma target 3.0
   INCLUDE
   CONSTANTSTART
   float4 _Horizon, _Crimson, _Zenith, _Cloud;
   CONSTANTEND
   struct A { float4 p:POSITION; };
   struct V { float4 p:SV_POSITION; float3 direction:TEXCOORD0; };
   V vert(A a) { V v; v.p=CLIPPOSITION; v.direction=a.p.xyz; return v; }
   float hash(float2 p) {
    float3 q=frac(float3(p.x,p.y,p.x)*.1031);
    q+=dot(q,q.yzx+33.33);
    return frac((q.x+q.y)*q.z);
   }
   float valueNoise(float2 p) {
    float2 i=floor(p), f=frac(p), u=f*f*(3-2*f);
    return lerp(lerp(hash(i),hash(i+float2(1,0)),u.x),
                lerp(hash(i+float2(0,1)),hash(i+float2(1,1)),u.x),u.y);
   }
   half4 frag(V v):SV_Target {
    float3 d=normalize(v.direction);
    float altitude=saturate(d.y);
    float3 color=lerp(_Horizon.rgb,_Crimson.rgb,smoothstep(0,.22,altitude));
    color=lerp(color,_Zenith.rgb,smoothstep(.18,.92,altitude));
    float2 p=d.xz/max(.35,d.y+.55)*3.4;
    p+=float2(_Time.y*.002,_Time.y*.0007);
    float clouds=valueNoise(p)*.55+valueNoise(p*2.03+7.1)*.28+
                 valueNoise(p*4.07+19.3)*.14;
    float cover=smoothstep(.33,.73,clouds)*smoothstep(.025,.14,altitude);
    color=lerp(color,_Cloud.rgb,cover*.78);
    // A soft wine-colored glow surrounds the existing moon mesh.
    float glow=pow(saturate(dot(d,normalize(float3(8,105,178)))),12);
    color+=_Crimson.rgb*glow*.12*smoothstep(0,.12,altitude);
    return half4(color,1);
   }
   ENDPROGRAM
  }
 }
}";
        source = source.Replace("__NAME__", urp ? "URP" : "BuiltIn");
        source = source.Replace("__TAG__",
            urp ? "\"RenderPipeline\"=\"UniversalPipeline\"" : "");
        // Per-pipeline includes keep this shader independent of package installs in Built-in projects.
        source = source.Replace("ENDPROGRAM", urp ? "ENDHLSL" : "ENDCG")
            .Replace("PROGRAM", urp ? "HLSLPROGRAM" : "CGPROGRAM")
            .Replace("INCLUDE", urp ?
                "#include \"Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl\"" :
                "#include \"UnityCG.cginc\"")
            .Replace("CONSTANTSTART", urp ? "CBUFFER_START(UnityPerMaterial)" : "")
            .Replace("CONSTANTEND", urp ? "CBUFFER_END" : "")
            .Replace("CLIPPOSITION", urp ? "TransformObjectToHClip(a.p.xyz)" : "UnityObjectToClipPos(a.p)");
        File.WriteAllText(path, source);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        Shader shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
        if (shader == null) throw new InvalidOperationException("Crimson sky shader import failed.");
        var material = new Material(shader) { name = "CrimsonCloudSky" };
        material.SetColor("_Horizon", RenderSettings.fogColor);
        material.SetColor("_Crimson", Hex("#772A3B"));
        material.SetColor("_Zenith", Hex("#180E22"));
        material.SetColor("_Cloud", Hex("#271923"));
        AssetDatabase.CreateAsset(material, Output + "/Materials/CrimsonCloudSky.mat");
        RenderSettings.skybox = material;
    }
    static Shader SkyShader()
    {
        string path=Output+"/Materials/BloodMoonFogFree.shader";
        string shader=urp ?
@"Shader ""AmberRuinsLite/SkyURP"" {
 Properties{ _Color(""Color"",Color)=(.7,.12,.2,1) _MainTex(""Moon texture"",2D)=""white""{} }
 SubShader{ Tags{""RenderPipeline""=""UniversalPipeline"" ""RenderType""=""Opaque""} Pass{
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""
 struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
 TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);CBUFFER_START(UnityPerMaterial) float4 _Color; CBUFFER_END
 V vert(A a){V v;v.p=TransformObjectToHClip(a.p.xyz);v.uv=a.uv;return v;}
 half4 frag(V v):SV_Target{return SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv)*_Color;}
 ENDHLSL
 }} }" :
@"Shader ""AmberRuinsLite/SkyBuiltIn"" {
 Properties{ _Color(""Color"",Color)=(.7,.12,.2,1) _MainTex(""Moon texture"",2D)=""white""{} }
 SubShader{ Tags{""RenderType""=""Opaque""} Pass{
 CGPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include ""UnityCG.cginc""
 struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};sampler2D _MainTex;float4 _Color;
 V vert(appdata_base a){V v;v.p=UnityObjectToClipPos(a.vertex);v.uv=a.texcoord;return v;}
 fixed4 frag(V v):SV_Target{return tex2D(_MainTex,v.uv)*_Color;}
 ENDCG
 }} }";
        File.WriteAllText(path,shader);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        Shader result=AssetDatabase.LoadAssetAtPath<Shader>(path);if(result==null)throw new InvalidOperationException("Sky shader import failed.");return result;
    }
    static bool SanctuaryClear(float x, float z, float radius)
    { return new Vector2(x - 1f, z - 22f).magnitude > radius; }
    static bool TreeTooClose(List<Vector2> positions, Vector2 candidate)
    {
        foreach (var position in positions)
            if ((position - candidate).sqrMagnitude < TreeSpacing * TreeSpacing) return true;
        return false;
    }
    static void MakeForest()
    {
        Transform forest = Group("FOREST | existing detailed tree assets", root);
        Transform floor = Group("NEARBY FLOOR DETAILS | distance culled", root);
        var treePositions = new List<Vector2>();
        // Landmark trees frame the path and sanctuary. There are no new detailed tree meshes.
        int[] samples = { 16, 58, 106, 148, 186, 224, 259 };
        for (int k = 0; k < samples.Length; k++)
        {
            int i = Mathf.Min(route.Count - 3, samples[k]); Vector3 p = route[i];
            Vector3 t = route[i + 2] - route[Mathf.Max(0, i - 2)]; t.y = 0; t.Normalize();
            Vector3 side = new Vector3(-t.z, 0, t.x);
            Vector3 pos = p + side * (k % 2 == 0 ? 7.8f : -7.8f);
            if (DistPath(pos.x,pos.z) < PathTreeClearance || !SanctuaryClear(pos.x, pos.z, 11f) ||
                TreeTooClose(treePositions, new Vector2(pos.x, pos.z))) continue;
            var obj = Place(willows[k % willows.Length],forest,"LANDMARK WILLOW | existing prefab",pos.x,pos.z,R(12,16),maxFootprint:18f);
            if (obj == null) continue;
            PrepareTreeLOD(obj,true); AddTreeCollider(obj,willows[k % willows.Length]); CullSmall(obj,50f);
            treePositions.Add(new Vector2(pos.x,pos.z)); foregroundTrees++;
        }
        // Keep three existing willow landmarks around the final moon-and-portal reveal.
        var framing = new[] { new Vector2(-14, 23), new Vector2(15, 25), new Vector2(0, 37) };
        for (int i = 0; i < framing.Length; i++)
        {
            var pos = framing[i];
            if (TreeTooClose(treePositions, pos)) continue;
            var obj = Place(willows[i % willows.Length], forest, "SANCTUARY WILLOW | existing prefab", pos.x, pos.y, R(14,17),maxFootprint:18f);
            if (obj == null) continue;
            PrepareTreeLOD(obj, true); AddTreeCollider(obj,willows[i % willows.Length]); CullSmall(obj,50f); treePositions.Add(pos); foregroundTrees++;
        }
        // A little more breathing room: 5m between tree centers, fewer supporting trees.
        for (int tries=0, placed=0; tries<2200 && placed<TreeBudget; tries++)
        {
            float x=R(-38,38), z=R(-38,38), d=DistPath(x,z);
            if (d<PathTreeClearance || d>15f || !SanctuaryClear(x,z,12f)) continue;
            if (TreeTooClose(treePositions, new Vector2(x,z))) continue;
            var prefab=trees[rng.Next(trees.Length)];
            var obj=Place(prefab,forest,"Tree | referenced " + prefab.name,x,z,R(8f,13f),maxFootprint:12f);
            if(obj==null)continue;
            PrepareTreeLOD(obj,false);AddTreeCollider(obj,prefab);CullSmall(obj,50f);treePositions.Add(new Vector2(x,z));placed++;foregroundTrees++;
        }
        for(int tries=0,placed=0;grasses.Length>0 && tries<2200 && placed<GrassBudget;tries++)
        {
            int i=rng.Next(route.Count-20);Vector3 p=route[i];Vector3 t=(route[Mathf.Min(i+2,route.Count-1)]-route[Mathf.Max(0,i-2)]).normalized;
            Vector3 side=new Vector3(-t.z,0,t.x);Vector3 pos=p+side*(rng.Next(2)==0?-1:1)*R(1.7f,4.4f);
            if(DistPath(pos.x,pos.z)<1.45f || !SanctuaryClear(pos.x,pos.z,8f))continue;
            var obj=Place(grasses[rng.Next(grasses.Length)],floor,"Path-edge grass / fern",pos.x,pos.z,R(.35f,.8f),maxFootprint:1.6f);
            if(obj==null)continue;RemoveFoliageColliders(obj);CullSmall(obj,18f);placed++;floorDetails++;
        }
        for(int i=0;i<UndergrowthBudget && undergrowth.Length>0;i++)
        {
            var p=route[rng.Next(route.Count-15)];float x=p.x+R(-5,5),z=p.z+R(-5,5);
            if(DistPath(x,z)<2f || !SanctuaryClear(x,z,8f))continue;
            var obj=Place(undergrowth[rng.Next(undergrowth.Length)],floor,"Near-path shrub / flower / mushroom",x,z,R(.45f,1.2f),maxFootprint:2.2f);
            if(obj!=null){RemoveFoliageColliders(obj);CullSmall(obj,22f);floorDetails++;}
        }
        for(int i=0;i<RockBudget && rocks.Length>0;i++)
        {
            var p=route[rng.Next(route.Count)];float x=p.x+R(-5,5),z=p.z+R(-5,5);
            if(DistPath(x,z)<2.3f || !SanctuaryClear(x,z,8f))continue;
            var obj=Place(rocks[rng.Next(rocks.Length)],floor,"Mossy rock | existing model",x,z,R(.6f,1.5f),maxFootprint:2.4f);
            if(obj!=null){AddPropCollider(obj);CullSmall(obj,35f);floorDetails++;}
        }
    }

    static GameObject Stone(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, int seed)
    {
        // Irregular beveled-looking stone geometry rather than Unity cube primitives.
        var obj = new GameObject(name); obj.transform.SetParent(parent, false);
        obj.transform.localPosition = pos;
        var vertices = new Vector3[8];
        float hx = size.x / 2, hy = size.y / 2, hz = size.z / 2;
        var corners = new[] {
            new Vector3(-hx,-hy,-hz),new Vector3(hx,-hy,-hz),new Vector3(hx,-hy,hz),new Vector3(-hx,-hy,hz),
            new Vector3(-hx,hy,-hz),new Vector3(hx,hy,-hz),new Vector3(hx,hy,hz),new Vector3(-hx,hy,hz)
        };
        var local = new System.Random(seed);
        for (int i = 0; i < 8; i++)
            vertices[i] = corners[i] + new Vector3(((float)local.NextDouble() - .5f) * size.x * .12f,
                ((float)local.NextDouble() - .5f) * size.y * .07f,
                ((float)local.NextDouble() - .5f) * size.z * .12f);
        int[] triangles = { 0,4,1, 1,4,5, 1,5,2, 2,5,6, 2,6,3, 3,6,7,
                            3,7,0, 0,7,4, 4,7,5, 5,7,6, 3,0,2, 2,0,1 };
        var mesh = new Mesh { name = "IrregularStone" };
        var uv=new Vector2[vertices.Length];for(int i=0;i<uv.Length;i++)uv[i]=new Vector2(vertices[i].x,vertices[i].z);
        mesh.vertices = vertices; mesh.uv=uv; mesh.triangles = triangles; mesh.RecalculateNormals(); mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh,AssetDatabase.GenerateUniqueAssetPath(Output+"/Meshes/Stone_"+seed+".asset"));
        // Persist the small custom stone mesh so it survives scene save and reopen.
        obj.AddComponent<MeshFilter>().sharedMesh = mesh;
        obj.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var collider = obj.AddComponent<BoxCollider>();
        collider.center = mesh.bounds.center; collider.size = mesh.bounds.size;
        return obj;
    }
    static void MakeSanctuary()
    {
        var sanctuary = Group("FORGOTTEN SANCTUARY | editable ruins", root);
        sanctuary.position = new Vector3(1, H(1, 22), 22);
        var paving = Group("Cracked circular stone platform", sanctuary);
        for (int ring = 0; ring < 4; ring++)
        {
            int count = 10 + ring * 12; float radius = 1.3f + ring * 1.65f;
            for (int i = 0; i < count; i++)
            {
                float a = 2 * Mathf.PI * i / count;
                var slab = Stone(paving, "Uneven ancient paving", new Vector3(Mathf.Sin(a) * radius, .1f, Mathf.Cos(a) * radius),
                    new Vector3(1.15f + ring * .15f, .24f, 1.3f),
                    i % 4 == 0 ? mats["ShadowStone"] : mats["WeatheredStone"], ring * 100 + i);
                slab.transform.localRotation = Quaternion.Euler(R(-2,2), -a * Mathf.Rad2Deg, R(-2,2));
            }
        }
        for (int i = 0; i < 6; i++)
        {
            float a = i * Mathf.PI / 3f;
            float x = Mathf.Sin(a) * 7.3f, z = Mathf.Cos(a) * 7.3f;
            GameObject p = ruins[i % ruins.Length];
            // Prefab is placed in world coordinates, then kept under sanctuary.
            GameObject obj = Place(p, sanctuary, "BROKEN PILLAR | " + p.name, 1 + x, 22 + z, R(2.8f, 4.8f),maxFootprint:3.2f);
            if (obj != null) {
                obj.transform.rotation = Quaternion.Euler(R(-4, 4), R(0, 360), R(-4, 4));
                AddPropCollider(obj);
            }
        }
        for (int i = 0; i < 16; i++)
        {
            float a = R(0, 2 * Mathf.PI), radius = R(7.8f, 11.5f);
            var piece = Stone(sanctuary, "Ancient fallen stone", new Vector3(Mathf.Cos(a) * radius, .1f, Mathf.Sin(a) * radius),
                new Vector3(R(.35f,1.1f), R(.22f,.6f), R(.4f,1.4f)),
                i % 2 == 0 ? mats["ShadowStone"] : mats["WeatheredStone"], 500 + i);
            piece.transform.localRotation = Quaternion.Euler(R(-20,20), R(0,360), R(-20,20));
        }
        // Portal is a distinct hierarchy; teammates can disable the barrier GameObject.
        var portal = Group("PORTAL | ancient stone gateway", sanctuary);
        portal.localPosition = new Vector3(0, .1f, 2.8f);
        for (int side = -1; side <= 1; side += 2)
        {
            for (int j = 0; j < 6; j++)
                Stone(portal, "Weathered arch jamb stone", new Vector3(side * 1.7f, .37f + j * .68f, 0),
                    new Vector3(.85f, .72f, 1.1f), j % 2 == 0 ? mats["ShadowStone"] : mats["WeatheredStone"], 700 + side * 10 + j);
        }
        for (int i = 0; i < 7; i++)
        {
            float x = -1.95f + i * .65f;
            Stone(portal, "Weathered arch lintel stone", new Vector3(x, 4.6f, 0),
                new Vector3(.73f, .92f, 1.1f), i % 2 == 0 ? mats["ShadowStone"] : mats["WeatheredStone"], 900 + i);
        }
        var barrier = Group("PORTAL BARRIER | disable on puzzle success", portal);
        barrier.localPosition = new Vector3(0, 2.12f, 0);
        barrier.gameObject.AddComponent<BoxCollider>().size = new Vector3(2.95f, 3.7f, .35f);
        var seal = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        seal.name = "Translucent amber magical seal"; seal.transform.SetParent(barrier, false);
        seal.transform.localScale = new Vector3(2.95f, 3.7f, .12f);
        seal.GetComponent<Renderer>().sharedMaterial = mats["PortalAmber"];
        UnityEngine.Object.DestroyImmediate(seal.GetComponent<Collider>());
        var glow = new GameObject("Amber portal light"); glow.transform.SetParent(barrier, false);
        glow.transform.localPosition = new Vector3(0, 0, -1);
        Light l = glow.AddComponent<Light>(); l.type = LightType.Point;
        l.color = Hex("#FFB347"); l.intensity = 2.3f; l.range = 13;
        for (int i = 0; i < 28; i++)
        {
            float a = i * Mathf.PI * 2 / 28f;
            var rune = Stone(sanctuary, "Glowing ritual glyph", new Vector3(Mathf.Sin(a) * 5.5f, .25f, Mathf.Cos(a) * 5.5f),
                new Vector3(.12f, .06f, .33f), mats["AmberRune"], 1000 + i);
            rune.transform.localRotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg, 0);
            UnityEngine.Object.DestroyImmediate(rune.GetComponent<Collider>());
        }
        for (int i = 0; i < 5; i++)
            Stone(sanctuary, "Weathered entry step", new Vector3(0, -.03f - i * .09f, -7.5f - i * .75f),
                new Vector3(3.5f + i * .22f, .28f, .78f), mats["WeatheredStone"], 1100 + i);
    }
    static void MakeLightingAndMoon()
    {
        var atmosphere = Group("BLOOD MOON | sky and lighting", root);
        var moon = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        moon.name = "Enormous crimson blood moon | visual"; moon.transform.SetParent(atmosphere, false);
        // From sanctuary, the moon appears high beyond the archway.
        moon.transform.position = new Vector3(8, 105, 178);
        moon.transform.localScale = Vector3.one * 64;
        moon.GetComponent<Renderer>().sharedMaterial = mats["MoonCrimson"];
        moon.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        UnityEngine.Object.DestroyImmediate(moon.GetComponent<Collider>());
        var moonMaterial=new Material(Shader.Find(urp?"Universal Render Pipeline/Unlit":"Unlit/Color"));
        moonMaterial.name="BloodMoon_FogFree";moonMaterial.color=new Color(.72f,.12f,.2f);
        if(moonMaterial.HasProperty("_BaseColor"))moonMaterial.SetColor("_BaseColor",new Color(.72f,.12f,.2f));
        // Built-in Unlit/Color ignores scene fog. URP sky fog is handled by a tiny dedicated shader below.
        moonMaterial.shader=SkyShader();
        var moonTexture = new Texture2D(256, 128, TextureFormat.RGBA32, true);
        moonTexture.name = "LunarSurface";
        for(int y=0;y<128;y++)for(int x=0;x<256;x++)
        {
            float u=x/256f,v=y/128f;
            float n=Mathf.PerlinNoise(u*12,v*12)*.52f+Mathf.PerlinNoise(u*39+7,v*39+3)*.22f;
            float maria=Mathf.SmoothStep(.28f,.8f,Mathf.PerlinNoise(u*5+18,v*6+2));
            float shade=Mathf.Clamp01(.38f+n-maria*.24f);
            moonTexture.SetPixel(x,y,new Color(shade,shade*.93f,shade*.88f,1));
        }
        moonTexture.Apply();AssetDatabase.CreateAsset(moonTexture,Output+"/Textures/LunarSurface.asset");
        moonMaterial.mainTexture=moonTexture;
        AssetDatabase.CreateAsset(moonMaterial,Output+"/Materials/BloodMoon_FogFree.mat");
        moon.GetComponent<Renderer>().sharedMaterial=moonMaterial;
        var light = new GameObject("Crimson moonlight"); light.transform.SetParent(atmosphere, false);
        light.transform.rotation = Quaternion.Euler(29, -34, 0);
        var directional = light.AddComponent<Light>(); directional.type = LightType.Directional;
        directional.color = new Color(.69f, .26f, .32f); directional.intensity = .67f;
        directional.shadows = LightShadows.Hard; directional.shadowResolution = LightShadowResolution.Low;
        directional.shadowBias=.08f;directional.shadowNormalBias=.25f;
        var amber = new GameObject("Sanctuary amber fill light"); amber.transform.SetParent(atmosphere, false);
        amber.transform.position = new Vector3(1, 5, 22);
        var point = amber.AddComponent<Light>(); point.type = LightType.Point;
        point.color = Hex("#FFB347"); point.range = 14; point.intensity = 2f; point.shadows = LightShadows.None;
    }
    static void MakeParticles()
    {
        var fx=Group("MAGIC | 224 particles maximum",root);
        Shader particleShader=Shader.Find(urp?"Universal Render Pipeline/Particles/Unlit":"Particles/Standard Unlit");
        if(particleShader==null)throw new InvalidOperationException("Particle shader was not found in the active pipeline.");
        Material pm=new Material(particleShader);
        pm.name="AmberMotes";pm.SetColor("_Color",new Color(1,.68f,.2f,.85f));
        if(urp){pm.SetFloat("_Surface",1);pm.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");}
        pm.SetFloat("_ZWrite",0);pm.SetFloat("_SrcBlend",(float)BlendMode.SrcAlpha);pm.SetFloat("_DstBlend",(float)BlendMode.One);
        pm.renderQueue=3000;AssetDatabase.CreateAsset(pm,Output+"/Materials/AmberMotes.mat");
        var tex=new Texture2D(32,32,TextureFormat.RGBA32,false);
        for(int y=0;y<32;y++)for(int x=0;x<32;x++){
            float d=Vector2.Distance(new Vector2(x,y),new Vector2(15.5f,15.5f))/15.5f;
            tex.SetPixel(x,y,new Color(1,1,1,Mathf.Pow(Mathf.Clamp01(1-d),2)));
        }
        tex.Apply();AssetDatabase.CreateAsset(tex,Output+"/Textures/MoteSoftDisc.asset");pm.mainTexture=tex;
        for(int i=0;i<7;i++)
        {
            Vector3 p=i<5?route[Mathf.Min(route.Count-1,24+i*49)]:new Vector3(1,H(1,22),22);
            var obj=new GameObject(i<5?"Path fireflies":"Sanctuary amber embers");obj.transform.SetParent(fx,false);obj.transform.position=p+Vector3.up*2;
            var ps=obj.AddComponent<ParticleSystem>();var main=ps.main;main.maxParticles=32;main.loop=true;
            main.startLifetime=new ParticleSystem.MinMaxCurve(5f,8f);main.startSpeed=.06f;main.startSize=new ParticleSystem.MinMaxCurve(.045f,.1f);
            main.startColor=i<5?new Color(.8f,.88f,.48f,.8f):new Color(1,.66f,.2f,.85f);
            var emission=ps.emission;emission.rateOverTime=4;
            var shape=ps.shape;shape.shapeType=ParticleSystemShapeType.Box;shape.scale=new Vector3(9,3,9);
            var velocity=ps.velocityOverLifetime;velocity.enabled=true;
            velocity.x=new ParticleSystem.MinMaxCurve(-.025f,.025f);
            velocity.y=new ParticleSystem.MinMaxCurve(.03f,.12f);
            velocity.z=new ParticleSystem.MinMaxCurve(-.025f,.025f);
            var noise=ps.noise;noise.enabled=false;
            var renderer=obj.GetComponent<ParticleSystemRenderer>();renderer.sharedMaterial=pm;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            var range=obj.AddComponent<AmberRuinsLiteDetail>();range.renderDistance=24f;
            ps.Play();
        }
    }
    static void MakeBoundaries()
    {
        var bounds = Group("FOG BOUNDARY | solid limits around 80 x 80m", root);
        bounds.gameObject.AddComponent<AmberRuinsLiteBoundaryFog>();
        for (int i = 0; i < 4; i++)
        {
            var obj = new GameObject("Solid invisible fog boundary"); obj.transform.SetParent(bounds, false);
            obj.transform.position = i == 0 ? new Vector3(-40, 5, 0) :
                i == 1 ? new Vector3(40, 5, 0) : i == 2 ? new Vector3(0, 5, -40) : new Vector3(0, 5, 40);
            var c = obj.AddComponent<BoxCollider>();
            c.size = i < 2 ? new Vector3(1, 100, 81) : new Vector3(81, 100, 1);
            c.isTrigger = false;
        }
    }
    static void MakeSpawn()
    {
        var spawn = Group("PLAYER SPAWN | put first-person controller here", root);
        Vector3 p = route[0]; spawn.position = p + Vector3.up * .2f;
        Vector3 forward = (route[4] - route[0]); forward.y = 0;
        spawn.rotation = Quaternion.LookRotation(forward.normalized);
        var npc = Group("NPC PLACEMENT | panda or duck goes here", root);
        npc.position = p + new Vector3(2.7f, .2f, 0);
        var preview = new GameObject("EDITOR VIEW CAMERA | disabled by default");
        preview.transform.SetParent(spawn, false); preview.transform.localPosition = new Vector3(0, 1.7f, 0);
        preview.AddComponent<Camera>().enabled = false;
    }
}
#endif
