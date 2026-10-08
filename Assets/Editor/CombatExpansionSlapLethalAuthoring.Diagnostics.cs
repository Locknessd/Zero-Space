using System;
using System.Linq;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapLethalAuthoring
    {
        static void ArchivePreviousProvenance()
        {
            string path = Output + "/BakeProvenance.txt";
            if (!File.Exists(path))
                return;
            string archive = Output + "/BakeProvenance.BeforeEncodingRepair." +
                DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ") + "." + Guid.NewGuid().ToString("N") + ".txt";
            File.Copy(path, archive, false);
        }

        static void DiagnoseRoundtrip(Scene scene, Source receiver, SampledSource[] tracks, StringBuilder report)
        {
            report.AppendLine("DIAGNOSTIC: inverse-bones = HumanPoseHandler.GetHumanPose; native-stream = " +
                "read-only IAnimationJob.GetMuscle, name-mapped including finger aliases. No importer mutation.");
            foreach (var track in tracks)
            {
                using var native = new PoseSampler(scene, receiver.path, track.source.clip);
                using var inverse = new PoseSampler(scene, receiver.path, track.source.clip);
                using var stream = new PoseSampler(scene, receiver.path, track.source.clip, true);
                var roundtrip = new PoseMetrics();
                var instrumentation = new PoseMetrics();
                float difference = 0;
                string muscle = "none";
                foreach (float time in ProbeTimes(track.source.clip.length))
                {
                    var expected = native.At(time);
                    var captured = stream.At(time);
                    instrumentation.Compare(expected, captured, Vector3.zero);
                    roundtrip.Compare(expected, inverse.InverseRoundtrip(time), Vector3.zero);
                    for (int i = 0; i < captured.muscles.Length; i++)
                    {
                        float delta = Mathf.Abs(captured.muscles[i] - captured.streamMuscles[i]);
                        if (delta <= difference)
                            continue;
                        difference = delta;
                        muscle = FormattableString.Invariant(
                            $"{HumanTrait.MuscleName[i]} binding={MuscleBindingName(i)} time={time:R}; inverse={captured.muscles[i]:R}; stream={captured.streamMuscles[i]:R}");
                    }
                }
                report.AppendLine("READ-ONLY STREAM INSTRUMENTATION " + track.source.guid + ": " + instrumentation);
                if (instrumentation.Score > 1)
                    throw new InvalidOperationException("Muscle capture graph changed native playback: " + instrumentation);
                report.AppendLine("DIRECT GetHumanPose/SetHumanPose (no curves/interpolation) " +
                    track.source.guid + ": " + roundtrip);
                report.AppendLine("INVERSE/STREAM MUSCLE deltaMax=" + difference.ToString("R") + "; " + muscle);
                var importer = AssetImporter.GetAtPath(track.source.path) as ModelImporter;
                report.AppendLine("SOURCE IMPORT " + track.source.guid + "; compression=" + importer.animationCompression +
                    "; rotationError=" + importer.animationRotationError + "; positionError=" +
                    importer.animationPositionError + "; comparison target is existing imported playback.");
                DiagnoseCurveKnots(scene, receiver, track, report);
            }
        }

        static void DiagnoseCurveKnots(Scene scene, Source receiver, SampledSource track, StringBuilder report)
        {
            // Prior evidence selects this coordinate convention; this is diagnostic, never an acceptance shortcut.
            foreach (bool streamMuscles in new[] { false, true })
            {
                var encoding = new PoseEncoding { normalizedMotion = true, streamMuscles = streamMuscles };
                var trial = EncodePoses(receiver, track.poses, encoding, "ExactKeyEncodingProbe", track.source.clip.length);
                try
                {
                    VerifyWrittenMuscles(track.poses, trial, streamMuscles, report);
                    var requested = ProbeTimes(track.source.clip.length);
                    var times = requested.Select(time => track.poses.OrderBy(p => Mathf.Abs(p.time - time))
                        .First().time).Distinct().OrderBy(time => time).ToArray();
                    var metrics = ComparePlayback(scene, receiver, track.source.clip, trial, times);
                    report.AppendLine("EXACT EXPORTED KEY (no between-key interpolation) " + encoding +
                        "; source=" + track.source.guid + "; " + metrics);
                    report.AppendLine("TRIAL clip=" + trial.name + "; fps=" + trial.frameRate +
                        "; bindingCount=" + AnimationUtility.GetCurveBindings(trial).Length +
                        "; in-memory editor curves; no model import or asset save during comparison.");
                }
                finally
                {
                    Object.DestroyImmediate(trial);
                }
            }
        }

        static void VerifyWrittenMuscles(MeasuredPose[] poses, AnimationClip trial, bool stream, StringBuilder report)
        {
            float error = 0;
            for (int i = 0; i < HumanTrait.MuscleCount; i++)
            {
                var binding = EditorCurveBinding.FloatCurve("", typeof(Animator), MuscleBindingName(i));
                var keys = AnimationUtility.GetEditorCurve(trial, binding).keys;
                if (keys.Length != poses.Length)
                    throw new InvalidOperationException("Export changed muscle key count: " + binding.propertyName);
                for (int key = 0; key < keys.Length; key++)
                {
                    if (keys[key].time != poses[key].time)
                        throw new InvalidOperationException("Export changed muscle key time: " + binding.propertyName);
                    float expected = (stream ? poses[key].streamMuscles : poses[key].muscles)[i];
                    error = Mathf.Max(error, Mathf.Abs(keys[key].value - expected));
                }
            }
            var serialized = new SerializedObject(trial);
            report.AppendLine("EXPORTED MUSCLE KEY READBACK deltaMax=" + error.ToString("R") +
                "; compressed=" + serialized.FindProperty("m_Compressed")?.boolValue +
                "; highQualityCurve=" + serialized.FindProperty("m_UseHighQualityCurve")?.boolValue +
                "; linear unweighted tangents; finger aliases verified by name.");
            if (error != 0)
                throw new InvalidOperationException("Export changed authored muscle key values.");
        }

        static string PoseDetail(string label, float time, Vector3 expected, Vector3 actual,
            Quaternion expectedRotation, Quaternion actualRotation, string local = "")
        {
            return FormattableString.Invariant(
                $"{label} time={time:R} worldPositionExpected={V(expected)} worldPositionActual={V(actual)} worldRotationExpected={Q(expectedRotation)} worldRotationActual={Q(actualRotation)}{local}");
        }

        static string V(Vector3 v)
        {
            return FormattableString.Invariant($"({v.x:R},{v.y:R},{v.z:R})");
        }

        static string Q(Quaternion q)
        {
            return FormattableString.Invariant($"({q.x:R},{q.y:R},{q.z:R},{q.w:R})");
        }
    }
}
