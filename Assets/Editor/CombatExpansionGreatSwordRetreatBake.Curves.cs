using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordRetreatBake
    {
        static void Prepare(Segment[] segments, StringBuilder report)
        {
            ReconcileBindings(segments, report);
            PrepareHelperOffsets(segments, report);
            for (int i = 0; i < segments.Length; i++)
            {
                var segment = segments[i];
                if (i > 0)
                {
                    var previous = segments[i - 1];
                    segment.start = previous.start + previous.clip.length;
                    segment.offset = previous.offset + RootPosition(previous, previous.clip.length) -
                        RootPosition(segment, 0);
                }
                foreach (var binding in segment.bindings.Values.Where(b => b.propertyName == "m_LocalRotation.w"))
                {
                    float sign = 1;
                    if (i > 0)
                    {
                        var previous = segments[i - 1];
                        var last = Rotation(previous, binding.path, previous.clip.length);
                        var next = Rotation(segment, binding.path, 0);
                        sign = Quaternion.Dot(last, next) * previous.quaternionSigns[binding.path] < 0 ? -1 : 1;
                    }
                    segment.quaternionSigns.Add(binding.path, sign);
                }
                report.Append(FormattableString.Invariant(
                    $"SEGMENT {i}: start={segment.start:R}s; end={segment.start + segment.clip.length:R}s; "));
                report.AppendLine(FormattableString.Invariant(
                    $"duration={segment.clip.length:R}s; root={MotionRoot}; offset={segment.offset.ToString("F9")}"));
                report.AppendLine("Quaternion paths flipped: " + string.Join(",", segment.quaternionSigns
                    .Where(p => p.Value < 0).Select(p => p.Key)));
            }
        }

        static Vector3 RootPosition(Segment segment, float time)
        {
            return new Vector3(segment.curves[MotionRoot + "|m_LocalPosition.x"].Evaluate(time),
                segment.curves[MotionRoot + "|m_LocalPosition.y"].Evaluate(time),
                segment.curves[MotionRoot + "|m_LocalPosition.z"].Evaluate(time));
        }

        static Quaternion Rotation(Segment segment, string path, float time)
        {
            return new Quaternion(segment.curves[path + "|m_LocalRotation.x"].Evaluate(time),
                segment.curves[path + "|m_LocalRotation.y"].Evaluate(time),
                segment.curves[path + "|m_LocalRotation.z"].Evaluate(time),
                segment.curves[path + "|m_LocalRotation.w"].Evaluate(time));
        }

        static AnimationClip Build(Segment[] segments, StringBuilder report)
        {
            var output = Object.Instantiate(segments[0].clip);
            try
            {
                ValidateQuaternionJoins(segments, report);
                output.name = "GreatSword_Ambush_Retreat";
                output.hideFlags = HideFlags.None;
                output.ClearCurves();
                output.frameRate = segments.Max(s => s.clip.frameRate);
                output.wrapMode = WrapMode.ClampForever;
                var settings = AnimationUtility.GetAnimationClipSettings(output);
                settings.startTime = 0;
                settings.stopTime = segments.Sum(s => s.clip.length);
                settings.loopTime = false;
                settings.loopBlend = false;
                AnimationUtility.SetAnimationClipSettings(output, settings);
                foreach (string key in segments[0].curves.Keys.OrderBy(k => k))
                {
                    var binding = segments[0].bindings[key];
                    var keys = new List<Keyframe>();
                    foreach (var segment in segments)
                    {
                        var addition = SegmentKeys(segment, key);
                        if (keys.Count > 0)
                        {
                            var last = keys[keys.Count - 1];
                            var next = addition[0];
                            float residual = Mathf.Abs(last.value - next.value);
                            bool rotation = binding.propertyName.StartsWith("m_LocalRotation.");
                            if ((!rotation && residual > ValueTolerance) ||
                                Mathf.Abs(last.time - next.time) > TimeTolerance)
                                throw new InvalidOperationException(FormattableString.Invariant(
                                    $"Discontinuous join at {segment.start:R}s for {key}: residual={residual:R}"));
                            // Preserve the preceding phase exactly; use the next phase's outgoing derivative.
                            last.outTangent = next.outTangent;
                            last.outWeight = next.outWeight;
                            last.weightedMode = (last.weightedMode & WeightedMode.In) |
                                (next.weightedMode & WeightedMode.Out);
                            keys[keys.Count - 1] = last;
                            addition.RemoveAt(0);
                        }
                        keys.AddRange(addition);
                    }
                    var curve = ExplicitCurve(keys.ToArray());
                    curve.preWrapMode = WrapMode.ClampForever;
                    curve.postWrapMode = WrapMode.ClampForever;
                    AnimationUtility.SetEditorCurve(output, binding, curve);
                }
                var events = new List<AnimationEvent>();
                foreach (var segment in segments)
                    foreach (var item in AnimationUtility.GetAnimationEvents(segment.clip))
                    {
                        if (item.time < 0 || item.time > segment.clip.length)
                            throw new InvalidOperationException("Animation event outside native segment.");
                        item.time += segment.start;
                        events.Add(item);
                    }
                AnimationUtility.SetAnimationEvents(output, events.OrderBy(e => e.time).ToArray());
                // Signs were propagated per segment to both values and tangents. Do not regenerate whole-clip slopes.
                ValidateQuaternionHemispheres(segments[0], output, report);
                if (Mathf.Abs(output.length - settings.stopTime) > TimeTolerance || output.isLooping)
                    throw new InvalidOperationException("Output duration or nonlooping settings failed verification.");
                report.AppendLine($"STITCH curves={segments[0].curves.Count}; events={events.Count}; " +
                    "explicit broken/free tangents; numeric keys/weights retained; no global quaternion rewrite.");
                ValidateCurves(segments, output, report);
                return output;
            }
            catch
            {
                Object.DestroyImmediate(output);
                throw;
            }
        }

        static List<Keyframe> SegmentKeys(Segment segment, string name)
        {
            var binding = segment.bindings[name];
            var curve = segment.curves[name];
            float sign = binding.propertyName.StartsWith("m_LocalRotation.") ?
                segment.quaternionSigns[binding.path] : 1;
            float offset = 0;
            if (binding.path == MotionRoot && binding.propertyName.StartsWith("m_LocalPosition."))
                offset = segment.offset["xyz".IndexOf(binding.propertyName.Last())];
            if (binding.propertyName.StartsWith("m_LocalPosition.") &&
                segment.helperOffsets.TryGetValue(binding.path, out var helperOffset))
                offset += helperOffset["xyz".IndexOf(binding.propertyName.Last())];
            var result = curve.keys.ToList();
            if (result.Any(k => !float.IsFinite(k.time) || !float.IsFinite(k.value) ||
                float.IsNaN(k.inTangent) || float.IsNaN(k.outTangent)))
                throw new InvalidOperationException("Nonfinite native key: " + name);
            if (result[0].time < -TimeTolerance || result.Last().time > segment.clip.length + TimeTolerance)
                throw new InvalidOperationException("Native keys extend outside clip boundaries: " + name);
            AddEndpoint(result, 0, true, curve, name);
            AddEndpoint(result, segment.clip.length, false, curve, name);
            for (int i = 0; i < result.Count; i++)
            {
                var key = result[i];
                key.time += segment.start;
                key.value = sign * key.value + offset;
                if (float.IsFinite(key.inTangent))
                    key.inTangent *= sign;
                if (float.IsFinite(key.outTangent))
                    key.outTangent *= sign;
                result[i] = key;
            }
            return result;
        }

        static void AddEndpoint(List<Keyframe> keys, float time, bool first, AnimationCurve curve, string name)
        {
            int index = first ? 0 : keys.Count - 1;
            var key = keys[index];
            if (Mathf.Abs(key.time - time) <= TimeTolerance)
            {
                key.time = time;
                keys[index] = key;
                return;
            }
            var wrap = first ? curve.preWrapMode : curve.postWrapMode;
            if (wrap == WrapMode.Loop || wrap == WrapMode.PingPong)
                throw new InvalidOperationException("Endpoint requires unsupported wrapped extrapolation: " + name);
            // Imported constant channels can contain one key; extend their native clamped value.
            if (first)
            {
                key.inTangent = 0;
                keys[index] = key;
                keys.Insert(0, new Keyframe(time, key.value, 0, 0));
            }
            else
            {
                key.outTangent = 0;
                keys[index] = key;
                keys.Add(new Keyframe(time, key.value, 0, 0));
            }
        }

        static void ValidateCurves(Segment[] segments, AnimationClip output, StringBuilder report)
        {
            float maximum = 0;
            foreach (string name in segments[0].curves.Keys)
            {
                if (segments[0].bindings[name].propertyName.StartsWith("m_LocalRotation."))
                    continue;
                var actual = AnimationUtility.GetEditorCurve(output, segments[0].bindings[name]);
                foreach (var segment in segments)
                {
                    var expected = new AnimationCurve(SegmentKeys(segment, name).ToArray());
                    int count = Mathf.CeilToInt(segment.clip.length * 240);
                    for (int i = 0; i <= count; i++)
                    {
                        float time = segment.start + Mathf.Min(i / 240f, segment.clip.length);
                        float delta = Mathf.Abs(actual.Evaluate(time) - expected.Evaluate(time));
                        maximum = Mathf.Max(maximum, delta);
                        if (delta > ValueTolerance * 2)
                            throw new InvalidOperationException(FormattableString.Invariant(
                                $"Curve equivalence failed: {name}; time={time:R}; delta={delta:R}"));
                    }
                }
            }
            report.AppendLine(FormattableString.Invariant($"CURVE_EQUIVALENCE 240Hz maxScalarError={maximum:R}"));
            ValidateQuaternionCurves(segments, output, report);
        }
    }
}
