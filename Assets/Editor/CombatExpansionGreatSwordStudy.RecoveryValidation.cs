using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordStudy
    {
        public static void ValidateRecoveryEntries()
        {
            const int sampleRate = 361;
            var rows = new StringBuilder("source,action,direction,seconds,clearance,repeatDrift,stoppedClockDrift\n");
            var cases = new StringBuilder();
            var complete = typeof(FrankBattlePairPlayback).GetMethod("CompleteSourceMotion",
                BindingFlags.Instance | BindingFlags.NonPublic);
            int count = 0;
            int samples = 0;
            int stoppedSamples = 0;
            float minimum = float.PositiveInfinity;
            float maximumEntry = 0;
            float maximumRepeat = 0;
            float maximumStopped = 0;
            string failure = null;
            try
            {
                EachPair((source, target, move, pair, camera, framing, direction) =>
                {
                    if (!move.sourcePair.recoveryGrounding || move.sourcePair.recoveryBlendSeconds <= 0)
                        throw new InvalidOperationException("Recovery correction is not configured: " + move.moveName);
                    pair.EvaluateAt(pair.Duration);
                    var bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                        .Select(index => target.Animator.GetBoneTransform((HumanBodyBones)index))
                        .Where(bone => bone).Distinct().ToArray();
                    var before = Positions(bones);
                    complete.Invoke(pair, null);
                    if (!pair.IsRecovering)
                        throw new InvalidOperationException("Controller recovery did not start: " + move.moveName);
                    float entry = PoseDrift(bones, before);
                    maximumEntry = Mathf.Max(maximumEntry, entry);
                    float localMinimum = float.PositiveInfinity;
                    float localRepeat = 0;
                    float localStopped = 0;
                    float duration = move.getUpAnim.length - .001f;
                    int intervals = Mathf.CeilToInt(duration * sampleRate);
                    float current = 0;
                    for (int frame = 0; frame <= intervals; frame++)
                    {
                        float seconds = duration * frame / intervals;
                        if (frame > 0)
                        {
                            pair.PrepareRecoveryPoseEvaluation();
                            target.Animator.Update(seconds - current);
                            pair.EvaluateRecoveryPose();
                        }
                        current = seconds;
                        var pose = Positions(bones);
                        pair.EvaluateRecoveryPose();
                        float repeat = PoseDrift(bones, pose);
                        localRepeat = Mathf.Max(localRepeat, repeat);
                        float stopped = 0;
                        if (frame % 31 == 0)
                        {
                            float speed = target.Animator.speed;
                            target.Animator.speed = 0;
                            for (int held = 0; held < 3; held++)
                            {
                                pair.PrepareRecoveryPoseEvaluation();
                                target.Animator.Update(1f / 30);
                                pair.EvaluateRecoveryPose();
                                stopped = Mathf.Max(stopped, PoseDrift(bones, pose));
                                stoppedSamples++;
                            }
                            target.Animator.speed = speed;
                        }
                        localStopped = Mathf.Max(localStopped, stopped);
                        float clearance = BattlePresentationContactSetup.MeasureGroundClearance(target);
                        if (!float.IsFinite(clearance))
                            throw new InvalidOperationException("Missing rendered recovery geometry: " + target.name);
                        localMinimum = Mathf.Min(localMinimum, clearance);
                        rows.AppendLine(FormattableString.Invariant(
                            $"{source.name},{move.moveName},{direction},{seconds:R},{clearance:R},{repeat:R},{stopped:R}"));
                        samples++;
                    }
                    minimum = Mathf.Min(minimum, localMinimum);
                    maximumRepeat = Mathf.Max(maximumRepeat, localRepeat);
                    maximumStopped = Mathf.Max(maximumStopped, localStopped);
                    pair.Cancel();
                    if (pair.Playing || pair.IsRecovering || pair.AttackerActor || pair.ReceiverActor ||
                        target.Animator.speed != 1 || source.Animator.speed != 1 ||
                        target.IsBusy || source.IsBusy)
                        throw new InvalidOperationException("Recovery cancellation left owned state active.");
                    count++;
                    cases.AppendLine(FormattableString.Invariant(
                        $"{source.name}/{move.moveName}/{direction}: entry={entry:R}m; clearance={localMinimum:R}m; ") +
                        FormattableString.Invariant($"repeat={localRepeat:R}m; stoppedClock={localStopped:R}m."));
                }, new[] { 1, -1 }, true);
                if (count != 16 || maximumEntry > .01f || minimum < -.025f ||
                    maximumRepeat > .00002f || maximumStopped > .00002f)
                    throw new InvalidOperationException("Recovery continuity, clearance or held-pose check failed.");
            }
            catch (Exception exception)
            {
                failure = exception.ToString();
                throw;
            }
            finally
            {
                string folder = Output + "/RecoveryValidation";
                Directory.CreateDirectory(folder);
                var report = new StringBuilder(failure == null ? "PASS\n" : "FAIL\n");
                report.AppendLine("Actual controller recovery after source completion; 361 Hz minimum.");
                report.AppendLine("Four pairs, both BattleScene avatars and directions; rendered body clearance.");
                report.AppendLine("Repeated evaluation, zero-speed controller samples and cancellation are checked.");
                report.AppendLine("Editor sampling only; no live pause, presentation or gameplay acceptance claim.");
                report.AppendLine(FormattableString.Invariant(
                    $"Cases={count}/16; samples={samples}; stopped samples={stoppedSamples}; ") +
                    FormattableString.Invariant($"maximum entry jump={maximumEntry:R}m; minimum clearance={minimum:R}m; ") +
                    FormattableString.Invariant($"repeat drift={maximumRepeat:R}m; stopped drift={maximumStopped:R}m."));
                report.Append(cases);
                if (failure != null)
                    report.AppendLine(failure);
                File.WriteAllText(folder + "/Report.txt", report.ToString());
                File.WriteAllText(folder + "/Samples.csv", rows.ToString());
            }
        }

        static Vector3[] Positions(Transform[] bones) => bones.Select(b => b.position).ToArray();

        static float PoseDrift(Transform[] bones, Vector3[] positions)
        {
            float worst = 0;
            for (int i = 0; i < bones.Length; i++)
            {
                float distance = Vector3.Distance(bones[i].position, positions[i]);
                if (!float.IsFinite(distance))
                    throw new InvalidOperationException("Nonfinite recovery pose: " + bones[i].name);
                worst = Mathf.Max(worst, distance);
            }
            return worst;
        }
    }
}
