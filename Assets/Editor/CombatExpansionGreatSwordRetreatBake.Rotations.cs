using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordRetreatBake
    {
        static AnimationCurve ExplicitCurve(Keyframe[] numericKeys)
        {
            var curve = new AnimationCurve(numericKeys);
            for (int index = 0; index < curve.length; index++)
            {
                AnimationUtility.SetKeyBroken(curve, index, true);
                AnimationUtility.SetKeyLeftTangentMode(curve, index, AnimationUtility.TangentMode.Free);
                AnimationUtility.SetKeyRightTangentMode(curve, index, AnimationUtility.TangentMode.Free);
            }
            // Tangent mode setters can recalculate slopes. Restore every numeric field after all setters finish.
            var frozen = curve.keys;
            for (int index = 0; index < frozen.Length; index++)
            {
                var original = numericKeys[index];
                frozen[index].time = original.time;
                frozen[index].value = original.value;
                frozen[index].inTangent = original.inTangent;
                frozen[index].outTangent = original.outTangent;
                frozen[index].inWeight = original.inWeight;
                frozen[index].outWeight = original.outWeight;
                frozen[index].weightedMode = original.weightedMode;
            }
            curve.keys = frozen;
            return curve;
        }

        static void ValidateQuaternionHemispheres(Segment schema, AnimationClip output, StringBuilder report)
        {
            int checkedPairs = 0;
            foreach (var binding in schema.bindings.Values.Where(b => b.propertyName == "m_LocalRotation.w"))
            {
                var curves = "xyzw".Select(axis => AnimationUtility.GetEditorCurve(output,
                    schema.bindings[binding.path + "|m_LocalRotation." + axis])).ToArray();
                var times = curves.SelectMany(c => c.keys.Select(k => k.time)).Distinct().OrderBy(t => t);
                Quaternion? previous = null;
                foreach (float time in times)
                {
                    var current = new Quaternion(curves[0].Evaluate(time), curves[1].Evaluate(time),
                        curves[2].Evaluate(time), curves[3].Evaluate(time));
                    RequireQuaternion(current, binding.path);
                    if (previous.HasValue)
                    {
                        float dot = Quaternion.Dot(previous.Value.normalized, current.normalized);
                        if (dot < 0)
                            throw new InvalidOperationException(FormattableString.Invariant(
                                $"Unresolved quaternion hemisphere change: {binding.path}; time={time:R}; dot={dot:R}"));
                        checkedPairs++;
                    }
                    previous = current;
                }
            }
            report.AppendLine($"QUATERNION_HEMISPHERES checkedPairs={checkedPairs}; no global tangent regeneration");
        }

        static void PrepareHelperRotations(Segment[] segments, StringBuilder report)
        {
            foreach (string path in RotationHelpers)
            {
                // Compute every frame offset from untouched native curves before transforming any curves.
                for (int index = 0; index < segments.Length; index++)
                {
                    var segment = segments[index];
                    Quaternion offset = Quaternion.identity;
                    if (index > 0)
                    {
                        var previous = segments[index - 1];
                        var last = previous.helperRotations[path] * Rotation(previous, path, previous.clip.length);
                        var first = Rotation(segment, path, 0);
                        RequireQuaternion(last, path);
                        RequireQuaternion(first, path);
                        offset = (last.normalized * Quaternion.Inverse(first.normalized)).normalized;
                    }
                    segment.helperRotations.Add(path, offset);
                    report.AppendLine(FormattableString.Invariant(
                        $"HELPER_ROTATION_OFFSET segment={index}; path={path}; x={offset.x:R}; y={offset.y:R}; ") +
                        FormattableString.Invariant($"z={offset.z:R}; w={offset.w:R}; ") +
                        "left multiplication in local space; constant frame change; relative rotation preserved");
                }
                for (int index = 1; index < segments.Length; index++)
                    RotateHelperCurves(segments[index], path, report);
            }
        }

        static void RotateHelperCurves(Segment segment, string path, StringBuilder report)
        {
            var names = "xyzw".Select(axis => path + "|m_LocalRotation." + axis).ToArray();
            var curves = names.Select(name => segment.curves[name]).ToArray();
            var channels = curves.Select(curve => curve.keys).ToArray();
            ValidateQuaternionLayout(segment, path, channels);
            Quaternion offset = segment.helperRotations[path];
            for (int component = 0; component < 4; component++)
            {
                var keys = channels[component].ToArray();
                for (int index = 0; index < keys.Length; index++)
                {
                    Quaternion values = Components(channels, index, key => key.value);
                    Quaternion incoming = Components(channels, index, key => key.inTangent);
                    Quaternion outgoing = Components(channels, index, key => key.outTangent);
                    var key = keys[index];
                    // Quaternion multiplication is linear in these four components; never normalize derivatives.
                    key.value = (offset * values)[component];
                    key.inTangent = (offset * incoming)[component];
                    key.outTangent = (offset * outgoing)[component];
                    keys[index] = key;
                }
                segment.curves[names[component]] = new AnimationCurve(keys)
                {
                    preWrapMode = curves[component].preWrapMode,
                    postWrapMode = curves[component].postWrapMode
                };
            }
            report.AppendLine(FormattableString.Invariant(
                $"HELPER_ROTATION_CURVES source={AssetDatabase.GetAssetPath(segment.clip)}; path={path}; ") +
                $"keysPerComponent={channels[0].Length}; matched key times/weights; values and tangents linearly combined");
        }

        static Quaternion Components(Keyframe[][] channels, int index, Func<Keyframe, float> select)
        {
            return new Quaternion(select(channels[0][index]), select(channels[1][index]),
                select(channels[2][index]), select(channels[3][index]));
        }

        static void ValidateQuaternionLayout(Segment segment, string path, Keyframe[][] channels)
        {
            string location = AssetDatabase.GetAssetPath(segment.clip) + " | " + path;
            for (int component = 0; component < 4; component++)
            {
                if (channels[component].Length != channels[0].Length)
                    throw new InvalidOperationException("Unsupported quaternion component key counts: " + location +
                        "; counts=" + string.Join(",", channels.Select(c => c.Length)));
                for (int index = 0; index < channels[component].Length; index++)
                {
                    var key = channels[component][index];
                    var basis = channels[0][index];
                    bool incoming = (basis.weightedMode & WeightedMode.In) != 0;
                    bool outgoing = (basis.weightedMode & WeightedMode.Out) != 0;
                    if (key.time != basis.time || key.weightedMode != basis.weightedMode ||
                        (incoming && key.inWeight != basis.inWeight) ||
                        (outgoing && key.outWeight != basis.outWeight))
                        throw new InvalidOperationException("Unsupported quaternion time/weight layout: " + location +
                            "; component=" + "xyzw"[component] + "; key=" + index);
                    if (!float.IsFinite(key.value) || !float.IsFinite(key.inTangent) ||
                        !float.IsFinite(key.outTangent) || !float.IsFinite(key.time) ||
                        (incoming && !float.IsFinite(key.inWeight)) || (outgoing && !float.IsFinite(key.outWeight)))
                        throw new InvalidOperationException("Unsupported nonfinite quaternion key/tangent: " + location +
                            "; component=" + "xyzw"[component] + "; key=" + index);
                }
            }
        }

        static void RequireQuaternion(Quaternion value, string path)
        {
            double norm = (double)value.x * value.x + (double)value.y * value.y +
                (double)value.z * value.z + (double)value.w * value.w;
            if (!double.IsFinite(norm) || norm < .00000001)
                throw new InvalidOperationException("Invalid native quaternion: " + path);
        }

        static float QuaternionDegrees(Quaternion a, Quaternion b, string path)
        {
            RequireQuaternion(a, path);
            RequireQuaternion(b, path);
            double dot = (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z + (double)a.w * b.w;
            double aa = (double)a.x * a.x + (double)a.y * a.y + (double)a.z * a.z + (double)a.w * a.w;
            double bb = (double)b.x * b.x + (double)b.y * b.y + (double)b.z * b.z + (double)b.w * b.w;
            return (float)(2 * Math.Acos(Math.Min(1, Math.Abs(dot) / Math.Sqrt(aa * bb))) * 180 / Math.PI);
        }

        static float QuaternionJoinAngle(Segment previous, Segment next, string path)
        {
            return QuaternionDegrees(Rotation(previous, path, previous.clip.length), Rotation(next, path, 0), path);
        }

        static void ValidateQuaternionJoins(Segment[] segments, StringBuilder report)
        {
            foreach (var binding in segments[0].bindings.Values.Where(b => b.propertyName == "m_LocalRotation.w"))
                for (int index = 1; index < segments.Length; index++)
                {
                    float angle = QuaternionJoinAngle(segments[index - 1], segments[index], binding.path);
                    report.AppendLine(FormattableString.Invariant(
                        $"QUATERNION_JOIN segment={index}; path={binding.path}; angle={angle:R}; ") +
                        FormattableString.Invariant($"limit={RotationTolerance:R}deg; original incoming key retained"));
                    if (angle > RotationTolerance)
                        throw new InvalidOperationException(FormattableString.Invariant(
                            $"Quaternion join exceeds native angular limit: {binding.path}; segment={index}; ") +
                            FormattableString.Invariant($"angle={angle:R}deg; limit={RotationTolerance:R}deg"));
                }
        }

        static void ValidateQuaternionCurves(Segment[] segments, AnimationClip output, StringBuilder report)
        {
            float maximum = 0;
            foreach (var binding in segments[0].bindings.Values.Where(b => b.propertyName == "m_LocalRotation.w"))
            {
                var actual = "xyzw".Select(axis => AnimationUtility.GetEditorCurve(output,
                    segments[0].bindings[binding.path + "|m_LocalRotation." + axis])).ToArray();
                foreach (var segment in segments)
                    foreach (float time in NativeTimes(segment))
                    {
                        float outputTime = segment.start + time;
                        var value = new Quaternion(actual[0].Evaluate(outputTime), actual[1].Evaluate(outputTime),
                            actual[2].Evaluate(outputTime), actual[3].Evaluate(outputTime));
                        float angle = QuaternionDegrees(Rotation(segment, binding.path, time), value, binding.path);
                        maximum = Mathf.Max(maximum, angle);
                        if (angle > RotationTolerance)
                            throw new InvalidOperationException(FormattableString.Invariant(
                                $"Quaternion curve equivalence failed: {binding.path}; time={outputTime:R}; ") +
                                FormattableString.Invariant($"angle={angle:R}deg; limit={RotationTolerance:R}deg"));
                    }
            }
            report.AppendLine(FormattableString.Invariant(
                $"QUATERNION_CURVE_EQUIVALENCE 240Hz plus native keys; maxAngle={maximum:R}deg; ") +
                FormattableString.Invariant($"limit={RotationTolerance:R}deg"));
        }
    }
}
