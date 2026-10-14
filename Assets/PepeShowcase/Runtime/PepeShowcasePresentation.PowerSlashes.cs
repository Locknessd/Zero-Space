using System.Collections.Generic;
using FrankRetarget;
using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcasePresentation
    {
        sealed class PowerBlade
        {
            public Renderer renderer;
            public BattleWeaponTrails.StaticBlade calibration;
            public Transform[] bones;
            public Vector3[] bases;
            public Vector3[] tips;
        }

        readonly List<PowerBlade> powerBlades = new List<PowerBlade>();
        PepeShowcaseLibrary powerLibrary;
        float powerStep;
        Vector3[] powerRoots;
        public int ActivePowerRibbonCount { get; private set; }

        public void PreparePowerSlashes(PepeAttackShowcase showcase)
        {
            powerBlades.Clear();
            ActivePowerRibbonCount = 0;
            powerLibrary = showcase.library;
            if (!powerLibrary.bomTrailer) return;
            int count = Mathf.CeilToInt(currentTake.Duration * 120) + 1;
            powerStep = currentTake.Duration / (count - 1);
            powerRoots = new Vector3[count];
            foreach (var renderer in showcase.Actor.Pose.weaponRenderers)
            {
                if (!renderer || !renderer.enabled) continue;
                string name = renderer.name.ToLowerInvariant();
                if (name.Contains("fx") || name.Contains("treak") || name.Contains("trail") ||
                    name.Contains("gun") || name.Contains("case") || name.Contains("sheath") ||
                    name.Contains("scabbard") || name.Contains("dummy")) continue;
                var skin = renderer as SkinnedMeshRenderer;
                var filter = renderer.GetComponent<MeshFilter>();
                var mesh = skin ? skin.sharedMesh : filter ? filter.sharedMesh : null;
                if (!mesh) continue;
                if (System.Array.IndexOf(powerLibrary.excludedBladeMeshes, mesh) >= 0) continue;
                var calibration = System.Array.Find(powerLibrary.bladeGeometry,
                    item => item != null && item.mesh == mesh);
                powerBlades.Add(new PowerBlade
                {
                    renderer = renderer,
                    calibration = calibration,
                    bones = skin ? skin.bones : null,
                    bases = new Vector3[count],
                    tips = new Vector3[count]
                });
            }
            var baked = new Mesh();
            try
            {
                for (int i = 0; i < count; i++)
                {
                    showcase.SampleAttackPose(i * powerStep);
                    powerRoots[i] = showcase.character.GetBoneTransform(HumanBodyBones.Hips).position;
                    foreach (var blade in powerBlades)
                        SamplePowerBlade(showcase, blade, baked, out blade.bases[i], out blade.tips[i]);
                }
            }
            finally
            {
                Remove(baked);
            }
            showcase.SampleAttackPose(0);
            PrepareAirCuts(showcase);
        }

        static void SamplePowerBlade(PepeAttackShowcase showcase, PowerBlade blade, Mesh baked,
            out Vector3 bladeBase, out Vector3 tip)
        {
            var calibration = blade.calibration;
            if (calibration != null)
            {
                if (blade.bones != null && calibration.baseBones.Length > 0 && calibration.tipBones.Length > 0)
                {
                    bladeBase = SkinPoint(calibration.baseBones, blade.bones);
                    tip = SkinPoint(calibration.tipBones, blade.bones);
                }
                else
                {
                    bladeBase = blade.renderer.transform.TransformPoint(calibration.bladeBase);
                    tip = blade.renderer.transform.TransformPoint(calibration.bladeTip);
                }
                return;
            }
            var skin = blade.renderer as SkinnedMeshRenderer;
            var filter = blade.renderer.GetComponent<MeshFilter>();
            var mesh = filter ? filter.sharedMesh : null;
            if (skin)
            {
                skin.BakeMesh(baked, false);
                mesh = baked;
            }
            var left = showcase.character.GetBoneTransform(HumanBodyBones.LeftHand).position;
            var right = showcase.character.GetBoneTransform(HumanBodyBones.RightHand).position;
            var center = blade.renderer.transform.TransformPoint(mesh.bounds.center);
            var hand = (left - center).sqrMagnitude < (right - center).sqrMagnitude ? left : right;
            bladeBase = hand;
            tip = hand;
            float closest = float.MaxValue;
            float farthest = 0;
            foreach (var vertex in mesh.vertices)
            {
                var world = blade.renderer.transform.TransformPoint(vertex);
                float distance = (world - hand).sqrMagnitude;
                if (distance < closest)
                {
                    closest = distance;
                    bladeBase = world;
                }
                if (distance <= farthest || distance > 6.25f) continue;
                farthest = distance;
                tip = world;
            }
        }

        void DrawPowerSlashes(float seconds)
        {
            ActivePowerRibbonCount = 0;
            if (!powerLibrary || !powerLibrary.bomTrailer) return;
            foreach (var cue in currentTake.cues)
            {
                if (!cue.swing) continue;
                float start = cue.effectEnd > cue.effectStart ? cue.effectStart : Mathf.Max(0, cue.seconds - .025f);
                float end = cue.effectEnd > cue.effectStart ? cue.effectEnd : cue.seconds + .15f;
                float from = Mathf.Max(start, seconds - powerLibrary.slashAfterglow);
                float to = Mathf.Min(seconds, end);
                if (to <= from) continue;
                foreach (var blade in powerBlades)
                {
                    int before = accentIndices.Count;
                    Vector3 previousBase = Vector3.zero;
                    Vector3 previousTip = Vector3.zero;
                    Vector3 previousRoot = Vector3.zero;
                    float previousFreshness = 0;
                    bool first = true;
                    for (float time = from; time <= to + powerStep; time += powerStep)
                    {
                        float sampleTime = Mathf.Min(time, to);
                        float sample = Mathf.Clamp(sampleTime / powerStep, 0, blade.tips.Length - 1);
                        int lo = Mathf.Min(Mathf.FloorToInt(sample), blade.tips.Length - 2);
                        var a = Vector3.Lerp(blade.bases[lo], blade.bases[lo + 1], sample - lo);
                        var b = Vector3.Lerp(blade.tips[lo], blade.tips[lo + 1], sample - lo);
                        var root = Vector3.Lerp(powerRoots[lo], powerRoots[lo + 1], sample - lo);
                        float fresh = Mathf.Clamp01(1 - (seconds - sampleTime) / powerLibrary.slashAfterglow);
                        float travel = Vector3.Distance(previousTip, b);
                        // A teleport starts a new ribbon instead of joining distant sword poses.
                        if (!first && travel > .002f && travel < .65f &&
                            Vector3.Distance(previousRoot, root) < .22f)
                            PowerSpan(previousBase, previousTip, a, b, previousFreshness, fresh);
                        previousBase = a;
                        previousTip = b;
                        previousRoot = root;
                        previousFreshness = fresh;
                        first = false;
                        if (sampleTime >= to) break;
                    }
                    if (accentIndices.Count > before) ActivePowerRibbonCount++;
                }
            }
        }

        void PowerSpan(Vector3 a, Vector3 tipA, Vector3 b, Vector3 tipB, float fadeA, float fadeB)
        {
            float power = powerLibrary.slashPower;
            var edgeA = a + (tipA - a) * power;
            var edgeB = b + (tipB - b) * power;
            for (int band = 0; band < SlashBands.Length - 1; band++)
            {
                float inner = SlashBands[band];
                float outer = SlashBands[band + 1];
                var innerA = SlashColor(band, fadeA);
                var outerA = SlashColor(band + 1, fadeA);
                var innerB = SlashColor(band, fadeB);
                var outerB = SlashColor(band + 1, fadeB);
                int index = accentVertices.Count;
                Vertex(Vector3.LerpUnclamped(a, edgeA, inner), innerA);
                Vertex(Vector3.LerpUnclamped(a, edgeA, outer), outerA);
                Vertex(Vector3.LerpUnclamped(b, edgeB, inner), innerB);
                Vertex(Vector3.LerpUnclamped(b, edgeB, outer), outerB);
                Triangle(index, index + 1, index + 2);
                Triangle(index + 1, index + 3, index + 2);
            }
        }

        static readonly float[] SlashBands = { .12f, .24f, .6f, .84f, .92f, .97f, 1.02f, 1.12f };
        static readonly float[] SlashAlphas = { 0, .04f, .13f, .28f, .78f, .55f, .12f, 0 };

        static Color SlashColor(int band, float freshness)
        {
            var color = band == 4 || band == 5 ? new Color(1.4f, 2.5f, 2.9f) : new Color(.08f, 1.3f, 2.2f);
            color.a = SlashAlphas[band] * Mathf.Pow(freshness, 1.5f);
            return color;
        }
    }
}
