using System.Collections.Generic;
using FrankRetarget;
using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcasePresentation
    {
        sealed class MuzzleBurst
        {
            public PepeShowcaseLibrary.Cue cue;
            public Renderer gun;
            public Vector3 localPosition;
            public Vector3 localDirection;
            public Vector3 firedPosition;
            public Vector3 firedDirection;
        }

        readonly List<MuzzleBurst> muzzleBursts = new List<MuzzleBurst>();
        readonly HashSet<PepeShowcaseLibrary.Cue> visibleMuzzleCues = new HashSet<PepeShowcaseLibrary.Cue>();
        bool enhancedMuzzle;
        public int ActiveMuzzleBurstCount { get; private set; }
        public Vector3 RenderedMuzzlePosition { get; private set; }
        public bool IsMuzzleCueVisible(PepeShowcaseLibrary.Cue cue) => visibleMuzzleCues.Contains(cue);

        public static bool TryMuzzlePose(FrankTestActor actor, out Renderer gun,
            out Vector3 position, out Vector3 direction)
        {
            gun = FindGun(actor);
            position = Vector3.zero;
            direction = Vector3.forward;
            if (!gun) return false;
            var filter = gun.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh) return false;
            var bounds = filter.sharedMesh.bounds;
            int axis = bounds.size.x > bounds.size.y ? 0 : 1;
            if (bounds.size.z > bounds.size[axis]) axis = 2;
            var positive = bounds.center;
            var negative = bounds.center;
            positive[axis] = bounds.max[axis];
            negative[axis] = bounds.min[axis];
            positive = gun.transform.TransformPoint(positive);
            negative = gun.transform.TransformPoint(negative);
            var hand = actor.character.GetBoneTransform(HumanBodyBones.RightHand);
            Vector3 grip = hand ? hand.position : gun.transform.position;
            bool forward = (positive - grip).sqrMagnitude > (negative - grip).sqrMagnitude;
            position = forward ? positive : negative;
            direction = (forward ? positive - negative : negative - positive).normalized;
            return true;
        }

        void SpawnMuzzle(FrankTestActor actor)
        {
            if (!TryMuzzlePose(actor, out var gun, out var position, out var direction)) return;
            LastMuzzlePosition = position;
            LastMuzzleDirection = direction;
            AddFlash(currentMuzzlePrefab, position, Quaternion.LookRotation(direction), enhancedMuzzle ? 2.2f : 1,
                enhancedMuzzle ? .22f : .16f);
            flashes[flashes.Count - 1].root.transform.SetParent(gun.transform, true);
        }

        public void PrepareMuzzleBursts(PepeAttackShowcase showcase)
        {
            muzzleBursts.Clear();
            visibleMuzzleCues.Clear();
            ActiveMuzzleBurstCount = 0;
            enhancedMuzzle = showcase.library.bomTrailer;
            if (!enhancedMuzzle) return;
            foreach (var cue in currentTake.cues)
            {
                if (!cue.gun) continue;
                showcase.SampleAttackPose(cue.seconds);
                if (!TryMuzzlePose(showcase.Actor, out var gun, out var position, out var direction)) continue;
                muzzleBursts.Add(new MuzzleBurst
                {
                    cue = cue,
                    gun = gun,
                    localPosition = gun.transform.InverseTransformPoint(position),
                    localDirection = gun.transform.InverseTransformDirection(direction),
                    firedPosition = position,
                    firedDirection = direction
                });
            }
            showcase.SampleAttackPose(0);
        }

        void DrawMuzzleBursts(float seconds)
        {
            visibleMuzzleCues.Clear();
            ActiveMuzzleBurstCount = 0;
            if (!enhancedMuzzle) return;
            foreach (var burst in muzzleBursts)
            {
                float age = seconds - burst.cue.seconds;
                if (age < 0 || age > .38f) continue;
                var origin = burst.firedPosition;
                var direction = burst.firedDirection;
                if (age < .075f && burst.gun)
                {
                    origin = burst.gun.transform.TransformPoint(burst.localPosition);
                    direction = burst.gun.transform.TransformDirection(burst.localDirection).normalized;
                    RenderedMuzzlePosition = origin;
                }
                int before = accentIndices.Count;
                DrawMuzzleExplosion(origin, direction, age);
                if (accentIndices.Count == before) continue;
                ActiveMuzzleBurstCount++;
                visibleMuzzleCues.Add(burst.cue);
            }
        }
    }
}
