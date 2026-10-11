using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

// Distance culling changes rendering only, leaving solid colliders active.
// Source prefab assets are never edited.
[ExecuteAlways]
public sealed class AmberRuinsLiteDetail : MonoBehaviour
{
    [Min(1)] public float renderDistance = 20f;
    Renderer[] cached;
    float nextCheck;
    void OnEnable()
    {
        cached = GetComponentsInChildren<Renderer>(true);
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
        EditorApplication.update += EditorTick;
#endif
    }
    void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
#endif
        if(cached!=null)foreach(var r in cached)if(r!=null)r.forceRenderingOff=false;
    }
    void Update()
    {
        if(!Application.isPlaying || Time.unscaledTime<nextCheck)return;
        nextCheck=Time.unscaledTime+.25f;
        var camera=Camera.main;
        if(camera==null && Camera.allCamerasCount>0)camera=Camera.allCameras[0];
        if(camera==null)return;
        Check(camera.transform.position);
    }
    void Check(Vector3 viewer)
    {
        bool hidden=(viewer-transform.position).sqrMagnitude>renderDistance*renderDistance;
        if(cached==null)return;
        foreach(var r in cached)if(r!=null)r.forceRenderingOff=hidden;
    }
#if UNITY_EDITOR
    double nextEditorCheck;
    void EditorTick()
    {
        if(this==null || Application.isPlaying || EditorApplication.timeSinceStartup<nextEditorCheck)return;
        nextEditorCheck=EditorApplication.timeSinceStartup+.25;
        if(SceneView.lastActiveSceneView!=null && SceneView.lastActiveSceneView.camera!=null)
            Check(SceneView.lastActiveSceneView.camera.transform.position);
    }
#endif
}
