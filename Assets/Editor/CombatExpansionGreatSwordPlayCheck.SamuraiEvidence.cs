using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordPlayCheck
    {
        static readonly HumanBodyBones[] SamuraiLandmarks =
        {
            HumanBodyBones.Hips, HumanBodyBones.Head, HumanBodyBones.LeftHand,
            HumanBodyBones.RightHand, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot
        };
        static Vector3[] samuraiNativeEnd;
        static bool samuraiBoundary, capturedNativeEnd, capturedEarly, capturedMid, capturedLate;
        static float samuraiMaxBoundary;
        static int samuraiNativeFrame, samuraiBlendFrames;

        static Vector3[] SamuraiPose() => fighters.SelectMany(fighter => SamuraiLandmarks.Select(bone =>
            fighter.Animator.GetBoneTransform(bone).position)).ToArray();

        static float SamuraiPoseDelta(Vector3[] before, Vector3[] after) =>
            before.Select((position, i) => Vector3.Distance(position, after[i])).Max();

        static Vector3[] SamuraiReceiverPose() => SamuraiLandmarks.Select(bone =>
            target.Animator.GetBoneTransform(bone).position).ToArray();

        static void ResetSamuraiEvidence()
        {
            ResetSamuraiTrajectory();
            samuraiNativeEnd = null;
            samuraiBoundary = capturedNativeEnd = capturedEarly = capturedMid = capturedLate = false;
            samuraiMaxBoundary = 0;
            samuraiBlendFrames = 0;
        }

        static void ObserveSamuraiEvidence()
        {
            if (!Samurai || !QueuePlayback || !pair.Playing)
                return;
            if (!pair.IsRecovering)
            {
                if (pair.SampleTime < pair.Duration - .00001f)
                    return;
                samuraiNativeEnd = SamuraiReceiverPose();
                samuraiNativeFrame = Time.frameCount;
                if (!capturedNativeEnd)
                {
                    CaptureSamuraiFrame("NativeEnd");
                    capturedNativeEnd = true;
                }
                return;
            }
            ObserveSamuraiTrajectory();
            if (!targetRecovered)
                return;
            float seconds = targetRecoveryProgress * move.sourcePair.getUp.length;
            float blend = Mathf.SmoothStep(0, 1, seconds / move.sourcePair.recoveryBlendSeconds);
            if (!samuraiBoundary)
            {
                Require(samuraiNativeEnd != null, "Native end pose was not observed in the actual player loop.");
                var first = SamuraiReceiverPose();
                samuraiMaxBoundary = SamuraiPoseDelta(samuraiNativeEnd, first);
                report.AppendLine($"RECOVERY_BOUNDARY nativeFrame={samuraiNativeFrame} frame={Time.frameCount} " +
                    $"sourceSeconds={pair.SampleTime:F6}/{pair.Duration:F6} recoverySeconds={seconds:F6} " +
                    $"blendDuration={move.sourcePair.recoveryBlendSeconds:F6} blendWeight={blend:F6} " +
                    $"maxLandmarkDeltaMeters={samuraiMaxBoundary:F6} toleranceMeters=0.002000");
                for (int i = 0; i < first.Length; i++)
                    report.AppendLine($"BOUNDARY_BONE {SamuraiLandmarks[i]} " +
                        $"native={samuraiNativeEnd[i]:F6} recovery={first[i]:F6} " +
                        $"delta={Vector3.Distance(samuraiNativeEnd[i], first[i]):F6}");
                ReportSamuraiBlendInternals();
                CaptureSamuraiFrame("RecoveryEarly");
                capturedEarly = true;
                samuraiBoundary = true;
                Require(samuraiMaxBoundary <= .002f,
                    $"{SamuraiRecoveryTitle} recovery entry exceeded 2 mm continuity tolerance; inspect boundary/blend diagnostics.");
            }
            if (seconds <= move.sourcePair.recoveryBlendSeconds)
            {
                samuraiBlendFrames++;
                report.AppendLine($"BLEND frame={Time.frameCount} recoverySeconds={seconds:F6} " +
                    $"weight={blend:F6} hips={target.Animator.GetBoneTransform(HumanBodyBones.Hips).position:F6}");
            }
            if (!capturedMid && targetRecoveryProgress >= .45f)
            {
                Require(targetRecoveryProgress < .7f, $"Missed real-frame mid-{SamuraiRecoveryPose} recovery capture window.");
                CaptureSamuraiFrame("RecoveryMid");
                capturedMid = true;
            }
            if (!capturedLate && targetRecoveryProgress >= .85f)
            {
                Require(targetRecoveryProgress <= 1.01f, $"Missed real-frame late-{SamuraiRecoveryPose} recovery capture window.");
                CaptureSamuraiFrame("RecoveryLate");
                capturedLate = true;
            }
        }

        static void ReportSamuraiBlendInternals()
        {
            // Read the actual runtime correction snapshot; never sample or step the Animator here.
            var correction = ObservationField(pair, "hitRecoveryPose").GetValue(pair);
            Require(correction != null, $"{SamuraiRecoveryTitle} recovery has no owned blend correction snapshot.");
            var bones = (Transform[])ObservationField(correction, "bones").GetValue(correction);
            var native = (Quaternion[])ObservationField(correction, "sourceRotations").GetValue(correction);
            var raw = (Quaternion[])ObservationField(correction, "rawRotations").GetValue(correction);
            var output = (Quaternion[])ObservationField(correction, "outputRotations").GetValue(correction);
            report.AppendLine("BLEND_INTERNAL sourceHipsWorld=" +
                ObservationField(correction, "sourceHipsWorld").GetValue(correction) + " rawHipsLocal=" +
                ObservationField(correction, "rawHipsLocal").GetValue(correction) + " outputHipsLocal=" +
                ObservationField(correction, "outputHipsLocal").GetValue(correction));
            for (int i = 0; i < bones.Length; i++)
                report.AppendLine($"BLEND_BONE {bones[i].name} sourceToRawDegrees=" +
                    $"{Quaternion.Angle(native[i], raw[i]):F6} sourceToOutputDegrees=" +
                    $"{Quaternion.Angle(native[i], output[i]):F6} outputToVisibleDegrees=" +
                    $"{Quaternion.Angle(output[i], bones[i].localRotation):F6}");
        }

        static void CaptureSamuraiFrame(string phase)
        {
            string directory = Path.GetDirectoryName(ReportPath);
            Directory.CreateDirectory(directory);
            string filename = $"{source.name}_{Direction}_Case{step + 1}_Play{samuraiRepeat + 1}_{phase}.png";
            ScreenCapture.CaptureScreenshot(Path.Combine(directory, filename));
            report.AppendLine($"FRAME phase={phase} file={filename} frame={Time.frameCount} " +
                $"sourceSeconds={pair.SampleTime:F6} recoveryNormalized={targetRecoveryProgress:F6}");
        }

        static void CheckSamuraiEvidence()
        {
            if (!Samurai || !QueuePlayback || LifecycleCancelled)
                return;
            FlushSamuraiTrajectory();
            Require(capturedNativeEnd && capturedEarly && capturedMid && capturedLate && samuraiBoundary &&
                samuraiBlendFrames > 0, $"Queued Samurai lacks native end or early/mid/late {SamuraiRecoveryPose} recovery evidence.");
            report.AppendLine($"RECOVERY_EVIDENCE maxBoundaryMeters={samuraiMaxBoundary:F6} " +
                $"blendFrames={samuraiBlendFrames} screenshotsRequested=4; " +
                "rendered appearance requires visual review.");
        }
    }
}
