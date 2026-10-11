#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AmberRuinsLitePlayerSetup
{
    [MenuItem("Tools/Amber Ruins/Add Test Player to Open Scene")]
    public static void AddPlayer()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogWarning("Stop Play mode before adding the test player."); return; }
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return;
        Transform spawn = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            var existing = root.GetComponentInChildren<AmberRuinsLitePlayer>(true);
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                EditorGUIUtility.PingObject(existing.gameObject);
                Debug.Log("Amber Ruins: this scene already has a test player. Selected it instead of adding another.");
                return;
            }
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                if (child.name.StartsWith("PLAYER SPAWN |", StringComparison.Ordinal)) spawn = child;
        }
        if (spawn == null)
        {
            EditorUtility.DisplayDialog("Open the Amber Ruins scene", "Open your generated TheAmberRuins_Lite scene first. The test player uses its PLAYER SPAWN marker.", "OK");
            return;
        }
#if !ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        EditorUtility.DisplayDialog("Input is disabled", "Enable an input backend in Project Settings > Player > Active Input Handling, then let Unity recompile.", "OK");
        return;
#else
        Undo.IncrementCurrentGroup(); int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Add Amber Ruins test player");
        try
        {
            // Make the new camera the view into this scene. Camera and listener changes are undoable.
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                    if (camera.enabled && camera.targetTexture == null)
                    {
                        Undo.RecordObject(camera, "Use test player camera"); camera.enabled = false;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(camera);
                    }
                foreach (AudioListener listener in root.GetComponentsInChildren<AudioListener>(true))
                    if (listener.enabled)
                    {
                        Undo.RecordObject(listener, "Use test player audio listener"); listener.enabled = false;
                        PrefabUtility.RecordPrefabInstancePropertyModifications(listener);
                    }
            }
            var player = new GameObject("AMBER RUINS TEST PLAYER");
            SceneManager.MoveGameObjectToScene(player, scene);
            Undo.RegisterCreatedObjectUndo(player, "Create test player");
            player.tag = "Player";
            player.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            var controller = Undo.AddComponent<CharacterController>(player);
            controller.height = 1.8f; controller.radius = .3f;
            controller.center = Vector3.up * .9f;
            controller.skinWidth = .035f; controller.stepOffset = .3f;
            controller.slopeLimit = 50f; controller.minMoveDistance = 0;

            var cameraObject = new GameObject("Test Player Camera");
            Undo.RegisterCreatedObjectUndo(cameraObject, "Create test camera");
            cameraObject.transform.SetParent(player.transform, false);
            cameraObject.transform.localPosition = new Vector3(0, 1.65f, 0);
            cameraObject.tag = "MainCamera";
            var view = Undo.AddComponent<Camera>(cameraObject);
            view.fieldOfView = 70; view.nearClipPlane = .05f; view.farClipPlane = 350;
            view.clearFlags = CameraClearFlags.Skybox; view.backgroundColor = RenderSettings.fogColor;
            Undo.AddComponent<AudioListener>(cameraObject);
            var movement = Undo.AddComponent<AmberRuinsLitePlayer>(player);
            movement.view = view.transform;
            EditorUtility.SetDirty(movement);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = player;
            Debug.Log("Amber Ruins: test player added at PLAYER SPAWN. Save the scene, press Play, and click Game view. WASD walk; Shift sprint; Space jump; mouse look; Esc releases the mouse; R returns to the start.", player);
        }
        catch (Exception ex)
        {
            Undo.RevertAllDownToGroup(undoGroup);
            Debug.LogException(ex);
        }
        finally { Undo.CollapseUndoOperations(undoGroup); }
#endif
    }
}
#endif
