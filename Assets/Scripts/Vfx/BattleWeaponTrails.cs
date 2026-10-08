using System;
using System.Collections.Generic;
using FrankRetarget;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Short world-space ribbons sampled from the actual animated weapon mesh.</summary>
[DisallowMultipleComponent]
public sealed class BattleWeaponTrails : MonoBehaviour
{
    [Serializable]
    public sealed class Style
    {
        public TrumpWeaponManager.WeaponType weapon;
        public Color color = new Color(1, .72f, .15f, .8f);
        [Range(.04f, .2f)] public float lifetime = .1f;
        [Range(0, .95f)] public float bladeStart = .2f;
        [Range(0, .95f)] public float thrustBladeStart = .9f;
        [Range(0, .2f)] public float thrustWidth;
        [Range(.025f, .2f)] public float followThroughSeconds = .025f;
        public Material material;
    }

    public Material material;
    public Style[] styles = Array.Empty<Style>();
    [Serializable]
    public sealed class StaticBlade
    {
        public Mesh mesh;
        public Vector3 bladeBase, bladeTip;
        public BonePoint[] baseBones = Array.Empty<BonePoint>();
        public BonePoint[] tipBones = Array.Empty<BonePoint>();
    }
    [Serializable]
    public sealed class BonePoint
    {
        public int bone;
        public Vector3 point;
        public float weight;
    }
    [Tooltip("Calibrated blade endpoints use only a few bone transforms, including meshes without Read/Write.")]
    public StaticBlade[] staticBlades = Array.Empty<StaticBlade>();
    [Range(8, 32)] public int maxTrails = 16;
    [Tooltip("Clear cached world ribbons if a fighter root jumps this far in one update, in metres.")]
    [Min(.25f)] public float discontinuityDistance = 2.5f;
    public int ActiveTrailCount
    {
        get
        {
            int count = 0;
            foreach (var trail in pool) if (trail.root && trail.root.activeSelf) count++;
            return count;
        }
    }
    public int PooledTrailCount => pool.Count;
    public int SampledPoseCount { get; private set; }

    sealed class Window
    {
        public BattleSfxBank.Cue cue;
        public float start, end;
        public bool shield;
    }
    sealed class Strip
    {
        public Mesh mesh;
        public MeshRenderer meshRenderer;
        public Renderer source;
        public readonly List<Vector3> inner = new List<Vector3>();
        public readonly List<Vector3> tip = new List<Vector3>();
        public int baseIndex, tipIndex;
        public Vector3 thrustAxis;
        public Vector3 thrustDirection;
    }
    sealed class Trail
    {
        public GameObject root;
        public readonly List<Strip> strips = new List<Strip>();
        public Window window;
        public Style style;
        public float sampleStep, clock;
    }

    readonly List<Window> windows = new List<Window>();
    readonly List<Trail> pool = new List<Trail>();
    readonly List<Vector3> geometry = new List<Vector3>();
    readonly List<Vector3> vertices = new List<Vector3>();
    readonly List<Color> colors = new List<Color>();
    readonly List<Vector2> uvs = new List<Vector2>();
    readonly List<int> triangles = new List<int>();
    readonly List<Renderer> candidates = new List<Renderer>();
    readonly Dictionary<Renderer, Vector3> before = new Dictionary<Renderer, Vector3>();
    readonly Dictionary<SkinnedMeshRenderer, Transform[]> boneCache = new Dictionary<SkinnedMeshRenderer, Transform[]>();
    readonly HashSet<BattleSfxBank.Cue> assignedHits = new HashSet<BattleSfxBank.Cue>();
    FrankBattlePairPlayback owner;
    CharacterCombat attacker;
    Style style;
    Camera view;
    Mesh scratch;
    Transform poolRoot;
    Vector3 lastRootPosition;
    int playbackId = -1;

    public bool Owns(GameObject root) => root && pool.Exists(t => t.root == root);

    public void Begin(FrankBattlePairPlayback playback, CharacterCombat source, BattleSfxBank.Move profile, Camera camera = null)
    {
        Clear();
        if (!playback || !source || profile?.cues == null) return;
        owner = playback;
        attacker = source;
        view = camera;
        playbackId = playback.PlaybackId;
        lastRootPosition = source.transform.position;
        style = Array.Find(styles ?? Array.Empty<Style>(), s => s != null && s.weapon == playback.Move.weapon);
        windows.Clear(); assignedHits.Clear();
        if (!isActiveAndEnabled || !material || style == null || playback.Move.skill != BattleSkill.None ||
            playback.Move.weapon == TrumpWeaponManager.WeaponType.None) return;
        foreach (var cue in profile.cues)
        {
            if (cue.group == null || !cue.group.EndsWith("swing", StringComparison.Ordinal)) continue;
            BattleSfxBank.Cue hit = null;
            foreach (var candidate in profile.cues)
                if (candidate.seconds > cue.seconds && candidate.seconds - cue.seconds <= .65f &&
                    (candidate.group == "light_hit" || candidate.group == "heavy_hit" || candidate.group == "stab_hit") &&
                    !assignedHits.Contains(candidate)) { hit = candidate; break; }
            if (hit == null || (hit.contactSource != "Weapon" && hit.contactSource != "Shield")) continue;
            assignedHits.Add(hit);
            float end = Mathf.Min(playback.Duration, hit.seconds + style.followThroughSeconds);
            // A rapid combo needs the return motion too, but each new stroke owns
            // its own sweep rather than extending the preceding ribbon into it.
            foreach (var next in profile.cues)
                if (next.seconds > hit.seconds && next.group != null && next.group.EndsWith("swing", StringComparison.Ordinal))
                { end = Mathf.Min(end, next.seconds); break; }
            windows.Add(new Window { cue = cue, shield = hit.contactSource == "Shield",
                start = Mathf.Max(0, cue.seconds - 1f / 30f),
                end = end });
        }
    }

    public GameObject Play(BattleSfxBank.Cue cue)
    {
        if (!isActiveAndEnabled || !material || !owner || !owner.Playing) return null;
        var window = windows.Find(w => ReferenceEquals(w.cue, cue));
        if (window == null) return null;
        var trail = pool.Find(t => t.root && !t.root.activeSelf);
        if (trail == null)
        {
            if (pool.Count >= maxTrails) return null;
            if (!poolRoot)
            {
                var host = new GameObject("Weapon trail pool");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, gameObject.scene);
                poolRoot = host.transform;
            }
            trail = new Trail { root = new GameObject("Weapon trail") };
            trail.root.SetActive(false);
            trail.root.transform.SetParent(poolRoot, false);
            pool.Add(trail);
        }
        trail.window = window; trail.style = style; trail.clock = cue.seconds;
        foreach (var strip in trail.strips) { strip.inner.Clear(); strip.tip.Clear(); strip.mesh.Clear(); }
        candidates.Clear(); before.Clear();
        var renderers = owner.AttackerActor?.Pose?.weaponRenderers;
        if (renderers == null) return null;
        foreach (var renderer in renderers)
            if (Usable(renderer) && IsShield(renderer) == window.shield) candidates.Add(renderer);
        float displayed = owner.SampleTime;
        try
        {
            owner.EvaluateAt(Mathf.Max(0, cue.seconds - 1f / 30f));
            foreach (var renderer in candidates)
                if (ReadGeometry(renderer)) before[renderer] = WorldTip(renderer);
            owner.EvaluateAt(cue.seconds);
            Renderer fastest = null, second = null;
            float speed = -1, secondSpeed = -1;
            foreach (var renderer in candidates)
            {
                if (!ReadGeometry(renderer) || !before.TryGetValue(renderer, out var previous)) continue;
                float movement = (WorldTip(renderer) - previous).sqrMagnitude;
                if (movement > speed) { second = fastest; secondSpeed = speed; fastest = renderer; speed = movement; }
                else if (movement > secondSpeed) { second = renderer; secondSpeed = movement; }
            }
            if (!fastest) return null;
            int count = !window.shield && owner.Move.weapon == TrumpWeaponManager.WeaponType.DualDaggers &&
                second && secondSpeed >= speed * .3f ? 2 : 1;
            while (trail.strips.Count < count) AddStrip(trail);
            for (int i = 0; i < trail.strips.Count; i++)
            {
                var strip = trail.strips[i];
                strip.source = i < count ? i == 0 ? fastest : second : null;
                strip.thrustAxis = Vector3.zero;
                strip.thrustDirection = Vector3.zero;
                strip.mesh.Clear();
                strip.meshRenderer.sharedMaterial = !window.shield && style.material ? style.material : material;
                if (!strip.source || !ReadGeometry(strip.source)) continue;
                ChooseEndpoints(strip);
            }
            // Cache a short authored sweep once per cue. Frame updates only interpolate
            // these points, avoiding CPU skinning every frame in WebGL.
            int sampleRate = style.weapon == TrumpWeaponManager.WeaponType.Assassin ? 240 : 120;
            int steps = Mathf.Clamp(Mathf.CeilToInt((window.end - window.start) * sampleRate), 2, 96);
            trail.sampleStep = (window.end - window.start) / steps;
            for (int sample = 0; sample <= steps; sample++)
            {
                owner.EvaluateAt(window.start + trail.sampleStep * sample);
                foreach (var strip in trail.strips)
                    if (strip.source && ReadGeometry(strip.source))
                    {
                        var matrix = strip.source.transform.localToWorldMatrix;
                        Vector3 grip = matrix.MultiplyPoint3x4(geometry[strip.baseIndex]);
                        Vector3 tip = matrix.MultiplyPoint3x4(geometry[strip.tipIndex]);
                        float start = window.shield ? .62f : cue.group == "thrust_swing" ? style.thrustBladeStart : style.bladeStart;
                        strip.inner.Add(Vector3.Lerp(grip, tip, start)); strip.tip.Add(tip);
                    }
                SampledPoseCount++;
            }
            if (cue.group == "thrust_swing" && style.thrustWidth > 0)
                foreach (var strip in trail.strips)
                {
                    // A straight thrust sweeps a nearly zero-area, edge-on blade.
                    // Give that motion a narrow visible streak without moving the
                    // tip off the actual weapon or creating another particle cue.
                    Vector3 forward = view ? view.transform.forward : Vector3.forward;
                    if (strip.tip.Count > 0)
                        strip.thrustDirection = (strip.tip[0] - strip.inner[0]).normalized;
                    strip.thrustAxis = Vector3.Cross(forward, strip.thrustDirection).normalized;
                    if (strip.thrustAxis.sqrMagnitude < .5f) strip.thrustAxis = view ? view.transform.up : Vector3.up;
                    for (int i = 0; i < strip.inner.Count; i++)
                        strip.inner[i] = WidenThrust(strip.inner[i], strip.tip[i], strip.thrustAxis, style.thrustWidth);
                }
            trail.root.name = window.shield ? "Shield sweep" : style.weapon + " " + cue.group;
            trail.root.SetActive(true);
            Draw(trail, cue.seconds);
            return trail.root;
        }
        finally { if (owner && owner.Playing) owner.EvaluateAt(displayed); }
    }

    static bool IsShield(Renderer r) => r.name.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0;
    static Vector3 WidenThrust(Vector3 inner, Vector3 tip, Vector3 axis, float width)
    {
        float projected = Vector3.Dot(inner - tip, axis);
        if (Mathf.Abs(projected) >= width) return inner;
        return inner + axis * ((projected < 0 ? -width : width) - projected);
    }
    static bool Usable(Renderer r) => r && r.enabled && r.gameObject.activeInHierarchy &&
        r.name.IndexOf("case", StringComparison.OrdinalIgnoreCase) < 0;

    bool ReadGeometry(Renderer renderer)
    {
        Mesh mesh = null;
        if (renderer is SkinnedMeshRenderer calibratedSkin && calibratedSkin.sharedMesh)
        {
            var calibrated = Array.Find(staticBlades ?? Array.Empty<StaticBlade>(), b => b != null && b.mesh == calibratedSkin.sharedMesh);
            if (calibrated != null && calibrated.baseBones.Length > 0 && calibrated.tipBones.Length > 0)
            {
                if (!boneCache.TryGetValue(calibratedSkin,out var bones))
                {
                    bones = calibratedSkin.bones;
                    boneCache.Add(calibratedSkin,bones);
                }
                var inverse = renderer.transform.worldToLocalMatrix;
                geometry.Clear();
                geometry.Add(inverse.MultiplyPoint3x4(SkinPoint(calibrated.baseBones,bones)));
                geometry.Add(inverse.MultiplyPoint3x4(SkinPoint(calibrated.tipBones,bones)));
                return true;
            }
        }
        if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh)
        {
            if (!scratch) { scratch = new Mesh { name = "Weapon trail sample", hideFlags = HideFlags.HideAndDontSave }; scratch.MarkDynamic(); }
            skin.BakeMesh(scratch, true); mesh = scratch;
        }
        else
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter) mesh = filter.sharedMesh;
            var calibrated = Array.Find(staticBlades ?? Array.Empty<StaticBlade>(), b => b != null && b.mesh == mesh);
            if (calibrated != null)
            {
                geometry.Clear(); geometry.Add(calibrated.bladeBase); geometry.Add(calibrated.bladeTip);
                return true;
            }
        }
        geometry.Clear();
        if (!mesh || !mesh.isReadable) return false;
        mesh.GetVertices(geometry);
        return geometry.Count > 0;
    }

    static Vector3 SkinPoint(BonePoint[] samples, Transform[] bones)
    {
        Vector3 point = Vector3.zero;
        foreach (var sample in samples)
            if (sample.bone >= 0 && sample.bone < bones.Length && bones[sample.bone])
                point += bones[sample.bone].TransformPoint(sample.point) * sample.weight;
        return point;
    }

    Vector3 Grip(Renderer renderer)
    {
        var animator = attacker ? attacker.Animator : null;
        if (!animator || !animator.isHuman) return renderer.bounds.center;
        var left = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        var right = animator.GetBoneTransform(HumanBodyBones.RightHand);
        Vector3 center = renderer.transform.TransformPoint(geometry[0]);
        if (!left) return right ? right.position : center;
        if (!right) return left.position;
        return (left.position - center).sqrMagnitude < (right.position - center).sqrMagnitude ? left.position : right.position;
    }

    Vector3 WorldTip(Renderer renderer)
    {
        Vector3 grip = Grip(renderer), point = grip;
        float distance = -1;
        var matrix = renderer.transform.localToWorldMatrix;
        foreach (var vertex in geometry)
        {
            Vector3 world = matrix.MultiplyPoint3x4(vertex);
            float d = (world - grip).sqrMagnitude;
            if (d > distance) { distance = d; point = world; }
        }
        return point;
    }

    void ChooseEndpoints(Strip strip)
    {
        Vector3 grip = Grip(strip.source);
        var matrix = strip.source.transform.localToWorldMatrix;
        float near = float.PositiveInfinity, far = -1;
        for (int i = 0; i < geometry.Count; i++)
        {
            float distance = (matrix.MultiplyPoint3x4(geometry[i]) - grip).sqrMagnitude;
            if (distance < near) { near = distance; strip.baseIndex = i; }
            if (distance > far) { far = distance; strip.tipIndex = i; }
        }
    }

    void AddStrip(Trail trail)
    {
        var child = new GameObject("Swept blade"); child.transform.SetParent(trail.root.transform, false);
        var mesh = new Mesh { name = "Animated weapon ribbon", hideFlags = HideFlags.HideAndDontSave }; mesh.MarkDynamic();
        child.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = child.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        trail.strips.Add(new Strip { mesh = mesh, meshRenderer = renderer });
    }

    public void Advance(float seconds)
    {
        if (!isActiveAndEnabled || !float.IsFinite(seconds)) return;
        if (owner && attacker)
        {
            float limit = Mathf.Max(.25f, discontinuityDistance);
            if (owner.PlaybackId != playbackId ||
                (attacker.transform.position - lastRootPosition).sqrMagnitude > limit * limit)
            {
                Clear();
                return;
            }
            lastRootPosition = attacker.transform.position;
        }
        float displayed = owner && owner.Playing ? owner.SampleTime : seconds;
        bool sample = owner && owner.Playing && Mathf.Abs(displayed-seconds) > .000001f;
        try
        {
            if (sample) owner.EvaluateAt(seconds);
            foreach (var trail in pool)
                if (trail.root && trail.root.activeSelf)
                {
                    trail.clock = seconds;
                    if (seconds > trail.window.end + trail.style.lifetime) trail.root.SetActive(false);
                    else Draw(trail, seconds);
                }
        }
        finally { if (sample && owner && owner.Playing) owner.EvaluateAt(displayed); }
    }

    void Draw(Trail trail, float seconds)
    {
        float life = trail.window.shield ? .07f : trail.style.lifetime;
        float from = Mathf.Max(trail.window.start, seconds - life), to = Mathf.Min(seconds, trail.window.end);
        foreach (var strip in trail.strips)
        {
            vertices.Clear(); colors.Clear(); uvs.Clear(); triangles.Clear();
            if (strip.tip.Count < 2 || to <= from) { strip.mesh.Clear(); continue; }
            Add(from);
            for (float t = trail.window.start + trail.sampleStep; t < to; t += trail.sampleStep)
                if (t > from) Add(t);
            Add(to);
            for (int i = 0; i < vertices.Count / 2 - 1; i++)
            {
                int v = i * 2;
                triangles.Add(v); triangles.Add(v + 1); triangles.Add(v + 3);
                triangles.Add(v); triangles.Add(v + 3); triangles.Add(v + 2);
            }
            strip.mesh.Clear(); strip.mesh.SetVertices(vertices); strip.mesh.SetColors(colors);
            strip.mesh.SetUVs(0, uvs); strip.mesh.SetTriangles(triangles, 0); strip.mesh.RecalculateBounds();

            void Add(float time)
            {
                float index = Mathf.Clamp((time - trail.window.start) / trail.sampleStep, 0, strip.tip.Count - 1);
                int lo = Mathf.Min(Mathf.FloorToInt(index), strip.tip.Count - 2); float blend = index - lo;
                // The pool has an identity transform. Old points remain in world space
                // instead of rotating an entire crescent with the current grip.
                Vector3 inner = Vector3.Lerp(strip.inner[lo], strip.inner[lo + 1], blend);
                Vector3 tip = Vector3.Lerp(strip.tip[lo], strip.tip[lo + 1], blend);
                // Pin the leading edge to the current blade pose even at a sharp
                // change of direction between authored samples.
                if (time == seconds && owner && owner.Playing && strip.source && ReadGeometry(strip.source))
                {
                    var matrix = strip.source.transform.localToWorldMatrix;
                    Vector3 grip = matrix.MultiplyPoint3x4(geometry[strip.baseIndex]);
                    tip = matrix.MultiplyPoint3x4(geometry[strip.tipIndex]);
                    float start = trail.window.shield ? .62f : trail.window.cue.group == "thrust_swing" ? trail.style.thrustBladeStart : trail.style.bladeStart;
                    inner = Vector3.Lerp(grip,tip,start);
                    if (strip.thrustAxis.sqrMagnitude > .5f)
                        inner = WidenThrust(inner, tip, strip.thrustAxis, trail.style.thrustWidth);
                }
                // The held part of a straight stab has almost no swept area.
                // A small glint tail along the blade keeps it readable; its
                // leading point still sits exactly on the sampled weapon tip.
                if (strip.thrustDirection.sqrMagnitude > .5f)
                {
                    Vector3 tail = strip.thrustDirection * (trail.style.thrustWidth * Mathf.Clamp01((to - time) / life));
                    inner -= tail; tip -= tail;
                }
                float freshness = Mathf.Clamp01(1 - (seconds - time) / life);
                // Collapse the old ribbon toward its sampled tip for a clean tail.
                // The leading edge and every sampled blade tip stay on the real weapon path.
                inner = Vector3.Lerp(tip, inner, Mathf.SmoothStep(.08f, 1, freshness));
                vertices.Add(inner);
                vertices.Add(tip);
                Color color = trail.window.shield ? new Color(1, .88f, .48f, .65f) : trail.style.color;
                color.a *= freshness * freshness;
                colors.Add(color); colors.Add(color);
                uvs.Add(new Vector2(0, time)); uvs.Add(new Vector2(1, time));
            }
        }
    }

    public void End(FrankBattlePairPlayback playback)
    {
        if (owner != playback) return;
        owner = null; attacker = null;
    }

    void LateUpdate()
    {
        if (owner && owner.Playing) return;
        foreach (var trail in pool)
            if (trail.root && trail.root.activeSelf)
            {
                trail.clock += Time.deltaTime;
                if (trail.clock > trail.window.end + trail.style.lifetime) trail.root.SetActive(false);
                else Draw(trail, trail.clock);
            }
    }

    public void Clear()
    {
        foreach (var trail in pool)
        {
            if (trail.root) trail.root.SetActive(false);
            foreach (var strip in trail.strips)
            {
                strip.inner.Clear();
                strip.tip.Clear();
                strip.source = null;
                strip.mesh.Clear();
            }
        }
        playbackId = -1;
        boneCache.Clear();
        owner = null; attacker = null;
    }
    void OnDisable() => Clear();
    void OnDestroy()
    {
        foreach (var trail in pool) foreach (var strip in trail.strips) Dispose(strip.mesh);
        Dispose(scratch);
        if (poolRoot) Dispose(poolRoot.gameObject);
    }
    static void Dispose(UnityEngine.Object item)
    {
        if (!item) return;
        if (Application.isPlaying) Destroy(item); else DestroyImmediate(item);
    }
}
