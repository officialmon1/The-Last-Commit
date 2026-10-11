using UnityEngine;
using UnityEngine.SceneManagement;

// Solid perimeter colliders are created by the builder. This fades the view
// into matching fog before the player reaches them, keeping the edge concealed.
[DisallowMultipleComponent]
public sealed class AmberRuinsLiteBoundaryFog : MonoBehaviour
{
    public const float DefaultFogStart = 10f;
    public const float DefaultFogEnd = 34f;
    public Vector2 center = Vector2.zero;
    [Min(1)] public float halfSize = 40f;
    [Min(.1f)] public float fogBand = 4f;
    [Min(0)] public float fullyHiddenWithin = 1.1f;
    [Min(0)] public float baseFogStart = DefaultFogStart;
    [Min(1)] public float baseFogEnd = DefaultFogEnd;
    float veil;
    bool changedFog;

    bool IsActiveScene() { return gameObject.scene == SceneManager.GetActiveScene(); }
    void LateUpdate()
    {
        if (!IsActiveScene()) return;
        Camera camera = Camera.main;
        if (camera == null && Camera.allCamerasCount > 0) camera = Camera.allCameras[0];
        if (camera == null) return;
        Vector3 p = camera.transform.position;
        float edge = Mathf.Min(halfSize - Mathf.Abs(p.x - center.x), halfSize - Mathf.Abs(p.z - center.y));
        float full = Mathf.Clamp(fullyHiddenWithin, 0, fogBand - .01f);
        float amount = Mathf.Clamp01((fogBand - edge) / Mathf.Max(.01f, fogBand - full));
        amount = Mathf.SmoothStep(0, 1, amount);
        veil = amount * amount;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = Mathf.Lerp(baseFogStart, 0, amount);
        RenderSettings.fogEndDistance = Mathf.Lerp(baseFogEnd, .2f, amount);
        camera.backgroundColor = RenderSettings.fogColor;
        changedFog = true;
    }
    void OnDisable()
    {
        if (!changedFog || !IsActiveScene()) return;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = baseFogStart;
        RenderSettings.fogEndDistance = baseFogEnd;
        veil = 0;
    }
    void OnGUI()
    {
        if (veil <= .001f || !IsActiveScene()) return;
        // A cheap matching veil also hides imported shaders that don't support scene fog.
        // Draw behind the player's controls. At the wall it is fully opaque.
        int previousDepth = GUI.depth; Color previousColor = GUI.color;
        GUI.depth = 1000;
        Color color = RenderSettings.fogColor; color.a = veil;
        GUI.color = color;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = previousColor; GUI.depth = previousDepth;
    }
}
