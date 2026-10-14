using System;
using System.Collections.Generic;
using FrankRetarget;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcasePresentation
    {
        sealed class Sweep
        {
            public Renderer blade;
            public BattleWeaponTrails.StaticBlade calibration;
            public Transform[] bones;
            public PepeShowcaseLibrary.Cue cue;
            public Mesh mesh;
            public GameObject root;
            public Vector3[] bases;
            public Vector3[] tips;
            public float step;
        }

        readonly List<Sweep> sweeps = new List<Sweep>();
        readonly List<Vector3> sweepVertices = new List<Vector3>();
        readonly List<Color> sweepColors = new List<Color>();
        readonly List<Vector2> sweepUvs = new List<Vector2>();
        readonly List<int> sweepTriangles = new List<int>();
        PepeShowcaseLibrary.Take currentTake;
        Camera effectCamera;
        public int ActiveSweepCount { get; private set; }
        public int ActiveParticleCount { get; private set; }
        public int ActiveMuzzleParticleCount { get; private set; }

        void ResetWeaponTrails(FrankTestActor actor, PepeShowcaseLibrary.Take take,
            PepeShowcaseLibrary library, Camera camera)
        {
            ClearWeaponTrails();
            currentTake = take;
            effectCamera = camera;
            if (take.nativeWeaponEffects) return;
            if (!library.weaponTrailMaterial) return;
            foreach (var renderer in actor.Pose.weaponRenderers)
            {
                if (!renderer || !renderer.enabled) continue;
                string name = renderer.name.ToLowerInvariant();
                if (name.Contains("gun") || name.Contains("case") || name.Contains("sheath") ||
                    name.Contains("scabbard") || name.Contains("dummy")) continue;
                var skin = renderer as SkinnedMeshRenderer;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = skin ? skin.sharedMesh : filter ? filter.sharedMesh : null;
                if (!mesh || Array.IndexOf(library.excludedBladeMeshes, mesh) >= 0) continue;
                var calibration = Array.Find(library.bladeGeometry, item => item != null && item.mesh == mesh);
                if (calibration == null) continue;
                foreach (var cue in take.cues)
                {
                    if (!cue.swing || cue.effectEnd <= cue.effectStart) continue;
                    var sweep = new Sweep
                    {
                        blade = renderer,
                        calibration = calibration,
                        bones = skin ? skin.bones : null,
                        cue = cue,
                        mesh = new Mesh { name = "Showcase animated blade ribbon" },
                        root = new GameObject("Weapon sweep VFX")
                    };
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(sweep.root, gameObject.scene);
                    sweep.mesh.MarkDynamic();
                    sweep.root.AddComponent<MeshFilter>().sharedMesh = sweep.mesh;
                    var output = sweep.root.AddComponent<MeshRenderer>();
                    output.sharedMaterial = library.weaponTrailMaterial;
                    output.shadowCastingMode = ShadowCastingMode.Off;
                    output.receiveShadows = false;
                    int count = Mathf.Max(2, Mathf.CeilToInt((cue.effectEnd - cue.effectStart) * 120) + 1);
                    sweep.step = (cue.effectEnd - cue.effectStart) / (count - 1);
                    sweep.bases = new Vector3[count];
                    sweep.tips = new Vector3[count];
                    for (int i = 0; i < count; i++)
                    {
                        actor.Evaluate(take.sourceStartSeconds + cue.effectStart + sweep.step * i);
                        Segment(sweep, out sweep.bases[i], out sweep.tips[i]);
                    }
                    sweeps.Add(sweep);
                }
            }
            actor.Evaluate(take.sourceStartSeconds);
        }

        static void Segment(Sweep sweep, out Vector3 bladeBase, out Vector3 bladeTip)
        {
            var calibration = sweep.calibration;
            if (sweep.bones != null && calibration.baseBones.Length > 0 && calibration.tipBones.Length > 0)
            {
                bladeBase = SkinPoint(calibration.baseBones, sweep.bones);
                bladeTip = SkinPoint(calibration.tipBones, sweep.bones);
            }
            else
            {
                bladeBase = sweep.blade.transform.TransformPoint(calibration.bladeBase);
                bladeTip = sweep.blade.transform.TransformPoint(calibration.bladeTip);
            }
        }

        static Vector3 SkinPoint(BattleWeaponTrails.BonePoint[] points, Transform[] bones)
        {
            Vector3 result = Vector3.zero;
            foreach (var point in points)
                if (point.bone >= 0 && point.bone < bones.Length && bones[point.bone])
                    result += bones[point.bone].TransformPoint(point.point) * point.weight;
            return result;
        }

        void DrawWeaponTrails(float seconds)
        {
            ActiveSweepCount = 0;
            foreach (var sweep in sweeps)
            {
                float from = Mathf.Max(sweep.cue.effectStart, seconds - currentTake.trailLifetime);
                float to = Mathf.Min(seconds, sweep.cue.effectEnd);
                sweep.mesh.Clear();
                if (to <= from || !sweep.blade) continue;
                sweepVertices.Clear();
                sweepColors.Clear();
                sweepUvs.Clear();
                sweepTriangles.Clear();
                AddSweepPoint(sweep, from, seconds);
                for (float t = sweep.cue.effectStart + sweep.step; t < to; t += sweep.step)
                    if (t > from) AddSweepPoint(sweep, t, seconds);
                AddSweepPoint(sweep, to, seconds);
                for (int i = 0; i < sweepVertices.Count / 2 - 1; i++)
                {
                    int vertex = i * 2;
                    sweepTriangles.AddRange(new[] { vertex, vertex + 1, vertex + 3,
                        vertex, vertex + 3, vertex + 2 });
                }
                sweep.mesh.SetVertices(sweepVertices);
                sweep.mesh.SetColors(sweepColors);
                sweep.mesh.SetUVs(0, sweepUvs);
                sweep.mesh.SetTriangles(sweepTriangles, 0);
                sweep.mesh.RecalculateBounds();
                if (sweep.mesh.bounds.size.sqrMagnitude > .00001f) ActiveSweepCount++;
            }
        }

        void AddSweepPoint(Sweep sweep, float time, float now)
        {
            float sample = Mathf.Clamp((time - sweep.cue.effectStart) / sweep.step, 0, sweep.tips.Length - 1);
            int lo = Mathf.Min(Mathf.FloorToInt(sample), sweep.tips.Length - 2);
            Vector3 bladeBase = Vector3.Lerp(sweep.bases[lo], sweep.bases[lo + 1], sample - lo);
            Vector3 tip = Vector3.Lerp(sweep.tips[lo], sweep.tips[lo + 1], sample - lo);
            if (Mathf.Abs(time - now) < .000001f) Segment(sweep, out bladeBase, out tip);
            float freshness = Mathf.Clamp01(1 - (now - time) / currentTake.trailLifetime);
            Vector3 inner = Vector3.Lerp(bladeBase, tip, currentTake.bladeStart);
            inner = Vector3.Lerp(tip, inner, Mathf.SmoothStep(.08f, 1, freshness));
            var color = currentTake.trailColor;
            color.a *= freshness * freshness;
            sweepVertices.Add(inner);
            sweepVertices.Add(tip);
            sweepColors.Add(color);
            sweepColors.Add(color);
            sweepUvs.Add(Vector2.zero);
            sweepUvs.Add(Vector2.right);
        }

        void SpawnSlash(PepeShowcaseLibrary.Cue cue)
        {
            if (!currentTake.slashPrefab || cue.effectEnd <= cue.effectStart) return;
            Sweep fastest = null;
            float speed = -1;
            foreach (var sweep in sweeps)
                if (sweep.cue == cue)
                {
                    float movement = (sweep.tips[1] - sweep.tips[0]).sqrMagnitude;
                    if (movement <= speed) continue;
                    speed = movement;
                    fastest = sweep;
                }
            if (fastest == null) return;
            Segment(fastest, out var bladeBase, out var tip);
            var direction = Quaternion.Inverse(effectCamera.transform.rotation) *
                (fastest.tips[1] - fastest.tips[0]);
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            var rotation = effectCamera.transform.rotation * Quaternion.Euler(0, 0, angle);
            AddFlash(currentTake.slashPrefab, Vector3.Lerp(bladeBase, tip, .6f), rotation,
                currentTake.slashScale, .28f, fastest);
        }

        void ClearWeaponTrails()
        {
            foreach (var sweep in sweeps)
            {
                if (sweep.root) Remove(sweep.root);
                if (sweep.mesh) Remove(sweep.mesh);
            }
            sweeps.Clear();
            ActiveSweepCount = 0;
        }
    }
}
