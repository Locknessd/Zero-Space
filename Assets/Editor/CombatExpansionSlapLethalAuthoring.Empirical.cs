using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapLethalAuthoring
    {
        sealed class SampledSource
        {
            public Source source;
            public MeasuredPose[] poses;
        }

        static AnimationClip BuildEmpirical(Source receiver, Source fall, int sequence, StringBuilder report)
        {
            float preserve = Contacts[sequence] + .12f;
            if (Mathf.Abs(fall.clip.length - 3.566667f) > .0001f || receiver.clip.length < preserve + FallDuration)
                throw new InvalidOperationException("Unexpected native duration; authored map cannot fit.");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                report.AppendLine("COMMON RECEIVER RIG=" + receiver.path + "; source=" + receiver.guid);
                var tracks = new[] { receiver, fall }.Select(s => CaptureTrack(scene, receiver, s)).ToArray();
                var encoding = CalibrateEncoding(scene, receiver, tracks, report);
                using var original = new PoseSampler(scene, receiver.path, receiver.clip, true);
                using var incoming = new PoseSampler(scene, receiver.path, fall.clip, true);
                var anchor = original.At(preserve);
                var fallAnchor = incoming.At(FallStart);
                var shift = anchor.body - fallAnchor.body;
                shift.y = 0;
                var times = SampleTimes(receiver.clip.length, Contacts[sequence], preserve);
                var poses = times.Select(time => BlendMeasured(original.At(time),
                    incoming.At(MapFallTime(time - preserve, fall.clip.length)),
                    time, preserve, shift)).ToArray();
                using var nativeOriginal = new PoseSampler(scene, receiver.path, receiver.clip, true);
                using var nativeFall = new PoseSampler(scene, receiver.path, fall.clip, true);
                var candidate = RefineExport(scene, receiver, poses, encoding,
                    "SlapSequence" + (sequence + 1) + "_LethalReceiver_Provisional", receiver.clip.length,
                    time => BlendMeasured(original.At(time),
                        incoming.At(MapFallTime(time - preserve, fall.clip.length)), time, preserve, shift),
                    observed => CandidateExportError(nativeOriginal, nativeFall, observed,
                        preserve, shift, fall.clip.length), report, out poses);
                times = poses.Select(pose => pose.time).ToArray();
                try
                {
                    report.AppendLine("CANDIDATE " + candidate.name + "; encoding=" + encoding);
                    report.AppendLine(FormattableString.Invariant(
                        $"Measured horizontal alignment metres=({shift.x:R},0,{shift.z:R}); body and root shifted together."));
                    report.AppendLine("No muscle residual, clamp, yaw offset or added noise. " +
                        "Muscle capture mode=" + encoding.streamMuscles + "; finger bindings mapped by verified names.");
                    WriteTimeMap(sequence, times, preserve, fall.clip.length);
                    WriteMeasuredMap(sequence, poses);
                    VerifyCandidate(scene, receiver, fall, candidate, preserve, shift, times, report);
                    candidate.hideFlags = HideFlags.None;
                    return candidate;
                }
                catch
                {
                    Object.DestroyImmediate(candidate);
                    throw;
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static SampledSource CaptureTrack(Scene scene, Source receiver, Source source)
        {
            var times = new SortedSet<float> { 0, source.clip.length };
            for (int tick = 1; tick < Mathf.CeilToInt(source.clip.length * 120); tick++)
                times.Add(tick / 120f);
            foreach (float time in new[] { FallStart, 1.3f, 2f, 2.55f, Contacts[0], Contacts[1],
                Contacts[0] + .12f, Contacts[1] + .12f })
                if (time < source.clip.length)
                    times.Add(time);
            using var sampler = new PoseSampler(scene, receiver.path, source.clip, true);
            return new SampledSource { source = source, poses = times.Select(sampler.At).ToArray() };
        }

        static PoseEncoding CalibrateEncoding(Scene scene, Source receiver, SampledSource[] tracks,
            StringBuilder report)
        {
            PoseEncoding best = null;
            float bestScore = float.PositiveInfinity;
            DiagnoseRoundtrip(scene, receiver, tracks, report);
            foreach (bool streamMuscles in new[] { false, true })
                foreach (bool relative in new[] { false, true })
                    foreach (bool normalized in new[] { false, true })
                    {
                        var encoding = new PoseEncoding
                        {
                            bodyRelative = relative,
                            normalizedMotion = normalized,
                            streamMuscles = streamMuscles
                        };
                        float score = 0;
                        foreach (var track in tracks)
                        {
                            var trial = EncodePoses(receiver, track.poses, encoding, "EmpiricalEncodingProbe",
                                track.source.clip.length);
                            try
                            {
                                var metrics = ComparePlayback(scene, receiver, track.source.clip, trial,
                                    ProbeTimes(track.source.clip.length));
                                score = Mathf.Max(score, metrics.Score);
                                report.AppendLine("ENCODING TRIAL " + encoding + "; source=" + track.source.guid +
                                    "; " + metrics);
                            }
                            finally
                            {
                                Object.DestroyImmediate(trial);
                            }
                        }
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = encoding;
                        }
                    }
            if (best == null || bestScore > 1)
                throw new InvalidOperationException("No empirical Root/Motion encoding preserved native bones/root. " +
                    "Best normalized error=" + bestScore + ". Trial measurements are in BakeProvenance.txt.");
            report.AppendLine("SELECTED EMPIRICAL ENCODING " + best);
            foreach (var track in tracks)
            {
                using var author = new PoseSampler(scene, receiver.path, track.source.clip, true);
                using var native = new PoseSampler(scene, receiver.path, track.source.clip);
                var canonical = RefineExport(scene, receiver, track.poses, best,
                    "NativeEquivalenceProof_" + track.source.guid, track.source.clip.length, author.At,
                    observed => NativeExportError(native.At(observed.time), observed, Vector3.zero),
                    report, out track.poses);
                try
                {
                    var metrics = ComparePlayback(scene, receiver, track.source.clip, canonical,
                        DenseTimes(track.source.clip.length, track.poses.Select(p => p.time)));
                    report.AppendLine("FULL 240Hz + OFFSET SOURCE EQUIVALENCE " + track.source.guid + ": " + metrics);
                    if (metrics.Score > 1)
                        throw new InvalidOperationException("Native pose/root equivalence failed: " + metrics);
                }
                finally
                {
                    Object.DestroyImmediate(canonical);
                }
            }
            report.AppendLine("Native Motion and explicit goals consumed by original Playable. " +
                "Canonical goal omission proven against independent full native playback, with foot/playable IK off.");
            return best;
        }

        static MeasuredPose BlendMeasured(MeasuredPose original, MeasuredPose incoming, float time,
            float preserve, Vector3 shift)
        {
            float weight = BlendWeight(time, preserve);
            if (Mathf.Abs(original.scale - incoming.scale) > .00001f)
                throw new InvalidOperationException("Sources were not sampled at a common native humanScale.");
            return new MeasuredPose
            {
                time = time,
                scale = original.scale,
                root = Vector3.Lerp(original.root, incoming.root + shift, weight),
                body = Vector3.Lerp(original.body, incoming.body + shift, weight),
                rootRotation = Quaternion.Slerp(original.rootRotation, incoming.rootRotation, weight),
                bodyRotation = Quaternion.Slerp(original.bodyRotation, incoming.bodyRotation, weight),
                muscles = original.muscles.Select((v, i) => Mathf.LerpUnclamped(v, incoming.muscles[i], weight)).ToArray(),
                streamMuscles = original.streamMuscles == null ? null : original.streamMuscles.Select((v, i) =>
                    Mathf.LerpUnclamped(v, incoming.streamMuscles[i], weight)).ToArray()
            };
        }

        static float[] ProbeTimes(float duration)
        {
            return new[] { 0, .45f, .933333333f, 1.4f, 1.52f, 2f, 2.55f, 3.2f, duration }
                .Where(t => t <= duration).Distinct().OrderBy(t => t).ToArray();
        }

        static float[] DenseTimes(float duration, IEnumerable<float> extra)
        {
            var ticks = Enumerable.Range(0, Mathf.CeilToInt(duration * 240) + 1);
            // Keep the original 240Hz gate and audit an independent phase between export/refinement knots.
            return ticks.Select(i => Mathf.Min(i / 240f, duration))
                .Concat(ticks.Select(i => Mathf.Min((i + .37f) / 240f, duration)))
                .Concat(extra).Distinct().OrderBy(t => t).ToArray();
        }

        static void WriteMeasuredMap(int sequence, MeasuredPose[] poses)
        {
            var csv = new StringBuilder("seconds,humanScale,bodyX,bodyY,bodyZ,rootX,rootY,rootZ\n");
            foreach (var p in poses)
                csv.AppendLine(FormattableString.Invariant(
                    $"{p.time:R},{p.scale:R},{p.body.x:R},{p.body.y:R},{p.body.z:R},{p.root.x:R},{p.root.y:R},{p.root.z:R}"));
            File.WriteAllText(Output + "/Sequence" + (sequence + 1) + ".MeasuredPoseMap.csv", csv.ToString());
        }

        [MenuItem("Tools/Combat Expansion/Slap/Diagnose Lethal Pose Encoding")]
        public static void DiagnoseLethalPoseEncoding()
        {
            var sources = LoadSources();
            var report = new StringBuilder();
            Exception failure = null;
            Directory.CreateDirectory(Output);
            ArchivePreviousProvenance();
            try
            {
                WriteInspection(sources);
                foreach (var source in sources)
                    ValidateSource(source);
                for (int sequence = 0; sequence < 2; sequence++)
                {
                    var scene = EditorSceneManager.NewPreviewScene();
                    try
                    {
                        var receiver = sources[sequence];
                        var tracks = new[] { receiver, sources[2] }
                            .Select(s => CaptureTrack(scene, receiver, s)).ToArray();
                        CalibrateEncoding(scene, receiver, tracks, report);
                    }
                    finally
                    {
                        EditorSceneManager.ClosePreviewScene(scene);
                    }
                }
            }
            catch (Exception exception)
            {
                failure = exception;
                report.AppendLine("FAILED: " + exception);
                throw;
            }
            finally
            {
                FinishGuard(sources, report, failure);
            }
        }
    }
}
