using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget
{
    public sealed partial class FrankCinematicCamera
    {
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
            if (battle)
            {
                RefreshBattleRenderers();
                AppendRendererPoints(points);
                return;
            }
            if (!tester) return;
            if (!cacheReady || cachedMankey != tester.mankey || cachedPepe != tester.pepe ||
                cachedMankeyDriver != (tester.mankey ? tester.mankey.activeDriver : null) ||
                cachedPepeDriver != (tester.pepe ? tester.pepe.activeDriver : null))
                RefreshRenderers();
            AppendRendererPoints(points);
        }

        void AppendRendererPoints(List<Vector3> points)
        {
            foreach (var entry in framingRenderers)
            {
                var renderer = entry.renderer;
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer is SkinnedMeshRenderer skin && skin.sharedMesh)
                {
                    // The unscaled bake already contains bone scale. Applying the
                    // renderer scale again enlarges Meme tenfold in this rig.
                    skin.BakeMesh(entry.baked, false);
                    if (entry.baked.vertexCount > 0)
                        AppendBounds(points, entry.baked.bounds, skin.transform);
                    else
                        AppendBounds(points, renderer.bounds, null);
                }
                else AppendBounds(points, renderer.bounds, null);
            }
        }

        void RefreshBattleRenderers()
        {
            var left = battle.leftCombat;
            var right = battle.rightCombat;
            var playback = left ? left.SourcePlayback : null;
            if (!playback && right) playback = right.SourcePlayback;
            var attack = playback ? playback.AttackerActor : null;
            var reaction = playback ? playback.ReceiverActor : null;
            if (cacheReady && cachedBattleLeft == left && cachedBattleRight == right &&
                cachedBattleAttack == attack && cachedBattleReaction == reaction) return;
            ReleaseMeshes();
            cachedBattleLeft = left;
            cachedBattleRight = right;
            cachedBattleAttack = attack;
            cachedBattleReaction = reaction;
            if (left) AddRenderers(left.gameObject);
            if (right) AddRenderers(right.gameObject);
            if (attack) AddRenderers(attack.gameObject);
            if (reaction) AddRenderers(reaction.gameObject);
            cacheReady = true;
        }

        void RefreshRenderers()
        {
            ReleaseMeshes();
            cachedMankey = tester.mankey;
            cachedPepe = tester.pepe;
            cachedMankeyDriver = cachedMankey ? cachedMankey.activeDriver : null;
            cachedPepeDriver = cachedPepe ? cachedPepe.activeDriver : null;
            if (cachedMankey) AddRenderers(cachedMankey.gameObject);
            if (cachedPepe) AddRenderers(cachedPepe.gameObject);
            cacheReady = true;
        }

        void AddRenderers(GameObject actor)
        {
            if (!actor) return;
            foreach (var renderer in actor.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer)) continue;
                if (!uniqueRenderers.Add(renderer)) continue;
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
                if (localToWorld)
                    corner = localToWorld.position + localToWorld.rotation * corner;
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
            ActiveShot = null;
            currentKey = null;
            battlePlayback = null;
            battlePlaybackId = -1;
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
            uniqueRenderers.Clear();
            cacheReady = false;
        }

        void OnDisable() { ResetView(); ReleaseMeshes(); }
        void OnDestroy() { ReleaseMeshes(); }
    }
}
