using System.Collections;
using UnityEngine;

/// <summary>
/// A broken ruin that rebuilds itself once the four prerequisite problems are solved (<see cref="PortalController"/> calls <see cref="Restore"/>).
/// The broken chunk sinks away while <see cref="restoredModel"/> (a whole column) rises out of the ground in an amber flash.
/// Without a restoredModel (a column that is still standing, just tilted) it straightens up and glows instead.
/// </summary>
public class RuinRestorer : MonoBehaviour
{
    [Tooltip("The broken model shown at the start. Sinks away on restore.")] public GameObject brokenModel;
    [Tooltip("The repaired model, hidden at the start. Rises out of the ground on restore. Empty = just straighten up.")] public GameObject restoredModel;
    public float duration = 2.4f;
    public Color glowColor = new Color(1f, 0.62f, 0.2f);
    [Tooltip("Soft light left on the ruin once it is whole again (0 = none).")] public float restingGlow = 0.8f;

    public bool Restored { get; private set; }

    void Awake()
    {
        if (restoredModel != null) restoredModel.SetActive(false);
    }

    public void Restore(float delay = 0f)
    {
        if (Restored) return;
        Restored = true;
        StartCoroutine(Run(delay));
    }

    IEnumerator Run(float delay)
    {
        if (delay > 0f) yield return new WaitForSeconds(delay);

        var glow = new GameObject("Restore glow").AddComponent<Light>();
        glow.transform.SetParent(transform, false);
        glow.type = LightType.Point;
        glow.color = glowColor;
        glow.range = 9f;
        glow.intensity = 0f;

        Quaternion startRot = transform.rotation;
        Quaternion upright = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        Vector3 brokenFrom = Vector3.zero, brokenScale = Vector3.one;
        float brokenDrop = 0f;
        if (brokenModel != null)
        {
            brokenFrom = brokenModel.transform.position;
            brokenScale = brokenModel.transform.localScale;
            brokenDrop = WorldBounds(brokenModel).size.y + 0.2f;
        }

        Vector3 restoredTo = Vector3.zero;
        float rise = 0f;
        if (restoredModel != null)
        {
            restoredModel.SetActive(true);
            restoredTo = restoredModel.transform.position;
            rise = WorldBounds(restoredModel).size.y + 0.2f;
            restoredModel.transform.position = restoredTo - Vector3.up * rise;
        }
        glow.transform.position = transform.position + Vector3.up * Mathf.Max(1.5f, rise * 0.5f);

        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            float e = 1f - Mathf.Pow(1f - t, 3f);   // ease out
            glow.intensity = Mathf.Sin(t * Mathf.PI) * 6f + restingGlow * t;
            transform.rotation = Quaternion.Slerp(startRot, upright, e);
            if (brokenModel != null)
            {
                brokenModel.transform.position = brokenFrom - Vector3.up * brokenDrop * Mathf.Clamp01(t * 1.6f);
                brokenModel.transform.localScale = brokenScale * Mathf.Lerp(1f, 0.6f, t);
            }
            if (restoredModel != null)
                restoredModel.transform.position = restoredTo - Vector3.up * rise * (1f - e);
            yield return null;
        }

        transform.rotation = upright;
        if (restoredModel != null)
        {
            restoredModel.transform.position = restoredTo;
            if (brokenModel != null) brokenModel.SetActive(false);
        }
        glow.intensity = restingGlow;
        if (restingGlow <= 0f) Destroy(glow.gameObject);
        FitCollider();
    }

    /// <summary>Resizes this ruin's box collider to whatever is visible now (the column is taller than the broken chunk).</summary>
    void FitCollider()
    {
        var box = GetComponent<BoxCollider>();
        if (box == null) return;
        if (!TryLocalBounds(transform, out var b)) return;
        box.center = b.center;
        box.size = b.size;
    }

    static Bounds WorldBounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    /// <summary>Bounds of every active mesh under <paramref name="root"/>, in root's local space (tight even when rotated).</summary>
    public static bool TryLocalBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        bool any = false;
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds;
            var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = m.MultiplyPoint3x4(corner);
                if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                else bounds.Encapsulate(p);
            }
        }
        return any;
    }
}
