using System;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordRetreatBake
    {
        static void ReportNativeMismatch(Segment segment, AnimationClip output, string fighter, string path,
            float time, NativeSample source, NativeSample baked, StringBuilder report)
        {
            try
            {
                var defaults = segment.materializedCurves.Where(k => k.StartsWith(path + "|",
                    StringComparison.Ordinal));
                report.AppendLine(FormattableString.Invariant(
                    $"NATIVE_FAILURE_DETAIL fighter={fighter}; path={path}; sourceTime={time:R}; ") +
                    FormattableString.Invariant($"segmentStart={segment.start:R}; ") +
                    "materialized=" + string.Join(",", defaults));
                WriteNativeState("failure", segment, output, path, time, source, baked, report);
                float step = 1f / segment.clip.frameRate;
                foreach (float nearby in new[] { 0f, Mathf.Max(0, time - step),
                    Mathf.Min(segment.clip.length, time + step) }.Distinct())
                {
                    source.Evaluate(nearby);
                    source.bones[MotionRoot].localPosition += segment.offset;
                    ApplyHelperOffsets(segment, source.bones);
                    baked.Evaluate(segment.start + nearby);
                    WriteNativeState("nearby", segment, output, path, nearby, source, baked, report);
                }
                foreach (char axis in "xyzw")
                {
                    var binding = EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + axis);
                    WriteCurveDetail("original", segment.clip, binding, time, report);
                    WriteCurveDetail("output", output, binding, segment.start + time, report);
                    WriteCurveDetail("outputAtJoin", output, binding, segment.start + segment.clip.length, report);
                }
            }
            catch (Exception diagnosticFailure)
            {
                // Diagnostic problems must not conceal the original failed native equivalence check.
                report.AppendLine("NATIVE_FAILURE_DIAGNOSTIC_ERROR " + diagnosticFailure);
            }
        }

        static void WriteNativeState(string label, Segment segment, AnimationClip output, string path, float time,
            NativeSample source, NativeSample baked, StringBuilder report)
        {
            var expected = source.bones[path];
            var actual = baked.bones[path];
            float parentAngle = expected.parent && actual.parent ?
                QuaternionDegrees(expected.parent.rotation, actual.parent.rotation, path + "/parent") : 0;
            float localAngle = QuaternionDegrees(expected.localRotation, actual.localRotation, path);
            report.AppendLine(FormattableString.Invariant(
                $"NATIVE_ROTATION_STATE sample={label}; sourceTime={time:R}; localAngle={localAngle:R}; ") +
                FormattableString.Invariant($"parentWorldAngle={parentAngle:R}; ") +
                $"expectedLocal={expected.localRotation.ToString("F9")}; actualLocal={actual.localRotation.ToString("F9")}; " +
                $"expectedWorld={expected.rotation.ToString("F9")}; actualWorld={actual.rotation.ToString("F9")}");
            var curves = "xyzw".Select(axis => AnimationUtility.GetEditorCurve(output,
                EditorCurveBinding.FloatCurve(path, typeof(Transform), "m_LocalRotation." + axis))).ToArray();
            if (curves.Any(c => c == null))
            {
                report.AppendLine("OUTPUT_QUATERNION_CURVES missing quaternion components at " + path);
                return;
            }
            float outputTime = segment.start + time;
            var evaluated = new Quaternion(curves[0].Evaluate(outputTime), curves[1].Evaluate(outputTime),
                curves[2].Evaluate(outputTime), curves[3].Evaluate(outputTime));
            float curveToNative = QuaternionDegrees(evaluated, actual.localRotation, path);
            float curveToExpected = QuaternionDegrees(evaluated, expected.localRotation, path);
            report.AppendLine($"OUTPUT_QUATERNION_CURVES value={evaluated.ToString("F9")}; " +
                FormattableString.Invariant(
                $"curveToNativeAngle={curveToNative:R}; curveToExpectedAngle={curveToExpected:R}"));
        }

        static void WriteCurveDetail(string label, AnimationClip clip, EditorCurveBinding binding, float time,
            StringBuilder report)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null)
            {
                report.AppendLine($"ROTATION_KEY source={label}; property={binding.propertyName}; absent=true");
                return;
            }
            var indices = Enumerable.Range(0, curve.length).OrderBy(i => Mathf.Abs(curve[i].time - time))
                .Take(4).OrderBy(i => i);
            foreach (int index in indices)
            {
                var key = curve[index];
                report.AppendLine(FormattableString.Invariant(
                    $"ROTATION_KEY source={label}; property={binding.propertyName}; index={index}; time={key.time:R}; ") +
                    FormattableString.Invariant(
                    $"value={key.value:R}; inTangent={key.inTangent:R}; outTangent={key.outTangent:R}; ") +
                    FormattableString.Invariant($"inWeight={key.inWeight:R}; outWeight={key.outWeight:R}; ") +
                    $"weightedMode={key.weightedMode}; broken={AnimationUtility.GetKeyBroken(curve, index)}; " +
                    $"leftMode={AnimationUtility.GetKeyLeftTangentMode(curve, index)}; " +
                    $"rightMode={AnimationUtility.GetKeyRightTangentMode(curve, index)}");
            }
        }
    }
}
