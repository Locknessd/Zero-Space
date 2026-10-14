using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcaseCamera
    {
        [Header("Cinematic attack reference (BOM trailer)")]
        public bool cinematicAttackReference;
        [Range(5, 12)] public float referenceDistance = 6.5f;
        [Range(.08f, .3f)] public float referenceFollowSmoothing = .14f;
        [Range(0, 1.5f)] public float referenceOrbitStrength = 1;
        public bool ReferenceReady { get; private set; }
        public float ReferenceNearest { get; private set; }
        public float ReferenceFarthest { get; private set; }

        sealed class ReferenceShot
        {
            public Vector3[] focus;
            public Quaternion[] rotation;
            public float[] distance;
            public float step;
        }

        ReferenceShot[] referenceShots;
        static readonly float[][] ReferenceYaws =
        {
            new[] { -26f, -18f, 10f, 5f, -8f },
            new[] { -8f, 4f, 20f, 14f, 12f },
            new[] { 12f, 3f, -24f, -22f, -16f },
            new[] { -16f, -4f, 15f, 2f, -12f }
        };

        bool UsesReference(PepeAttackShowcase showcase) =>
            cinematicAttackReference && showcase.library.bomTrailer;

        void ResetReferenceShots(int takes)
        {
            ReferenceReady = false;
            referenceShots = new ReferenceShot[takes];
            ReferenceNearest = 0;
            ReferenceFarthest = 0;
        }

        Quaternion ShotRotation(PepeAttackShowcase showcase, float seconds, float lift)
        {
            var take = showcase.library.takes[showcase.TakeIndex];
            if (!UsesReference(showcase) || take.lockCamera)
                return ViewRotation(take, take.lockCamera ? 0 : seconds / take.Duration);
            float progress = Mathf.Clamp01(seconds / take.Duration);
            int shot = Mathf.Min(showcase.TakeIndex, ReferenceYaws.Length - 1);
            var keys = ReferenceYaws[shot];
            float sample = progress * (keys.Length - 1);
            int index = Mathf.Min(Mathf.FloorToInt(sample), keys.Length - 2);
            float blend = SmoothReference(sample - index);
            float yaw = Mathf.Lerp(keys[index], keys[index + 1], blend);
            yaw = Mathf.Lerp(-12, yaw, referenceOrbitStrength);
            // Elevate the view for airborne action, then lower it toward the landing.
            // The envelope preserves matching entry/exit angles at each rig boundary.
            float envelope = Mathf.Sin(progress * Mathf.PI);
            float pitch = 6 + envelope * (3 + Mathf.Clamp(lift * 7, 0, 12));
            float roll = .8f * Mathf.Sin(progress * Mathf.PI * 2) * referenceOrbitStrength;
            return Quaternion.Euler(pitch, 180 + yaw, roll);
        }

        static float SmoothReference(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        static Vector3 ReferenceFocus(Vector3[] raw, Vector3 smooth, int index, float step)
        {
            float edge = Mathf.Min(index * step, (raw.Length - 1 - index) * step);
            float weight = SmoothReference(edge / .28f);
            // Preserve the exact carried pose at both ends while filtering jumps and torso bob.
            return Vector3.Lerp(raw[index], smooth, weight * .85f);
        }

        Quaternion ReferenceRotation(PepeAttackShowcase showcase)
        {
            var shot = referenceShots[showcase.TakeIndex];
            ReferenceSample(shot, showcase.AttackTime, out int index, out float weight);
            return Quaternion.Slerp(shot.rotation[index], shot.rotation[index + 1], weight);
        }

        float ReferenceDistance(PepeAttackShowcase showcase)
        {
            var shot = referenceShots[showcase.TakeIndex];
            ReferenceSample(shot, showcase.AttackTime, out int index, out float weight);
            return Mathf.Lerp(shot.distance[index], shot.distance[index + 1], weight);
        }

        static void ReferenceSample(ReferenceShot shot, float seconds, out int index, out float weight)
        {
            float sample = Mathf.Clamp(seconds / shot.step, 0, shot.distance.Length - 1);
            index = Mathf.Min(Mathf.FloorToInt(sample), shot.distance.Length - 2);
            weight = sample - index;
        }
    }
}
