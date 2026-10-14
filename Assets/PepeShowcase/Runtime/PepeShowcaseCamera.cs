using FrankRetarget;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcaseCamera : MonoBehaviour
    {
        public Camera output;
        public float fieldOfView = 42;
        [Range(.02f, .15f)] public float margin = .065f;
        float distance;
        Vector3[] focusTrack;
        float trackStep;
        static readonly HumanBodyBones[] FramingBones =
        {
            HumanBodyBones.Head, HumanBodyBones.Hips, HumanBodyBones.LeftShoulder,
            HumanBodyBones.RightShoulder, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot
        };
        public float SequenceDistance { get; set; }
        public float TakeDistance => distance;
        public float SafetyDistance => bomDistanceReady ? bomShotDistance : Mathf.Max(distance, SequenceDistance);

        public void PrepareTake(PepeAttackShowcase showcase)
        {
            var take = showcase.library.takes[showcase.TakeIndex];
            int sampleRate = take.attackTrack || showcase.library.continuous ? 60 : 30;
            int samples = Mathf.CeilToInt(take.Duration * sampleRate) + 1;
            trackStep = take.Duration / (samples - 1);
            var raw = new Vector3[samples];
            var frames = new Bounds[samples];
            var viewFrames = showcase.library.bomTrailer ? new Bounds[samples] : null;
            var rotations = new Quaternion[samples];
            for (int i = 0; i < samples; i++)
            {
                showcase.SampleAttackPose(i * trackStep);
                var hips = showcase.character.GetBoneTransform(HumanBodyBones.Hips);
                var head = showcase.character.GetBoneTransform(HumanBodyBones.Head);
                raw[i] = Vector3.Lerp(hips.position, head.position, .35f);
                rotations[i] = ShotRotation(showcase, i * trackStep, raw[i].y - raw[0].y);
                frames[i] = FramingBounds(showcase);
                if (viewFrames != null)
                    viewFrames[i] = BomViewBounds(showcase, rotations[i]);
            }
            var lockedFocus = PlanLockedFocus(showcase, raw, frames);
            focusTrack = new Vector3[samples];
            float smoothing = UsesReference(showcase) ? referenceFollowSmoothing : .25f;
            int radius = Mathf.CeilToInt(smoothing * 2.4f / trackStep);
            for (int i = 0; i < samples; i++)
            {
                float total = 0;
                Vector3 point = Vector3.zero;
                for (int j = Mathf.Max(0, i - radius); j <= Mathf.Min(samples - 1, i + radius); j++)
                {
                    float t = (j - i) * trackStep / smoothing;
                    float weight = Mathf.Exp(-.5f * t * t);
                    point += raw[j] * weight;
                    total += weight;
                }
                focusTrack[i] = take.lockCamera ? lockedFocus : UsesReference(showcase) ?
                    ReferenceFocus(raw, point / total, i, trackStep) : showcase.library.continuous ? raw[i] :
                    take.attackTrack ? Vector3.MoveTowards(raw[i], point / total, .18f) : point / total;
                focusTrack[i] = PrepareNextLockedShot(showcase, focusTrack[i], i * trackStep);
            }
            distance = take.cameraDistance;
            float vertical = Mathf.Tan(fieldOfView * Mathf.Deg2Rad * .5f) * (1 - 2 * (margin + .025f));
            float horizontal = Mathf.Tan(fieldOfView * Mathf.Deg2Rad * .5f) * output.aspect * (1 - 2 * margin);
            var fits = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                var rotation = rotations[i];
                var inverse = Quaternion.Inverse(rotation);
                fits[i] = output.nearClipPlane;
                for (int corner = 0; corner < 8; corner++)
                {
                    var frame = viewFrames == null ? frames[i] : viewFrames[i];
                    var point = frame.center + Vector3.Scale(frame.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1,
                            (corner & 4) == 0 ? -1 : 1));
                    var local = viewFrames == null ? inverse * (point - focusTrack[i]) :
                        point - inverse * focusTrack[i];
                    fits[i] = Mathf.Max(fits[i], Mathf.Abs(local.x) / horizontal - local.z,
                        Mathf.Abs(local.y) / vertical - local.z, output.nearClipPlane - local.z);
                }
                distance = Mathf.Max(distance, fits[i]);
            }
            distance *= 1.025f;
            CacheBomDistance(showcase);
            CacheReferenceShot(showcase, rotations, fits, trackStep);
            showcase.SampleAttackPose(0);
        }

        Bounds FramingBounds(PepeAttackShowcase showcase)
        {
            var character = showcase.character;
            var hips = character.GetBoneTransform(HumanBodyBones.Hips);
            var bounds = new Bounds(hips.position, Vector3.one * .05f);
            foreach (var human in FramingBones)
            {
                var bone = character.GetBoneTransform(human);
                if (bone) bounds.Encapsulate(bone.position);
            }
            bounds.Expand(.12f);
            Vector3 desiredFocus = bounds.center;
            float weaponReach = Mathf.Max(1.65f, bounds.size.y * 1.25f);
            foreach (var renderer in showcase.Actor.Pose.weaponRenderers)
                if (renderer && renderer.enabled && renderer.gameObject.activeInHierarchy)
                {
                    string name = renderer.name.ToLowerInvariant();
                    if (name.Contains("fx") || name.Contains("treak") || name.Contains("trail")) continue;
                    if (WeaponBounds(renderer, out var weaponBounds) &&
                        Vector3.Distance(weaponBounds.center, desiredFocus) <= weaponReach)
                        bounds.Encapsulate(weaponBounds);
                }
            return bounds;
        }

        static Quaternion ViewRotation(PepeShowcaseLibrary.Take take, float progress)
        {
            float yaw = Mathf.Lerp(take.yawStart, take.yawEnd, Mathf.SmoothStep(0, 1, progress));
            var direction = Quaternion.Euler(-6, yaw, 0) * Vector3.forward;
            return Quaternion.LookRotation(-direction, Vector3.up);
        }

        public void Apply(PepeAttackShowcase showcase, float deltaTime)
        {
            if (focusTrack == null) PrepareTake(showcase);
            var take = showcase.library.takes[showcase.TakeIndex];
            if (bomDistanceReady) bomShotDistance = PlannedDistance(showcase);
            float sample = Mathf.Clamp(showcase.AttackTime / trackStep, 0, focusTrack.Length - 1);
            int index = Mathf.Min(Mathf.FloorToInt(sample), focusTrack.Length - 2);
            float t = sample - index;
            Vector3 a = focusTrack[Mathf.Max(0, index - 1)];
            Vector3 b = focusTrack[index];
            Vector3 c = focusTrack[index + 1];
            Vector3 d = focusTrack[Mathf.Min(focusTrack.Length - 1, index + 2)];
            var focus = .5f * (2 * b + (-a + c) * t +
                (2 * a - 5 * b + 4 * c - d) * t * t + (-a + 3 * b - 3 * c + d) * t * t * t);
            if (take.lockCamera) focus = focusTrack[0];
            var rotation = UsesReference(showcase) && ReferenceReady ? ReferenceRotation(showcase) :
                ViewRotation(take, take.lockCamera ? 0 : showcase.AttackTime / take.Duration);
            output.fieldOfView = fieldOfView;
            output.transform.SetPositionAndRotation(focus - rotation * Vector3.forward * SafetyDistance, rotation);
        }
    }
}
