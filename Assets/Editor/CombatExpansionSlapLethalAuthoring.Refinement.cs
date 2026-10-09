using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapLethalAuthoring
    {
        sealed class ExportError
        {
            public PoseMetrics pose;
            public float muscle, inverseMuscleDiagnostic;
            public float Score => Mathf.Max(pose.Score, muscle / .003f);
            public override string ToString()
            {
                return pose + "; nativeStreamMuscleError=" + muscle.ToString("R") +
                    "; inverseMuscleDiagnostic=" + inverseMuscleDiagnostic.ToString("R");
            }
        }

        static ExportError NativeExportError(MeasuredPose expected, MeasuredPose actual, Vector3 shift)
        {
            var metrics = new PoseMetrics();
            metrics.Compare(expected, actual, shift);
            return new ExportError { pose = metrics };
        }

        static ExportError CandidateExportError(PoseSampler original, PoseSampler fall, MeasuredPose actual,
            float preserve, Vector3 shift, float fallDuration)
        {
            float time = actual.time;
            if (time <= preserve)
                return NativeExportError(original.At(time), actual, Vector3.zero);
            if (time >= preserve + BlendDuration)
                return NativeExportError(fall.At(MapFallTime(time - preserve, fallDuration)), actual, shift);
            var expected = BlendMeasured(original.At(time), fall.At(MapFallTime(time - preserve, fallDuration)),
                time, preserve, shift);
            var metrics = new PoseMetrics { samples = 1 };
            metrics.Track(expected.root, actual.root, expected.rootRotation, actual.rootRotation, "blend root", time);
            metrics.Track(expected.body, actual.body, expected.bodyRotation, actual.bodyRotation, "blend body", time);
            return new ExportError
            {
                pose = metrics,
                muscle = StreamMuscleError(expected, actual),
                inverseMuscleDiagnostic = expected.muscles.Select((value, i) =>
                    Mathf.Abs(value - actual.muscles[i])).Max()
            };
        }

        static float StreamMuscleError(MeasuredPose expected, MeasuredPose actual)
        {
            if (expected.streamMuscles == null || actual.streamMuscles == null ||
                expected.streamMuscles.Length != HumanTrait.MuscleCount ||
                actual.streamMuscles.Length != HumanTrait.MuscleCount)
                throw new InvalidOperationException("Authored blend validation requires native animation streams.");
            // GetHumanPose reconstructs controls from the solved bones; that nonlinear inverse
            // does not commute with blending. Compare the authored native control domain instead.
            return expected.streamMuscles.Select((value, i) =>
                Mathf.Abs(value - actual.streamMuscles[i])).Max();
        }

        // Retain all 120Hz base keys. Add exact native samples only where a linear interval loses accuracy.
        // A stricter refinement budget reserves margin; original independent acceptance gates still run afterward.
        static AnimationClip RefineExport(Scene scene, Source rig, MeasuredPose[] initial, PoseEncoding encoding,
            string name, float duration, Func<float, MeasuredPose> sample,
            Func<MeasuredPose, ExportError> compare, StringBuilder report, out MeasuredPose[] poses)
        {
            const float refinementBudget = .25f;
            const int maximumPasses = 7;
            poses = initial;
            for (int pass = 0; pass < maximumPasses; pass++)
            {
                var trial = EncodePoses(rig, poses, encoding, name, duration);
                bool keep = false;
                try
                {
                    using var actual = new PoseSampler(scene, rig.path, trial, true);
                    var add = new SortedSet<float>();
                    float keyScore = 0;
                    float interiorScore = 0;
                    string worstKey = "none";
                    string worstInterior = "none";
                    for (int i = 0; i < poses.Length; i++)
                    {
                        float time = poses[i].time;
                        var keyError = compare(actual.At(time));
                        if (keyError.Score > keyScore)
                        {
                            keyScore = keyError.Score;
                            worstKey = keyError.ToString();
                        }
                        if (i + 1 == poses.Length)
                            continue;
                        float next = poses[i + 1].time;
                        foreach (float fraction in new[] { .25f, .5f, .75f })
                        {
                            float probe = Mathf.Lerp(time, next, fraction);
                            if (probe <= time || probe >= next)
                                continue;
                            var error = compare(actual.At(probe));
                            if (error.Score > interiorScore)
                            {
                                interiorScore = error.Score;
                                worstInterior = FormattableString.Invariant($"interval=[{time:R},{next:R}] ") + error;
                            }
                            if (error.Score > refinementBudget)
                                add.Add(Mathf.Lerp(time, next, .5f));
                        }
                    }
                    report.AppendLine(FormattableString.Invariant(
                        $"LINEAR REFINEMENT {name} pass={pass}; keys={poses.Length}; add={add.Count}; keyScore={keyScore:R}; interiorScore={interiorScore:R}"));
                    report.AppendLine("EXACT KEY MAX " + worstKey);
                    report.AppendLine("QUARTER/MIDPOINT MAX " + worstInterior);
                    if (keyScore > 1)
                        throw new InvalidOperationException("Native preservation failed at an exact exported key; " +
                            "subdivision cannot repair this encoding error: " + worstKey);
                    if (add.Count == 0)
                    {
                        keep = true;
                        return trial;
                    }
                    if (pass + 1 == maximumPasses)
                        throw new InvalidOperationException("Linear export refinement exceeded its finite pass budget: " +
                            worstInterior);
                    var times = new SortedSet<float>(poses.Select(pose => pose.time));
                    int previousCount = times.Count;
                    times.UnionWith(add);
                    if (times.Count == previousCount)
                        throw new InvalidOperationException("Cannot subdivide inaccurate interval at float precision.");
                    poses = times.Select(sample).ToArray();
                }
                finally
                {
                    if (!keep)
                        Object.DestroyImmediate(trial);
                }
            }
            throw new InvalidOperationException("Export refinement did not converge.");
        }
    }
}
