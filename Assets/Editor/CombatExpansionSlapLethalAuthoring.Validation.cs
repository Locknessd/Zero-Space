using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapLethalAuthoring
    {
        const float PosePositionLimit = .002f;
        const float PoseAngleLimit = .5f;

        sealed class PoseMetrics
        {
            public float position, angle, root, body;
            public string worstPosition = "none", worstAngle = "none";
            public int samples;
            public float Score => Mathf.Max(position / PosePositionLimit, angle / PoseAngleLimit);

            public void Compare(MeasuredPose expected, MeasuredPose actual, Vector3 shift)
            {
                if (!expected.boneNames.SequenceEqual(actual.boneNames))
                    throw new InvalidOperationException("Empirical rigs have different bone schemas.");
                samples++;
                root = Mathf.Max(root, Vector3.Distance(expected.root + shift, actual.root));
                body = Mathf.Max(body, Vector3.Distance(expected.body + shift, actual.body));
                Track(expected.root + shift, actual.root, expected.rootRotation, actual.rootRotation,
                    "AnimatorRoot", actual.time);
                Track(expected.body + shift, actual.body, expected.bodyRotation, actual.bodyRotation,
                    "Body", actual.time);
                for (int i = 0; i < expected.bones.Length; i++)
                    TrackBone(expected, actual, shift, i);
            }

            void TrackBone(MeasuredPose expected, MeasuredPose actual, Vector3 shift, int i)
            {
                string local = "";
                if (Vector3.Distance(expected.bones[i] + shift, actual.bones[i]) > position ||
                    Quaternion.Angle(expected.rotations[i], actual.rotations[i]) > angle)
                    local = " localPositionExpected=" + V(expected.localBones[i]) +
                        " localPositionActual=" + V(actual.localBones[i]) +
                        " localRotationExpected=" + Q(expected.localRotations[i]) +
                        " localRotationActual=" + Q(actual.localRotations[i]);
                Track(expected.bones[i] + shift, actual.bones[i], expected.rotations[i], actual.rotations[i],
                    expected.boneNames[i], actual.time, local);
            }

            public void Track(Vector3 expected, Vector3 actual, Quaternion expectedRotation,
                Quaternion actualRotation, string label, float time, string local = "")
            {
                float distance = Vector3.Distance(expected, actual);
                float degrees = Quaternion.Angle(expectedRotation, actualRotation);
                if (!Finite(expected) || !Finite(actual) || !Finite(expectedRotation) || !Finite(actualRotation))
                    throw new InvalidOperationException("Nonfinite comparison for " + label);
                if (distance > position)
                    worstPosition = PoseDetail(label, time, expected, actual, expectedRotation, actualRotation, local);
                if (degrees > angle)
                    worstAngle = PoseDetail(label, time, expected, actual, expectedRotation, actualRotation, local);
                position = Mathf.Max(position, distance);
                angle = Mathf.Max(angle, degrees);
            }

            public override string ToString()
            {
                return FormattableString.Invariant(
                    $"samples={samples}; positionMax={position:R}m; angleMax={angle:R}deg; rootMax={root:R}m; bodyMax={body:R}m; worstPosition=[{worstPosition}]; worstAngle=[{worstAngle}]");
            }
        }

        static PoseMetrics ComparePlayback(Scene scene, Source rig, AnimationClip native, AnimationClip canonical,
            float[] times)
        {
            using var expected = new PoseSampler(scene, rig.path, native);
            using var actual = new PoseSampler(scene, rig.path, canonical);
            var metrics = new PoseMetrics();
            foreach (float time in times)
                metrics.Compare(expected.At(time), actual.At(time), Vector3.zero);
            return metrics;
        }

        static void VerifyCandidate(Scene scene, Source receiver, Source fall, AnimationClip candidate,
            float preserve, Vector3 shift, float[] knots, StringBuilder report)
        {
            ValidateClipStructure(candidate, receiver.clip.length);
            using var original = new PoseSampler(scene, receiver.path, receiver.clip, true);
            using var incoming = new PoseSampler(scene, receiver.path, fall.clip, true);
            using var actual = new PoseSampler(scene, receiver.path, candidate, true);
            var prefix = new PoseMetrics();
            var tail = new PoseMetrics();
            var blend = new PoseMetrics();
            var hold = new PoseMetrics();
            float muscleError = 0;
            float inverseMuscleDiagnostic = 0;
            MeasuredPose held = null;
            var snapshots = new Dictionary<float, MeasuredPose>();
            foreach (float time in DenseTimes(candidate.length, knots))
            {
                var observed = actual.At(time);
                snapshots[time] = observed;
                if (time <= preserve)
                    prefix.Compare(original.At(time), observed, Vector3.zero);
                else if (time >= preserve + BlendDuration)
                    tail.Compare(incoming.At(MapFallTime(time - preserve, fall.clip.length)), observed, shift);
                else
                {
                    var expected = BlendMeasured(original.At(time),
                        incoming.At(MapFallTime(time - preserve, fall.clip.length)), time, preserve, shift);
                    blend.samples++;
                    blend.Track(expected.root, observed.root, expected.rootRotation, observed.rootRotation,
                        "blend root", time);
                    blend.Track(expected.body, observed.body, expected.bodyRotation, observed.bodyRotation,
                        "blend body", time);
                    muscleError = Mathf.Max(muscleError, StreamMuscleError(expected, observed));
                    for (int i = 0; i < expected.muscles.Length; i++)
                        inverseMuscleDiagnostic = Mathf.Max(inverseMuscleDiagnostic,
                            Mathf.Abs(expected.muscles[i] - observed.muscles[i]));
                }
                if (time >= preserve + FallDuration)
                {
                    if (held == null)
                        held = observed;
                    hold.Compare(held, observed, Vector3.zero);
                }
            }
            report.AppendLine("240Hz ORIGINAL PREFIX " + prefix);
            report.AppendLine("240Hz AUTHORED FALL VS NATIVE RETIMED SOURCE " + tail);
            report.AppendLine("240Hz BLEND BODY/ROOT " + blend + "; nativeStreamMuscleError=" + muscleError +
                "; inverseMuscleDiagnostic=" + inverseMuscleDiagnostic);
            report.AppendLine("240Hz HELD FINAL POSE " + hold);
            if (prefix.Score > 1 || tail.Score > 1 || blend.Score > 1 || hold.Score > 1 || muscleError > .003f)
                throw new InvalidOperationException("Candidate failed independent native pose/root preservation. " +
                    "See per-phase measurements in BakeProvenance.txt.");
            var reverse = new PoseMetrics();
            foreach (float time in ProbeTimes(candidate.length).Where(snapshots.ContainsKey).Reverse())
                reverse.Compare(snapshots[time], actual.At(time), Vector3.zero);
            report.AppendLine("REVERSE REPLAY " + reverse);
            if (reverse.Score > 1)
                throw new InvalidOperationException("Candidate reverse replay differed from forward playback: " + reverse);
        }

        static void ValidateClipStructure(AnimationClip output, float duration)
        {
            var bindings = AnimationUtility.GetCurveBindings(output);
            var settings = AnimationUtility.GetAnimationClipSettings(output);
            if (!output.humanMotion || output.legacy || output.isLooping || settings.loopTime ||
                AnimationUtility.GetAnimationEvents(output).Length != 0 ||
                Mathf.Abs(output.length - duration) > .00001f || bindings.Length != HumanTrait.MuscleCount + 14)
                throw new InvalidOperationException("Canonical Humanoid/duration/nonlooping/event/schema invariant failed.");
            foreach (var binding in bindings)
            {
                var curve = AnimationUtility.GetEditorCurve(output, binding);
                if (curve == null || curve.length < 2 || Mathf.Abs(curve.keys[0].time) > .000001f ||
                    Mathf.Abs(curve.keys.Last().time - duration) > .000001f)
                    throw new InvalidOperationException("Missing output curve endpoints: " + binding.propertyName);
                foreach (var key in curve.keys)
                    if (!float.IsFinite(key.value) || !float.IsFinite(key.time) ||
                        !float.IsFinite(key.inTangent) || !float.IsFinite(key.outTangent) ||
                        key.weightedMode != WeightedMode.None)
                        throw new InvalidOperationException("Nonfinite/weighted canonical curve: " + binding.propertyName);
            }
        }
    }
}
