using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget
{
    /// <summary>
    /// Plays individually directed camera takes after the tester evaluates both characters.
    /// The library owns shot design; this component only interpolates, blends and protects framing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class FrankCinematicCamera : MonoBehaviour
    {
        public FrankCombinationTester tester;
        public FrankCameraLibrary library;
        public bool cinematic = true;
        [Range(.05f, .15f)] public float viewportMargin = .07f;
        [Min(.1f)] public float transitionDuration = .65f;
        [Min(.03f)] public float minimumHeight = .3f;
        public float Zoom { get; set; } = 1f;
        public FrankCameraLibrary.Shot ActiveShot { get; private set; }
        public float SafetyDolly { get; private set; }
        public IReadOnlyList<Vector3> LastFramingPoints => framingPoints;

        readonly List<Vector3> framingPoints = new List<Vector3>(128);
        readonly List<FramingRenderer> framingRenderers = new List<FramingRenderer>();
        FrankTestDriver cachedMankeyDriver, cachedPepeDriver;
        FrankTestActor cachedMankey, cachedPepe;
        FrankCameraLibrary cachedLibrary;
        string currentKey;
        int cachedMode = -1, cachedMotion = -1;
        bool cachedPepeAttacks;
        bool initialized, cacheReady;
        float previousTime, transitionTime, activeTransitionDuration;
        bool transitioning;
        FrankCameraLibrary.Frame displayed, transitionFrom;

        sealed class FramingRenderer
        {
            public Renderer renderer;
            public Mesh baked;
        }

        public static string CurrentKey(FrankCombinationTester owner)
        {
            if (!owner) return null;
            string role = owner.pepeAttacks ? "/pepe" : "/mankey";
            if (owner.greatSword) return "execution/" + owner.greatSwordMotion + role;
            if (owner.gunSword) return "combo/" + owner.comboMotion + role;
            if (owner.unarmed) return "vol10/" + owner.unarmedMotion + role;
            return "frank/" + owner.motion + role;
        }

        /// <summary>Returns false when the caller should use its manual fallback view.</summary>
        public bool Apply(float deltaTime = 0, bool immediate = false)
        {
            if (!cinematic || !tester || !tester.demoCamera || !library) return false;
            int mode = tester.greatSword ? 3 : tester.gunSword ? 2 : tester.unarmed ? 1 : 0;
            int motion = mode == 3 ? tester.greatSwordMotion : mode == 2 ? tester.comboMotion : mode == 1 ? tester.unarmedMotion : tester.motion;
            bool changed = mode != cachedMode || motion != cachedMotion || tester.pepeAttacks != cachedPepeAttacks || library != cachedLibrary;
            string key = changed ? CurrentKey(tester) : currentKey;
            if (changed || ActiveShot == null)
            {
                ActiveShot = library.Find(key);
                cachedLibrary = library;
            }
            if (ActiveShot == null || ActiveShot.frames == null || ActiveShot.frames.Length == 0)
                return false;

            Camera camera = tester.demoCamera;
            camera.aspect = Mathf.Max(.05f, (float)camera.pixelWidth / Mathf.Max(1, camera.pixelHeight));
            var frame = Sample(ActiveShot, tester.time);
            // Preserve the baked motion envelope on narrow windows instead of chasing
            // each changing silhouette with a reactive portrait dolly.
            float aspectScale = Mathf.Max(1, 1.30f / camera.aspect);
            frame.position = frame.focus + (frame.position - frame.focus) * Mathf.Clamp(Zoom, .65f, 3f) * aspectScale;
            bool wrapped = initialized && !changed && tester.time + .001f < previousTime;
            if (initialized && (changed || wrapped) && !immediate)
            {
                transitionFrom = displayed;
                // Actor selections and loop restarts reset the source motion. Blend
                // viewing angle and lens relative to the incoming action center.
                if (changed)
                {
                    transitionFrom.position += frame.focus - transitionFrom.focus;
                    transitionFrom.focus = frame.focus;
                }
                float angle = Quaternion.Angle(LookRotation(transitionFrom.position, transitionFrom.focus), LookRotation(frame.position, frame.focus));
                float radiusRatio = Vector3.Distance(transitionFrom.position, transitionFrom.focus) / Mathf.Max(.1f, Vector3.Distance(frame.position, frame.focus));
                activeTransitionDuration = Mathf.Clamp(transitionDuration + angle * .009f + Mathf.Abs(Mathf.Log(radiusRatio)) * .22f + Vector3.Distance(transitionFrom.focus, frame.focus) * .045f, .65f, 1.45f);
                transitionTime = 0;
                transitioning = true;
                SafetyDolly = 0;
            }
            if (immediate || !initialized) transitioning = false;
            currentKey = key;
            cachedMode = mode;
            cachedMotion = motion;
            cachedPepeAttacks = tester.pepeAttacks;
            previousTime = tester.time;

            if (transitioning)
            {
                transitionTime += Mathf.Max(0, deltaTime);
                float u = Mathf.Clamp01(transitionTime / activeTransitionDuration);
                float blend = u * u * u * (u * (u * 6 - 15) + 10);
                Quaternion orbit = Quaternion.Slerp(LookRotation(transitionFrom.position, transitionFrom.focus),
                    LookRotation(frame.position, frame.focus), blend);
                float targetRadius = Vector3.Distance(frame.position, frame.focus);
                float radius = Mathf.Lerp(Vector3.Distance(transitionFrom.position, transitionFrom.focus), targetRadius, blend);
                float blendedFov = Mathf.LerpUnclamped(transitionFrom.fov, frame.fov, blend);
                // Incoming motion already contains an anticipated framing envelope. Do not
                // squeeze it inside the previous shot or lag behind its action center.
                // Keep coverage consistent while blending between different lens widths.
                float lensFit = Mathf.Max(1, Mathf.Tan(frame.fov * Mathf.Deg2Rad * .5f) / Mathf.Tan(blendedFov * Mathf.Deg2Rad * .5f));
                radius = Mathf.Max(radius, targetRadius * lensFit);
                frame.position = frame.focus - orbit * Vector3.forward * radius;
                frame.fov = blendedFov;
                if (u >= 1) transitioning = false;
            }

            camera.orthographic = false;
            camera.nearClipPlane = .03f;
            camera.farClipPlane = Mathf.Max(200, camera.farClipPlane);
            frame.fov = Mathf.Clamp(frame.fov, 20, 75);
            frame.position.y = Mathf.Max(minimumHeight, frame.position.y);
            CollectFramingPoints(framingPoints);
            var rotation = LookRotation(frame.position, frame.focus);
            float wanted = RequiredDolly(frame, rotation, camera.aspect, Mathf.Clamp(viewportMargin, .05f, .15f));
            float hardMinimum = RequiredDolly(frame, rotation, camera.aspect, .05f);
            if (immediate || !initialized) SafetyDolly = wanted;
            else if (deltaTime > 0)
                SafetyDolly = Mathf.Lerp(SafetyDolly, wanted, 1 - Mathf.Exp(-deltaTime / .25f));
            SafetyDolly = Mathf.Max(hardMinimum, SafetyDolly);
            frame.position -= rotation * Vector3.forward * SafetyDolly;

            // Low-angle shots must remain above the floor even after aspect/weapon fitting.
            // Refit after a height correction because it changes the view direction.
            for (int i = 0; i < 8; i++)
            {
                frame.position.y = Mathf.Max(minimumHeight, frame.position.y);
                rotation = LookRotation(frame.position, frame.focus);
                float correction = RequiredDolly(frame, rotation, camera.aspect, .0501f);
                if (correction <= .00001f) break;
                frame.position -= rotation * Vector3.forward * (correction + .0001f);
            }
            frame.position.y = Mathf.Max(minimumHeight, frame.position.y);
            camera.transform.SetPositionAndRotation(frame.position, LookRotation(frame.position, frame.focus));
            camera.fieldOfView = frame.fov;
            displayed = frame;
            initialized = true;
            return true;
        }

        static Quaternion LookRotation(Vector3 position, Vector3 focus)
        {
            Vector3 direction = focus - position;
            return direction.sqrMagnitude > .000001f ? Quaternion.LookRotation(direction, Vector3.up) : Quaternion.identity;
        }

        float RequiredDolly(FrankCameraLibrary.Frame frame, Quaternion rotation, float aspect, float margin)
        {
            float vertical = Mathf.Tan(frame.fov * Mathf.Deg2Rad * .5f) * (1 - margin * 2);
            float horizontal = vertical * aspect;
            Quaternion inverse = Quaternion.Inverse(rotation);
            float dolly = 0;
            foreach (Vector3 point in framingPoints)
            {
                Vector3 local = inverse * (point - frame.position);
                dolly = Mathf.Max(dolly, Mathf.Max(Mathf.Abs(local.x) / horizontal - local.z,
                    Mathf.Max(Mathf.Abs(local.y) / vertical - local.z, .08f - local.z)));
            }
            return dolly;
        }

        /// <summary>
        /// Current visible body and weapon bounds, reusable by the shot builder and validators.
        /// Baked target meshes avoid stale import bounds when the Animator is manually evaluated.
        /// </summary>
        public void CollectFramingPoints(List<Vector3> points)
        {
            points.Clear();
            if (!tester) return;
            if (!cacheReady || cachedMankey != tester.mankey || cachedPepe != tester.pepe ||
                cachedMankeyDriver != (tester.mankey ? tester.mankey.activeDriver : null) ||
                cachedPepeDriver != (tester.pepe ? tester.pepe.activeDriver : null))
                RefreshRenderers();
            foreach (var entry in framingRenderers)
            {
                var renderer = entry.renderer;
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh)
                {
                    skin.BakeMesh(entry.baked, true);
                    AppendBounds(points, entry.baked.bounds, skin.transform);
                }
                else AppendBounds(points, renderer.bounds, null);
            }
        }

        void RefreshRenderers()
        {
            ReleaseMeshes();
            cachedMankey = tester.mankey;
            cachedPepe = tester.pepe;
            cachedMankeyDriver = cachedMankey ? cachedMankey.activeDriver : null;
            cachedPepeDriver = cachedPepe ? cachedPepe.activeDriver : null;
            AddRenderers(cachedMankey);
            AddRenderers(cachedPepe);
            cacheReady = true;
        }

        void AddRenderers(FrankTestActor actor)
        {
            if (!actor) return;
            foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                var entry = new FramingRenderer { renderer = renderer };
                if (renderer is SkinnedMeshRenderer)
                    entry.baked = new Mesh { name = "Cinematic framing mesh", hideFlags = HideFlags.HideAndDontSave };
                framingRenderers.Add(entry);
            }
        }

        static void AppendBounds(List<Vector3> points, Bounds bounds, Transform localToWorld)
        {
            Vector3 center = bounds.center, extents = bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = center + Vector3.Scale(extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                if (localToWorld) corner = localToWorld.TransformPoint(corner);
                if (float.IsFinite(corner.x) && float.IsFinite(corner.y) && float.IsFinite(corner.z)) points.Add(corner);
            }
        }

        public static FrankCameraLibrary.Frame Sample(FrankCameraLibrary.Shot shot, float time)
        {
            var frames = shot.frames;
            if (frames.Length == 1 || time <= frames[0].time) return frames[0];
            int last = frames.Length - 1;
            if (time >= frames[last].time) return frames[last];
            int lo = 0, hi = last;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (frames[mid].time <= time) lo = mid; else hi = mid;
            }
            var a = frames[Mathf.Max(0, lo - 1)];
            var b = frames[lo];
            var c = frames[hi];
            var d = frames[Mathf.Min(last, hi + 1)];
            float interval = Mathf.Max(.000001f, c.time - b.time);
            float u = Mathf.Clamp01((time - b.time) / interval);
            float u2 = u * u, u3 = u2 * u;
            float h0 = 2 * u3 - 3 * u2 + 1, h1 = u3 - 2 * u2 + u;
            float h2 = -2 * u3 + 3 * u2, h3 = u3 - u2;
            float m0 = interval / Mathf.Max(.000001f, c.time - a.time);
            float m1 = interval / Mathf.Max(.000001f, d.time - b.time);
            return new FrankCameraLibrary.Frame
            {
                time = time,
                position = h0 * b.position + h1 * (c.position - a.position) * m0 + h2 * c.position + h3 * (d.position - b.position) * m1,
                focus = h0 * b.focus + h1 * (c.focus - a.focus) * m0 + h2 * c.focus + h3 * (d.focus - b.focus) * m1,
                fov = Mathf.Clamp(h0 * b.fov + h1 * (c.fov - a.fov) * m0 + h2 * c.fov + h3 * (d.fov - b.fov) * m1,
                    Mathf.Min(b.fov, c.fov), Mathf.Max(b.fov, c.fov))
            };
        }

        public void ResetView()
        {
            initialized = false;
            transitioning = false;
            SafetyDolly = 0;
        }

        void ReleaseMeshes()
        {
            foreach (var entry in framingRenderers)
                if (entry.baked)
                {
                    if (Application.isPlaying) Destroy(entry.baked);
                    else DestroyImmediate(entry.baked);
                }
            framingRenderers.Clear();
        }

        void OnDestroy() { ReleaseMeshes(); }
    }
}
