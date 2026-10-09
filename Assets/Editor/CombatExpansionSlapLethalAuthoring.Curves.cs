using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapLethalAuthoring
    {
        sealed class PoseEncoding
        {
            public bool bodyRelative, normalizedMotion, streamMuscles;
            public override string ToString()
            {
                return "muscles=" + (streamMuscles ? "native-stream" : "inverse-bones") + "; body=" + (bodyRelative ? "root-relative" : "world") +
                    "; MotionT=" + (normalizedMotion ? "metres/humanScale" : "metres");
            }
        }

        static AnimationClip EncodePoses(Source template, MeasuredPose[] poses, PoseEncoding encoding,
            string name, float duration)
        {
            string[] muscles = Enumerable.Range(0, HumanTrait.MuscleCount).Select(MuscleBindingName).ToArray();
            var keys = muscles.ToDictionary(m => m, m => new List<Keyframe>(poses.Length));
            foreach (string group in new[] { "Root", "Motion" })
                foreach (string channel in new[] { "T.x", "T.y", "T.z", "Q.x", "Q.y", "Q.z", "Q.w" })
                    keys.Add(group + channel, new List<Keyframe>(poses.Length));
            Quaternion previousBody = Quaternion.identity;
            Quaternion previousRoot = Quaternion.identity;
            for (int i = 0; i < poses.Length; i++)
            {
                var pose = poses[i];
                var body = encoding.bodyRelative ?
                    Quaternion.Inverse(pose.rootRotation) * (pose.body - pose.root) : pose.body;
                var bodyRotation = encoding.bodyRelative ?
                    Quaternion.Inverse(pose.rootRotation) * pose.bodyRotation : pose.bodyRotation;
                var rootRotation = pose.rootRotation;
                if (i > 0 && Quaternion.Dot(previousBody, bodyRotation) < 0)
                    bodyRotation = Negate(bodyRotation);
                if (i > 0 && Quaternion.Dot(previousRoot, rootRotation) < 0)
                    rootRotation = Negate(rootRotation);
                previousBody = bodyRotation;
                previousRoot = rootRotation;
                AddTransform(keys, "Root", pose.time, body / pose.scale, bodyRotation);
                AddTransform(keys, "Motion", pose.time,
                    pose.root / (encoding.normalizedMotion ? pose.scale : 1), rootRotation);
                for (int muscle = 0; muscle < muscles.Length; muscle++)
                    keys[muscles[muscle]].Add(new Keyframe(pose.time,
                        (encoding.streamMuscles ? pose.streamMuscles : pose.muscles)[muscle]));
            }
            var output = Object.Instantiate(template.clip);
            try
            {
                output.name = name;
                output.hideFlags = HideFlags.HideAndDontSave;
                output.ClearCurves();
                output.frameRate = 120;
                output.wrapMode = WrapMode.ClampForever;
                foreach (var pair in keys)
                    AnimationUtility.SetEditorCurve(output,
                        EditorCurveBinding.FloatCurve("", typeof(Animator), pair.Key), LinearCurve(pair.Value));
                AnimationUtility.SetAnimationEvents(output, Array.Empty<AnimationEvent>());
                var settings = AnimationUtility.GetAnimationClipSettings(output);
                settings.startTime = 0;
                settings.stopTime = duration;
                settings.loopTime = false;
                settings.loopBlend = false;
                settings.loopBlendOrientation = false;
                settings.loopBlendPositionY = false;
                settings.loopBlendPositionXZ = false;
                settings.cycleOffset = 0;
                AnimationUtility.SetAnimationClipSettings(output, settings);
                ValidateClipStructure(output, duration);
                return output;
            }
            catch
            {
                Object.DestroyImmediate(output);
                throw;
            }
        }

        static void AddTransform(Dictionary<string, List<Keyframe>> keys, string group, float time,
            Vector3 position, Quaternion rotation)
        {
            for (int axis = 0; axis < 3; axis++)
                keys[group + "T." + "xyz"[axis]].Add(new Keyframe(time, position[axis]));
            for (int axis = 0; axis < 4; axis++)
                keys[group + "Q." + "xyzw"[axis]].Add(new Keyframe(time, rotation[axis]));
        }

        static float[] SampleTimes(float duration, float contact, float preserve)
        {
            var times = new SortedSet<float> { 0, contact, preserve, preserve + BlendDuration, duration };
            for (int i = 1; i < Mathf.CeilToInt(duration * 120); i++)
                times.Add(i / 120f);
            foreach (float boundary in new[] { .70f, .86f, 1.41f, FallDuration })
                times.Add(preserve + boundary);
            return times.ToArray();
        }

        static float BlendWeight(float time, float preserve)
        {
            float weight = Mathf.Clamp01((time - preserve) / BlendDuration);
            return weight * weight * (3 - 2 * weight);
        }

        static float MapFallTime(float elapsed, float endpoint)
        {
            if (elapsed <= 0)
                return FallStart;
            if (elapsed <= .70f)
                return Mathf.Lerp(FallStart, 1.3f, elapsed / .70f);
            if (elapsed <= .86f)
                return Mathf.Lerp(1.3f, 2f, (elapsed - .70f) / .16f);
            if (elapsed <= 1.41f)
                return Mathf.Lerp(2f, 2.55f, (elapsed - .86f) / .55f);
            return Mathf.Lerp(2.55f, endpoint, Mathf.Clamp01((elapsed - 1.41f) / .40f));
        }

        static AnimationCurve LinearCurve(List<Keyframe> samples)
        {
            for (int i = 0; i < samples.Count; i++)
            {
                var key = samples[i];
                key.inTangent = i == 0 ? 0 :
                    (key.value - samples[i - 1].value) / (key.time - samples[i - 1].time);
                key.outTangent = i == samples.Count - 1 ? 0 :
                    (samples[i + 1].value - key.value) / (samples[i + 1].time - key.time);
                key.weightedMode = WeightedMode.None;
                samples[i] = key;
            }
            var curve = new AnimationCurve(samples.ToArray());
            curve.preWrapMode = WrapMode.ClampForever;
            curve.postWrapMode = WrapMode.ClampForever;
            for (int i = 0; i < curve.length; i++)
            {
                AnimationUtility.SetKeyBroken(curve, i, true);
                AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.Linear);
            }
            return curve;
        }

        static Quaternion Negate(Quaternion value)
        {
            return new Quaternion(-value.x, -value.y, -value.z, -value.w);
        }

        static void WriteTimeMap(int sequence, float[] times, float preserve, float fallEndpoint)
        {
            var csv = new StringBuilder("outputTime,receiverTime,fallTime,fallWeight,phase\n");
            foreach (float time in times)
            {
                string phase = time <= preserve ? "original" : time < preserve + BlendDuration ? "blend" :
                    time < preserve + FallDuration ? "fall" : "hold";
                csv.AppendLine(FormattableString.Invariant(
                    $"{time:R},{time:R},{MapFallTime(time - preserve, fallEndpoint):R},{BlendWeight(time, preserve):R},{phase}"));
            }
            System.IO.File.WriteAllText(Output + "/Sequence" + (sequence + 1) + ".TimeMap.csv", csv.ToString());
        }
    }
}
