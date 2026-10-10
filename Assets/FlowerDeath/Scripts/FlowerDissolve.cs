using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// "Dissolve into petals on death" component (Unity port of the Godot flower_dissolve.gd).
///
/// Usage: attach to the character (or set target to the character root) and call Play() when it dies. It will:
///   1. swap every Renderer under target to the dissolve material and erode it piece by piece, top-down by default;
///   2. spawn petal particles at the dissolving edge; they tumble away in the wind, land, shrink and vanish.
/// DissolveFinished: the body is fully gone (safe to Destroy the character). The petal system is independent and destroys itself when done.
/// </summary>
public class FlowerDissolve : MonoBehaviour
{
    [Tooltip("Root of the character to dissolve; empty = this object.")]
    public Transform target;

    [Header("Dissolve")]
    public float duration = 2.6f;
    public bool topDown = true;
    [Range(0f, 1f)] public float heightWeight = 0.65f;
    public float noiseScale = 5f;
    [Range(0f, 0.3f)] public float edgeWidth = 0.05f;
    [ColorUsage(false, true)] public Color edgeColor = new Color(1f, 0.58f, 0.78f);
    public float edgeEmission = 5f;
    public bool hideTargetWhenDone = true;
    [Tooltip("When set, distance to this point replaces height: the closest parts dissolve first (e.g. spreading from the hands over the body). topDown is ignored.")]
    public Transform dissolveOrigin;

    [Header("Petals")]
    [Tooltip("Total number of petals released over the whole dissolve.")]
    public int petalAmount = 700;
    public float petalLifetime = 3.2f;
    public Vector2 petalSize = new Vector2(0.075f, 0.1f);
    [Tooltip("Wind (constant acceleration, m/s^2). A positive y lifts the petals first.")]
    public Vector3 wind = new Vector3(1.2f, 0.1f, 0.3f);
    public Gradient petalColors;
    [Tooltip("Petals land on Colliders in the scene.")]
    public bool petalsCollide = true;

    [Header("Shaders (empty = load from FlowerDeath/Resources)")]
    [SerializeField] Shader dissolveShader;
    [SerializeField] Shader petalShader;

    public event Action DissolveFinished;
    public bool IsPlaying { get; private set; }

    const int NoiseOffset = 4096;
    static readonly int ProgressId = Shader.PropertyToID("_Progress");
    static readonly int WorldToTargetId = Shader.PropertyToID("_WorldToTarget");

    struct Sample { public Vector3 pos, normal; public float value; }
    struct Saved { public Renderer r; public Material[] mats; public bool enabled; }

    readonly List<Saved> _saved = new List<Saved>();
    readonly List<Material> _materials = new List<Material>();
    readonly List<Sample> _samples = new List<Sample>();   // sorted by value ascending
    int _nextSample;
    ParticleSystem _particles;
    Transform _root;
    float _pStart, _pEnd, _t;

    Transform Root => target != null ? target : transform;

    /// <summary>Start dissolving.</summary>
    public void Play()
    {
        if (IsPlaying) return;
        _root = Root;
        var renderers = new List<Renderer>();
        foreach (var r in _root.GetComponentsInChildren<Renderer>())
            if (r is MeshRenderer || r is SkinnedMeshRenderer) renderers.Add(r);
        if (renderers.Count == 0) { Debug.LogWarning("FlowerDissolve: no Renderer under target.", this); return; }

        float vMin, vMax;
        BuildSamples(renderers, out vMin, out vMax);
        ApplyDissolveMaterials(renderers);
        CreateParticles();

        // Dissolve threshold range: start just before any pixel glows, end just after everything is gone
        _pStart = vMin - edgeWidth - 0.01f;
        _pEnd = vMax + 0.03f;
        _nextSample = 0;
        _t = 0f;
        SetProgress(_pStart);
        IsPlaying = true;
    }

    /// <summary>Restore the character (demo / revive). Petals already released keep flying.</summary>
    public void ResetDissolve()
    {
        IsPlaying = false;
        RetireParticles();
        foreach (var s in _saved)
        {
            if (s.r == null) continue;
            s.r.sharedMaterials = s.mats;
            s.r.enabled = s.enabled;
        }
        foreach (var m in _materials) Destroy(m);
        _saved.Clear();
        _materials.Clear();
    }

    void Update()
    {
        if (!IsPlaying) return;
        _t += Time.deltaTime;
        float k = Mathf.Clamp01(_t / duration);
        // slightly slow at the start and faster at the end, so it feels carried away by the wind
        float eased = k * k * (3f - 2f * k) * 0.5f + k * 0.5f;
        float p = Mathf.Lerp(_pStart, _pEnd, eased);
        SetProgress(p);
        EmitCrossed(p);
        if (k >= 1f) Finish();
    }

    void Finish()
    {
        IsPlaying = false;
        if (hideTargetWhenDone)
            foreach (var s in _saved) if (s.r != null) s.r.enabled = false;
        RetireParticles();
        DissolveFinished?.Invoke();
    }

    void RetireParticles()
    {
        if (_particles == null) return;
        _particles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        Destroy(_particles.gameObject, petalLifetime * 1.5f);
        _particles = null;
    }

    // ------------------------------------------------------------------ Dissolve materials

    void ApplyDissolveMaterials(List<Renderer> renderers)
    {
        var shader = dissolveShader != null ? dissolveShader : Resources.Load<Shader>("FlowerDissolve");
        _saved.Clear();
        _materials.Clear();
        foreach (var r in renderers)
        {
            var src = r.sharedMaterials;
            _saved.Add(new Saved { r = r, mats = src, enabled = r.enabled });
            var mats = new Material[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var m = new Material(shader);
                var s = src[i];
                if (s != null)
                {
                    // common property names of URP Lit / most shaders
                    if (s.HasProperty("_BaseColor")) m.SetColor("_BaseColor", s.GetColor("_BaseColor"));
                    else if (s.HasProperty("_Color")) m.SetColor("_BaseColor", s.GetColor("_Color"));
                    if (s.HasProperty("_BaseMap") && s.GetTexture("_BaseMap") != null) m.SetTexture("_BaseMap", s.GetTexture("_BaseMap"));
                    else if (s.HasProperty("_MainTex") && s.GetTexture("_MainTex") != null) m.SetTexture("_BaseMap", s.GetTexture("_MainTex"));
                    if (s.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", s.GetFloat("_Smoothness"));
                    if (s.HasProperty("_Metallic")) m.SetFloat("_Metallic", s.GetFloat("_Metallic"));
                }
                m.SetFloat("_MinY", _minY);
                m.SetFloat("_MaxY", _maxY);
                m.SetVector("_Origin", _origin);
                m.SetFloat("_MaxDist", _maxDist);
                m.SetFloat("_UseOrigin", dissolveOrigin != null ? 1f : 0f);
                m.SetFloat("_TopDown", topDown ? 1f : 0f);
                m.SetFloat("_HeightWeight", heightWeight);
                m.SetFloat("_NoiseScale", noiseScale);
                m.SetFloat("_EdgeWidth", edgeWidth);
                m.SetColor("_EdgeColor", edgeColor);
                m.SetFloat("_EdgeEmission", edgeEmission);
                mats[i] = m;
                _materials.Add(m);
            }
            r.sharedMaterials = mats;
        }
    }

    void SetProgress(float p)
    {
        var w2t = _root.worldToLocalMatrix;
        foreach (var m in _materials)
        {
            m.SetFloat(ProgressId, p);
            m.SetMatrix(WorldToTargetId, w2t);
        }
    }

    // ------------------------------------------------------------------ Petal particles

    void CreateParticles()
    {
        var go = new GameObject("Petals");   // at the scene root, so petals keep flying after the character is destroyed
        go.transform.position = _root.position;
        _particles = go.AddComponent<ParticleSystem>();
        var ps = _particles;
        ps.Stop();

        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.loop = false;
        main.playOnAwake = false;
        main.maxParticles = petalAmount + 64;
        main.startLifetime = petalLifetime;
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.enabled = false;   // everything is emitted manually with Emit()

        var force = ps.forceOverLifetime;
        force.enabled = true;
        force.space = ParticleSystemSimulationSpace.World;
        var g = wind + new Vector3(0f, -0.35f, 0f);
        force.x = g.x; force.y = g.y; force.z = g.z;

        var drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.drag = new ParticleSystem.MinMaxCurve(0.3f, 0.8f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.6f;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 0.3f;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        // tumbling (radians/second)
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.separateAxes = true;
        rot.x = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
        rot.y = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);
        rot.z = new ParticleSystem.MinMaxCurve(-2.5f, 2.5f);

        // pop in from 0 at birth, shrink away at the end
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.08f, 1f), new Keyframe(0.75f, 1f), new Keyframe(1f, 0f)));

        // brighter when spawned (catching the glow of the dissolve edge), then back to the base color
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var life = new Gradient();
        life.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.85f, 0.85f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        col.color = life;

        if (petalsCollide)
        {
            var c = ps.collision;
            c.enabled = true;
            c.type = ParticleSystemCollisionType.World;
            c.mode = ParticleSystemCollisionMode.Collision3D;
            c.bounce = 0f;
            c.dampen = 0.9f;
            c.lifetimeLoss = 0f;
            c.quality = ParticleSystemCollisionQuality.High;
        }

        var r = ps.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Mesh;
        r.alignment = ParticleSystemRenderSpace.World;
        r.mesh = PetalQuad();
        r.sharedMaterial = new Material(petalShader != null ? petalShader : Resources.Load<Shader>("Petal"));
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        ps.Play();
    }

    Mesh PetalQuad()
    {
        float w = petalSize.x * 0.5f, h = petalSize.y * 0.5f;
        var m = new Mesh { name = "Petal" };
        m.vertices = new[] { new Vector3(-w, -h, 0), new Vector3(w, -h, 0), new Vector3(-w, h, 0), new Vector3(w, h, 0) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.normals = new[] { -Vector3.forward, -Vector3.forward, -Vector3.forward, -Vector3.forward };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        return m;
    }

    Gradient PetalGradient()
    {
        if (petalColors != null && petalColors.colorKeys.Length > 0) return petalColors;
        var g = new Gradient();   // white ~ pink ~ deep pink
        g.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 0.86f, 0.91f), 0f),
                new GradientColorKey(new Color(0.98f, 0.64f, 0.78f), 0.5f),
                new GradientColorKey(new Color(0.93f, 0.42f, 0.62f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        return g;
    }

    /// <summary>Emit every surface sample that crossed the dissolve threshold between the previous frame and this one.</summary>
    void EmitCrossed(float p)
    {
        if (_particles == null) return;
        var grad = PetalGradient();
        var ep = new ParticleSystem.EmitParams();
        while (_nextSample < _samples.Count && _samples[_nextSample].value < p)
        {
            var s = _samples[_nextSample++];
            var pos = _root.TransformPoint(s.pos);
            var n = _root.TransformDirection(s.normal);
            // along the normal, scattered randomly within about 50 degrees
            var dir = Vector3.Slerp(n, UnityEngine.Random.onUnitSphere, UnityEngine.Random.value * 0.55f).normalized;
            ep.position = pos;
            ep.velocity = dir * UnityEngine.Random.Range(0.25f, 0.9f);
            ep.startLifetime = petalLifetime * UnityEngine.Random.Range(0.65f, 1f);
            ep.startSize = UnityEngine.Random.Range(0.7f, 1.3f);
            ep.rotation3D = UnityEngine.Random.rotation.eulerAngles;
            ep.startColor = grad.Evaluate(UnityEngine.Random.value);
            _particles.Emit(ep, 1);
        }
    }

    // ------------------------------------------------------------------ Surface sampling (target local space)

    float _minY, _maxY, _maxDist;
    Vector3 _origin;

    void BuildSamples(List<Renderer> renderers, out float vMin, out float vMax)
    {
        var toLocal = _root.worldToLocalMatrix;
        var tris = new List<Vector3>();
        var cum = new List<float>();
        float total = 0f;
        _minY = float.PositiveInfinity; _maxY = float.NegativeInfinity;
        _origin = dissolveOrigin != null ? toLocal.MultiplyPoint3x4(dissolveOrigin.position) : Vector3.zero;
        _maxDist = 0f;

        foreach (var r in renderers)
        {
            Mesh mesh; Matrix4x4 xf;
            if (r is SkinnedMeshRenderer smr)
            {
                // skinned characters: sample the pose at the moment of death, not the T-pose
                mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                xf = toLocal * Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
            }
            else
            {
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                mesh = mf.sharedMesh;
                xf = toLocal * r.transform.localToWorldMatrix;
            }
            if (!mesh.isReadable) { Debug.LogWarning($"FlowerDissolve: the mesh on {r.name} is not Read/Write enabled; no petals will be emitted from it.", r); continue; }

            var verts = mesh.vertices;
            var idx = mesh.triangles;
            var local = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++)
            {
                local[i] = xf.MultiplyPoint3x4(verts[i]);
                _minY = Mathf.Min(_minY, local[i].y);
                _maxY = Mathf.Max(_maxY, local[i].y);
                _maxDist = Mathf.Max(_maxDist, Vector3.Distance(local[i], _origin));
            }
            for (int i = 0; i < idx.Length; i += 3)
            {
                Vector3 a = local[idx[i]], b = local[idx[i + 1]], c = local[idx[i + 2]];
                float area = Vector3.Cross(b - a, c - a).magnitude * 0.5f;
                if (area <= 0f) continue;
                tris.Add(a); tris.Add(b); tris.Add(c);
                total += area;
                cum.Add(total);
            }
        }

        // Sample surface points weighted by area, compute each point's dissolve value v, sort by v, then emit each one as the threshold passes it
        _samples.Clear();
        vMin = float.PositiveInfinity; vMax = float.NegativeInfinity;
        for (int s = 0; s < petalAmount && cum.Count > 0; s++)
        {
            int ti = cum.BinarySearch(UnityEngine.Random.value * total);
            if (ti < 0) ti = ~ti;
            ti = Mathf.Min(ti, cum.Count - 1);
            Vector3 a = tris[ti * 3], b = tris[ti * 3 + 1], c = tris[ti * 3 + 2];
            float u = UnityEngine.Random.value, w = UnityEngine.Random.value;
            if (u + w > 1f) { u = 1f - u; w = 1f - w; }
            var p = a + (b - a) * u + (c - a) * w;
            var smp = new Sample { pos = p, normal = Vector3.Cross(b - a, c - a).normalized, value = DissolveValue(p) };
            _samples.Add(smp);
            vMin = Mathf.Min(vMin, smp.value);
            vMax = Mathf.Max(vMax, smp.value);
        }
        _samples.Sort((x, y) => x.value.CompareTo(y.value));
        // calibrate the dissolve range with every vertex so no pixel glows on frame 0
        foreach (var t in tris) { float v = DissolveValue(t); vMin = Mathf.Min(vMin, v); vMax = Mathf.Max(vMax, v); }
    }

    // ------------------------------------------------------------------ Noise identical to the shader

    float DissolveValue(Vector3 p)
    {
        float h;
        if (dissolveOrigin != null)
        {
            h = Mathf.Clamp01(Vector3.Distance(p, _origin) / Mathf.Max(_maxDist, 0.0001f));
        }
        else
        {
            h = Mathf.Clamp01((p.y - _minY) / Mathf.Max(_maxY - _minY, 0.0001f));
            if (topDown) h = 1f - h;
        }
        return Mathf.Lerp(Fbm(p * noiseScale), h, heightWeight);
    }

    static float Hash3(int x, int y, int z)
    {
        uint ux = (uint)(x + NoiseOffset), uy = (uint)(y + NoiseOffset), uz = (uint)(z + NoiseOffset);
        uint h = unchecked((ux * 73856093u) ^ (uy * 19349663u) ^ (uz * 83492791u));
        h ^= h >> 16;
        h = unchecked(h * 2146121005u);
        h ^= h >> 15;
        h = unchecked(h * 2221713035u);
        h ^= h >> 16;
        return h / 4294967295f;
    }

    static float ValueNoise(Vector3 p)
    {
        int cx = Mathf.FloorToInt(p.x), cy = Mathf.FloorToInt(p.y), cz = Mathf.FloorToInt(p.z);
        var f = new Vector3(p.x - cx, p.y - cy, p.z - cz);
        var u = new Vector3(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y), f.z * f.z * (3f - 2f * f.z));
        float x00 = Mathf.Lerp(Hash3(cx, cy, cz), Hash3(cx + 1, cy, cz), u.x);
        float x10 = Mathf.Lerp(Hash3(cx, cy + 1, cz), Hash3(cx + 1, cy + 1, cz), u.x);
        float x01 = Mathf.Lerp(Hash3(cx, cy, cz + 1), Hash3(cx + 1, cy, cz + 1), u.x);
        float x11 = Mathf.Lerp(Hash3(cx, cy + 1, cz + 1), Hash3(cx + 1, cy + 1, cz + 1), u.x);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, u.y), Mathf.Lerp(x01, x11, u.y), u.z);
    }

    static float Fbm(Vector3 p)
    {
        float n = 0.5f * ValueNoise(p);
        n += 0.25f * ValueNoise(p * 2.03f + Vector3.one * 17.1f);
        n += 0.125f * ValueNoise(p * 4.01f + Vector3.one * 31.7f);
        return n / 0.875f;
    }
}
