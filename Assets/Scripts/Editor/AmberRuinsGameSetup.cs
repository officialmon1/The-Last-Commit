using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PandaEditor
{
    /// <summary>
    /// Turns main's Amber Ruins scene into the playable game: brings over the gameplay from SampleScene
    /// (player, panda + AI/face service, code editor, timer/flower death, panda UI), scatters four broken-ruin
    /// problem pillars along the forest path, and wires the sanctuary: the broken ruins rebuild and the portal
    /// turns on after the four problems, and the final problem lives at the portal.
    /// Tools > The Last Commit > Build Game Scene runs it once; Reshuffle Problem Pillars re-scatters the four pillars.
    /// </summary>
    public static class AmberRuinsGameSetup
    {
        public const string GameScenePath = "Assets/Scenes/AmberRuinsLiteScenes/Forest_20261011_010711_237de3a1162647a0aa9b5d033cfb6d76/Scenes/TheAmberRuins_Lite.unity";
        const string GameplayScenePath = "Assets/Scenes/SampleScene.unity";
        const string BrokenPillarPath = "Assets/Prefabs/Ruins/BrokenPillar_01.fbx";
        const string ColumnPath = "Assets/Prefabs/Ruins/AncientColumn_01.fbx";
        const string Track = "dijkstra";
        const string PillarGroupName = "PROBLEM PILLARS | four broken ruins on the path (Tools > The Last Commit > Reshuffle)";

        // Root objects of SampleScene that make up the game. Its old placeholder pillars, light and volume stay behind.
        static readonly string[] GameplayRoots = { "Player", "Main Camera", "Panda", "EventSystem", "PandaService", "CodeEditor", "GameStart", "ClickRouter", "FlowerDeathDemo", "UI" };

        // Same control points as AmberRuinsLiteBuilder.MakeRoute: the winding path from the spawn to the sanctuary.
        static readonly Vector3[] RoutePoints =
        {
            new Vector3(-30,0,-35), new Vector3(-18,0,-29), new Vector3(12,0,-32),
            new Vector3(30,0,-22), new Vector3(14,0,-12), new Vector3(-22,0,-15),
            new Vector3(-30,0,-2), new Vector3(9,0,0), new Vector3(28,0,10),
            new Vector3(12,0,16), new Vector3(-7,0,10), new Vector3(0,0,19),
            new Vector3(1,0,22)
        };

        [MenuItem("Tools/The Last Commit/Build Game Scene (Amber Ruins + Panda)")]
        public static void BuildGameScene()
        {
            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            if (Object.FindAnyObjectByType<FirstPersonController>(FindObjectsInactive.Include) != null)
            {
                Debug.LogWarning("[AmberRuinsGameSetup] The Amber Ruins scene already has the game in it (found a FirstPersonController). Not touching it.");
                return;
            }

            // ---------------- bring the gameplay over from SampleScene ----------------
            var gameplay = EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            foreach (var root in gameplay.GetRootGameObjects())
                if (GameplayRoots.Contains(root.name)) SceneManager.MoveGameObjectToScene(root, scene);
            EditorSceneManager.CloseScene(gameplay, true);   // discarded: SampleScene on disk is untouched

            var player = Object.FindAnyObjectByType<FirstPersonController>();
            var spawn = FindByPrefix("PLAYER SPAWN |");
            if (spawn != null) player.transform.SetPositionAndRotation(spawn.position, Quaternion.Euler(0f, spawn.eulerAngles.y, 0f));

            // main's test player is replaced by zara2's player; keep its camera look
            var testPlayer = Object.FindAnyObjectByType<AmberRuinsLitePlayer>(FindObjectsInactive.Include);
            if (testPlayer != null)
            {
                var testCam = testPlayer.GetComponentInChildren<Camera>(true);
                var mainCam = FindRoot(scene, "Main Camera")?.GetComponent<Camera>();
                if (testCam != null && mainCam != null)
                {
                    mainCam.fieldOfView = testCam.fieldOfView;
                    mainCam.nearClipPlane = testCam.nearClipPlane;
                    mainCam.farClipPlane = testCam.farClipPlane;
                    mainCam.clearFlags = testCam.clearFlags;
                    mainCam.backgroundColor = testCam.backgroundColor;
                    mainCam.transform.SetPositionAndRotation(player.transform.position + Vector3.up * player.eyeHeight, player.transform.rotation);
                }
                Object.DestroyImmediate(testPlayer.gameObject);
            }

            var panda = FindRoot(scene, "Panda");
            var npc = FindByPrefix("NPC PLACEMENT |");
            if (panda != null && npc != null) panda.transform.position = npc.position;

            // ---------------- the four problem pillars on the path ----------------
            var pillarGroup = new GameObject(PillarGroupName).transform;
            var problemRuins = PlaceProblemPillars(pillarGroup, 1031);

            // ---------------- sanctuary ruins that get repaired ----------------
            var sanctuaryRuins = new List<RuinRestorer>();
            var sanctuary = FindByPrefix("FORGOTTEN SANCTUARY |");
            var column = AssetDatabase.LoadAssetAtPath<GameObject>(ColumnPath);
            foreach (Transform t in sanctuary)
            {
                if (!t.name.StartsWith("BROKEN PILLAR |", StringComparison.Ordinal)) continue;
                var restorer = t.gameObject.AddComponent<RuinRestorer>();
                if (t.name.Contains("BrokenPillar"))
                {
                    // a fallen chunk: a whole column rises in its place
                    restorer.brokenModel = t.GetChild(0).gameObject;
                    restorer.restoredModel = AddColumn(t, column, 4.2f);
                }
                restorer.restingGlow = 0.6f;
                sanctuaryRuins.Add(restorer);
            }
            // repair them in a circle around the sanctuary
            Vector3 c = sanctuary.position;
            sanctuaryRuins.Sort((a, b) => Angle(a.transform.position - c).CompareTo(Angle(b.transform.position - c)));

            // ---------------- the portal: final problem + turns on after the four ----------------
            var portal = FindByPrefix("PORTAL | ancient stone gateway");
            var barrier = FindByPrefix("PORTAL BARRIER |");
            var finalTerminal = portal.gameObject.AddComponent<CodeTerminal>();
            finalTerminal.track = Track;
            finalTerminal.level = 5;
            finalTerminal.finalProblem = true;
            finalTerminal.hideUntilGameStart = false;
            finalTerminal.interactDistance = 3.5f;
            var portalController = portal.gameObject.AddComponent<PortalController>();
            portalController.track = Track;
            portalController.finalLevel = 5;
            portalController.seal = barrier.GetComponentInChildren<MeshRenderer>();
            portalController.portalLight = barrier.GetComponentInChildren<Light>();
            portalController.barrier = barrier.GetComponent<Collider>();
            portalController.ruins = problemRuins.Concat(sanctuaryRuins).ToArray();

            // ---------------- start screen -> start the game ----------------
            var gameStart = Object.FindAnyObjectByType<GameStart>();
            gameStart.useStartKey = false;
            var pandaCanvas = FindRoot(scene, "UI")?.GetComponent<Canvas>();
            gameStart.hideUntilStart = pandaCanvas != null ? new[] { pandaCanvas } : Array.Empty<Canvas>();
            var startScreen = Object.FindAnyObjectByType<StartScreenController>(FindObjectsInactive.Include);
            if (startScreen != null)
            {
                var so = new SerializedObject(startScreen);
                var controls = so.FindProperty("playerControlScripts");
                controls.arraySize = 1;
                controls.GetArrayElementAtIndex(0).objectReferenceValue = player;
                so.ApplyModifiedPropertiesWithoutUndo();
                var begin = (UnityEvent)typeof(StartScreenController)
                    .GetField("onBeginEscape", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(startScreen);
                UnityEventTools.AddVoidPersistentListener(begin, gameStart.StartGame);
                EditorUtility.SetDirty(startScreen);
            }
            else Debug.LogWarning("[AmberRuinsGameSetup] No start screen found; the game starts with the Enter key instead.");

            // ---------------- build settings: this scene first ----------------
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != GameScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(GameScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[AmberRuinsGameSetup] Amber Ruins game scene built: player + panda at the spawn, 4 problem pillars on the path, final problem at the portal.");
        }

        [MenuItem("Tools/The Last Commit/Reshuffle Problem Pillars")]
        public static void ReshufflePillars()
        {
            var scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
            var group = FindRoot(scene, PillarGroupName)?.transform;
            var portal = Object.FindAnyObjectByType<PortalController>();
            if (group == null || portal == null)
            {
                Debug.LogWarning("[AmberRuinsGameSetup] Build the game scene first (Tools > The Last Commit > Build Game Scene).");
                return;
            }
            var old = group.GetComponentsInChildren<RuinRestorer>(true);
            for (int i = group.childCount - 1; i >= 0; i--) Object.DestroyImmediate(group.GetChild(i).gameObject);
            var placed = PlaceProblemPillars(group, Environment.TickCount);
            portal.ruins = placed.Concat(portal.ruins.Where(r => r != null && !old.Contains(r))).ToArray();
            EditorUtility.SetDirty(portal);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// Puts one broken pillar in each quarter of the path, at a random spot along it and a few metres to a random side,
        /// skipping spots where a tree or rock is in the way. Each pillar opens one prerequisite level.
        /// </summary>
        static List<RuinRestorer> PlaceProblemPillars(Transform group, int seed)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var broken = AssetDatabase.LoadAssetAtPath<GameObject>(BrokenPillarPath);
            var column = AssetDatabase.LoadAssetAtPath<GameObject>(ColumnPath);
            Physics.SyncTransforms();

            // the panda's click box is only shrunk to the panda at runtime; in the editor it covers the whole path, so ignore it here
            var pandaColliders = Object.FindObjectsByType<Panda.PandaCompanion>().SelectMany(p => p.GetComponentsInChildren<Collider>()).Where(c => c.enabled).ToArray();
            foreach (var c in pandaColliders) c.enabled = false;
            Physics.SyncTransforms();

            var path = SamplePath();
            var bins = new[] { (0.10f, 0.24f), (0.32f, 0.46f), (0.54f, 0.68f), (0.74f, 0.84f) };
            var result = new List<RuinRestorer>();
            for (int level = 1; level <= 4; level++)
            {
                var (lo, hi) = bins[level - 1];
                if (!FindSpot(path, R(lo, hi), rng, out Vector3 spot))
                {
                    Debug.LogWarning($"[AmberRuinsGameSetup] No clear spot found for pillar {level}; placing it on the path edge.");
                    spot = PointAt(path, (lo + hi) / 2f, out var tangent) + new Vector3(-tangent.z, 0, tangent.x) * 3f;
                    spot.y = GroundHeight(spot);
                }

                var root = new GameObject($"PROBLEM PILLAR {level} | press E for level {level}");
                root.transform.SetParent(group, false);
                root.transform.SetPositionAndRotation(spot, Quaternion.Euler(0f, R(0f, 360f), 0f));

                var chunk = AddModel(root.transform, broken, footprint: 2.6f);
                var restored = AddColumn(root.transform, column, 4.0f);

                var box = root.AddComponent<BoxCollider>();
                if (RuinRestorer.TryLocalBounds(root.transform, out var b)) { box.center = b.center; box.size = b.size; }

                var glow = new GameObject("Pillar glow").AddComponent<Light>();
                glow.transform.SetParent(root.transform, false);
                glow.transform.localPosition = new Vector3(0f, 2.2f, 0f);
                glow.type = LightType.Point;
                glow.range = 7f;
                glow.intensity = 2.2f;
                glow.shadows = LightShadows.None;

                var terminal = root.AddComponent<CodeTerminal>();
                terminal.track = Track;
                terminal.level = level;
                terminal.hideUntilGameStart = false;
                terminal.interactDistance = 3.2f;
                terminal.stateLight = glow;

                var restorer = root.AddComponent<RuinRestorer>();
                restorer.brokenModel = chunk;
                restorer.restoredModel = restored;
                restorer.restingGlow = 0f;   // the pillar's own state light already glows
                result.Add(restorer);
            }
            foreach (var c in pandaColliders) c.enabled = true;
            return result;
        }

        static bool FindSpot(List<Vector3> path, float fraction, System.Random rng, out Vector3 spot)
        {
            // try both sides, a few distances, and nudges along the path until nothing solid is in the way
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float f = Mathf.Clamp01(fraction + (attempt / 2) * 0.006f * (attempt % 4 < 2 ? 1 : -1));
                Vector3 p = PointAt(path, f, out var tangent);
                Vector3 side = new Vector3(-tangent.z, 0f, tangent.x) * (rng.Next(2) == 0 ? 1f : -1f);
                spot = p + side * (3.4f + (attempt % 3) * 0.6f);
                spot.y = GroundHeight(spot);
                if (float.IsNaN(spot.y)) continue;
                var flat = new Vector3(spot.x, 0f, spot.z);
                if (path.Any(q => (q - flat).sqrMagnitude < 3f * 3f)) continue;   // the path winds back on itself: don't land on another stretch of it
                var blockers = Physics.OverlapCapsule(spot + Vector3.up * 0.5f, spot + Vector3.up * 2.5f, 1.5f, ~0, QueryTriggerInteraction.Ignore);
                if (blockers.All(IsGround))
                {
                    Debug.Log($"[AmberRuinsGameSetup] Pillar spot {spot} ({fraction:P0} along the path).");
                    return true;
                }
            }
            spot = default;
            return false;
        }

        /// <summary>The walkable ground of main's builder: the terrain, the path ribbon and the trampled earth. Everything else is in the way.</summary>
        static bool IsGround(Collider c)
        {
            for (var t = c.transform; t != null; t = t.parent)
                if (t.name.StartsWith("TERRAIN |", StringComparison.Ordinal) || t.name.StartsWith("WINDING PATH |", StringComparison.Ordinal)
                    || t.name.StartsWith("Walkable trampled earth", StringComparison.Ordinal)) return true;
            return false;
        }

        static float GroundHeight(Vector3 p)
        {
            var hits = Physics.RaycastAll(new Vector3(p.x, 80f, p.z), Vector3.down, 200f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NaN;
            foreach (var h in hits)
                if (IsGround(h.collider) && (float.IsNaN(best) || h.point.y > best)) best = h.point.y;
            return best;
        }

        static List<Vector3> SamplePath()
        {
            var pts = new List<Vector3>();
            for (int i = 0; i < RoutePoints.Length - 1; i++)
                for (int s = 0; s < 24; s++)
                    pts.Add(Catmull(RoutePoints[Mathf.Max(0, i - 1)], RoutePoints[i], RoutePoints[i + 1], RoutePoints[Mathf.Min(RoutePoints.Length - 1, i + 2)], s / 24f));
            pts.Add(RoutePoints[RoutePoints.Length - 1]);
            return pts;
        }

        static Vector3 PointAt(List<Vector3> path, float fraction, out Vector3 tangent)
        {
            float total = 0f;
            for (int i = 1; i < path.Count; i++) total += Vector3.Distance(path[i - 1], path[i]);
            float target = fraction * total, run = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                float seg = Vector3.Distance(path[i - 1], path[i]);
                if (run + seg >= target || i == path.Count - 1)
                {
                    tangent = (path[i] - path[i - 1]).normalized;
                    return Vector3.Lerp(path[i - 1], path[i], seg > 0f ? (target - run) / seg : 0f);
                }
                run += seg;
            }
            tangent = Vector3.forward;
            return path[path.Count - 1];
        }

        static Vector3 Catmull(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t) =>
            .5f * ((2f * b) + (-a + c) * t + (2f * a - 5f * b + 4f * c - d) * t * t + (-a + 3f * b - 3f * c + d) * t * t * t);

        /// <summary>Instantiates a model under <paramref name="parent"/>, scaled to the footprint and resting on the parent's origin.</summary>
        static GameObject AddModel(Transform parent, GameObject prefab, float footprint = 0f, float height = 0f)
        {
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            model.transform.SetParent(parent, false);
            var b = WorldBounds(model);
            float k = height > 0f ? height / Mathf.Max(0.001f, b.size.y) : footprint / Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.z));
            model.transform.localScale *= k;
            b = WorldBounds(model);
            // centre over the parent and sink 5 cm into the ground so it doesn't float on slopes
            Vector3 shift = parent.position - new Vector3(b.center.x, b.min.y + 0.05f, b.center.z);
            model.transform.position += shift;
            return model;
        }

        /// <summary>The whole column a broken ruin turns into, hidden until <see cref="RuinRestorer.Restore"/>.</summary>
        static GameObject AddColumn(Transform parent, GameObject column, float height)
        {
            // stand it on the ground under the broken model, upright even if the ruin is tilted
            var model = AddModel(parent, column, height: height);
            model.name = "Restored column";
            var groundY = WorldBounds(parent.GetChild(0).gameObject).min.y;
            var b = WorldBounds(model);
            model.transform.position += Vector3.up * (groundY - b.min.y);
            model.SetActive(false);
            return model;
        }

        static Bounds WorldBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static float Angle(Vector3 d) => Mathf.Atan2(d.x, d.z);

        static Transform FindByPrefix(string prefix) =>
            Object.FindObjectsByType<Transform>(FindObjectsInactive.Include)
                .FirstOrDefault(t => t.name.StartsWith(prefix, StringComparison.Ordinal));

        static GameObject FindRoot(Scene scene, string name) => scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
    }
}
