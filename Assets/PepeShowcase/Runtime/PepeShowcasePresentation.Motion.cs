using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcasePresentation
    {
        sealed class MotionFrame
        {
            public Vector3 hips;
            public Vector3 feet;
            public float footHeight;
        }

        sealed class Accent
        {
            public float seconds;
            public Vector3 position;
            public float strength;
            public bool ground;
            public bool landing;
            public Color color;
        }

        readonly List<Accent> accents = new List<Accent>();
        MotionFrame[] motionFrames;
        float motionStep;
        GameObject motionRoot;
        Mesh motionMesh;
        public int GroundAccentCount { get; private set; }
        public int BladeAccentCount { get; private set; }
        public int ActiveAccentTriangles { get; private set; }

        public void PrepareMotion(PepeAttackShowcase showcase)
        {
            ClearMotion();
            if (!showcase.library.motionAccentMaterial) return;
            var take = showcase.library.takes[showcase.TakeIndex];
            int count = Mathf.CeilToInt(take.Duration * 120) + 1;
            motionStep = take.Duration / (count - 1);
            motionFrames = new MotionFrame[count];
            float floor = showcase.character.transform.position.y;
            bool airborne = false;
            float lastGround = -.3f;
            for (int i = 0; i < count; i++)
            {
                float seconds = i * motionStep;
                showcase.SampleAttackPose(seconds);
                var left = showcase.character.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                var right = showcase.character.GetBoneTransform(HumanBodyBones.RightFoot).position;
                var frame = new MotionFrame
                {
                    hips = showcase.character.GetBoneTransform(HumanBodyBones.Hips).position,
                    feet = (left + right) * .5f,
                    footHeight = Mathf.Min(left.y, right.y) - floor
                };
                motionFrames[i] = frame;
                if (i == 0)
                {
                    airborne = frame.footHeight > .22f;
                    continue;
                }
                float rise = (frame.footHeight - motionFrames[i - 1].footHeight) / motionStep;
                float speed = Vector3.ProjectOnPlane(frame.hips - motionFrames[i - 1].hips,
                    Vector3.up).magnitude / motionStep;
                bool takeoff = !airborne && frame.footHeight > .22f && rise > .8f;
                bool landing = airborne && frame.footHeight < .12f && rise < -.6f;
                bool dash = !airborne && speed > 3.8f && seconds - lastGround > .3f;
                if (takeoff || landing || dash)
                {
                    Vector3 position = frame.feet;
                    position.y = floor + .035f;
                    accents.Add(new Accent
                    {
                        seconds = seconds,
                        position = position,
                        strength = landing ? 1.3f : takeoff ? .85f : .55f,
                        ground = true,
                        landing = landing,
                        color = new Color(.35f, .9f, 1, .5f)
                    });
                    GroundAccentCount++;
                    lastGround = seconds;
                }
                if (takeoff) airborne = true;
                if (landing) airborne = false;
            }
            var baked = new Mesh();
            try
            {
                foreach (var cue in take.cues)
                {
                    if (!cue.swing) continue;
                    showcase.SampleAttackPose(cue.seconds);
                    foreach (var blade in showcase.Actor.Pose.weaponRenderers)
                    {
                        if (!blade || !blade.enabled) continue;
                        string name = blade.name.ToLowerInvariant();
                        if (name.Contains("fx") || name.Contains("treak") || name.Contains("trail") ||
                            name.Contains("gun") || name.Contains("case") || name.Contains("sheath") ||
                            name.Contains("scabbard") || name.Contains("dummy")) continue;
                        if (!BladePoint(showcase, blade, baked, out var position)) continue;
                        accents.Add(new Accent
                        {
                            seconds = cue.seconds,
                            position = position,
                            strength = take.nativeWeaponEffects ? 1 : .8f,
                            color = take.nativeWeaponEffects ? new Color(.3f, 1, 1, .85f) :
                                new Color(1, .7f, .25f, .9f)
                        });
                        BladeAccentCount++;
                    }
                }
            }
            finally
            {
                Remove(baked);
            }
            motionRoot = new GameObject("Motion VFX - blade sparks, dash wakes and landing rings");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(motionRoot, gameObject.scene);
            motionMesh = new Mesh { name = "Deterministic showcase motion VFX" };
            motionMesh.MarkDynamic();
            motionRoot.AddComponent<MeshFilter>().sharedMesh = motionMesh;
            var output = motionRoot.AddComponent<MeshRenderer>();
            output.sharedMaterial = showcase.library.motionAccentMaterial;
            output.shadowCastingMode = ShadowCastingMode.Off;
            output.receiveShadows = false;
            showcase.SampleAttackPose(0);
        }

        static bool BladePoint(PepeAttackShowcase showcase, Renderer blade, Mesh baked, out Vector3 point)
        {
            point = Vector3.zero;
            var skin = blade as SkinnedMeshRenderer;
            var filter = blade.GetComponent<MeshFilter>();
            Mesh mesh = filter ? filter.sharedMesh : null;
            var original = skin ? skin.sharedMesh : mesh;
            if (!original || System.Array.IndexOf(showcase.library.excludedBladeMeshes, original) >= 0) return false;
            var calibration = System.Array.Find(showcase.library.bladeGeometry,
                item => item != null && item.mesh == original);
            if (calibration != null)
            {
                bool skinned = skin && calibration.baseBones.Length > 0 && calibration.tipBones.Length > 0;
                var bladeBase = skinned ? SkinPoint(calibration.baseBones, skin.bones) :
                    blade.transform.TransformPoint(calibration.bladeBase);
                var bladeTip = skinned ? SkinPoint(calibration.tipBones, skin.bones) :
                    blade.transform.TransformPoint(calibration.bladeTip);
                point = Vector3.Lerp(bladeBase, bladeTip, .85f);
                return Vector3.Distance(bladeBase, bladeTip) > .1f;
            }
            if (skin && skin.sharedMesh)
            {
                skin.BakeMesh(baked, false);
                mesh = baked;
            }
            if (!mesh || !mesh.isReadable) return false;
            var left = showcase.character.GetBoneTransform(HumanBodyBones.LeftHand).position;
            var right = showcase.character.GetBoneTransform(HumanBodyBones.RightHand).position;
            var center = blade.transform.TransformPoint(mesh.bounds.center);
            Vector3 hand = (left - center).sqrMagnitude < (right - center).sqrMagnitude ? left : right;
            float farthest = 0;
            foreach (var vertex in mesh.vertices)
            {
                var world = blade.transform.TransformPoint(vertex);
                float distance = (world - hand).sqrMagnitude;
                if (distance <= farthest || distance > 6.25f) continue;
                farthest = distance;
                point = Vector3.Lerp(hand, world, .85f);
            }
            return farthest > .01f;
        }

        void ClearMotion()
        {
            powerBlades.Clear();
            if (motionRoot) Remove(motionRoot);
            if (motionMesh) Remove(motionMesh);
            motionRoot = null;
            motionMesh = null;
            motionFrames = null;
            accents.Clear();
            GroundAccentCount = 0;
            BladeAccentCount = 0;
            ActiveAccentTriangles = 0;
        }
    }
}
