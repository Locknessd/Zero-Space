using System;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static void E10Measure(CharacterCombat[] fighters, CharacterCombat source, CharacterCombat target,
            E10Case record, StringBuilder rows, StringBuilder entries)
        {
            var pair = E10Begin(fighters, source, target, record.direction, record);
            try
            {
                pair.EvaluateAt(pair.Duration);
                record.endpointSeconds = pair.SampleTime;
                var bones = E10Bones(target);
                var both = E10Bones(source).Concat(bones).Distinct().ToArray();
                record.mappedVictimBones = bones.Length;
                var before = E10Positions(bones);
                record.bounds = new Bounds(before[0], Vector3.zero);
                E10Bounds(record, both);
                foreach (var renderer in pair.AttackerActor.Pose.weaponRenderers)
                    record.bounds.Encapsulate(renderer.bounds);
                E10Complete(pair);
                record.recoveryStarted = true;
                var after = E10Positions(bones);
                for (int index = 0; index < bones.Length; index++)
                {
                    Vector3 a = before[index];
                    Vector3 b = after[index];
                    float jump = Vector3.Distance(a, b);
                    if (!float.IsFinite(jump))
                        throw new InvalidOperationException("Nonfinite entry displacement.");
                    record.entryJump = Mathf.Max(record.entryJump, jump);
                    entries.AppendLine(E10Prefix(record) + GroundingCsv(bones[index].name) + "," +
                        FormattableString.Invariant($"{a.x:R},{a.y:R},{a.z:R},{b.x:R},{b.y:R},{b.z:R},{jump:R}"));
                }
                record.lastRecoverySeconds = record.recoveryDuration - .001f;
                E10Advance(pair, target, 0);
                int intervals = Mathf.CeilToInt(record.lastRecoverySeconds * 361);
                float current = 0;
                var previous = after;
                for (int frame = 0; frame <= intervals; frame++)
                {
                    float seconds = record.lastRecoverySeconds * frame / intervals;
                    if (frame > 0)
                        E10Advance(pair, target, seconds - current);
                    current = seconds;
                    var pose = E10Positions(bones);
                    float displacement = E10Drift(pose, previous);
                    record.maximumFrameDisplacement = Mathf.Max(record.maximumFrameDisplacement, displacement);
                    previous = pose;
                    pair.EvaluateRecoveryPose();
                    float repeat = E10Drift(E10Positions(bones), pose);
                    float stopped = 0;
                    if (frame % 31 == 0 || frame == intervals)
                    {
                        float speed = target.Animator.speed;
                        try
                        {
                            target.Animator.speed = 0;
                            for (int held = 0; held < 3; held++)
                            {
                                E10Advance(pair, target, 1f / 30);
                                stopped = Mathf.Max(stopped, E10Drift(E10Positions(bones), pose));
                                record.stoppedSamples++;
                            }
                        }
                        finally
                        {
                            target.Animator.speed = speed;
                        }
                    }
                    float clearance = BattlePresentationContactSetup.MeasureGroundClearance(target);
                    if (!float.IsFinite(clearance) || !float.IsFinite(displacement) ||
                        !float.IsFinite(repeat) || !float.IsFinite(stopped))
                        throw new InvalidOperationException("Nonfinite recovery geometry or displacement.");
                    record.minimumClearance = frame == 0 ? clearance : Mathf.Min(record.minimumClearance, clearance);
                    record.repeatDrift = Mathf.Max(record.repeatDrift, repeat);
                    record.stoppedDrift = Mathf.Max(record.stoppedDrift, stopped);
                    rows.AppendLine(E10Prefix(record) + FormattableString.Invariant(
                        $"{seconds:R},{clearance:R},{displacement:R},{repeat:R},{stopped:R}"));
                    record.samples++;
                    E10Bounds(record, both);
                }
                record.bounds.Encapsulate(new Vector3(record.bounds.center.x, 0, record.bounds.center.z));
                record.bounds.Expand(.8f);
            }
            finally
            {
                E10Cancel(pair, source, target);
                record.cancellationPassed = true;
            }
        }

        static void E10Advance(FrankBattlePairPlayback pair, CharacterCombat target, float seconds)
        {
            if (!float.IsFinite(seconds) || seconds < 0 || !pair.IsRecovering)
                throw new InvalidOperationException("Invalid recovery sample clock or ownership.");
            pair.PrepareRecoveryPoseEvaluation();
            target.Animator.Update(seconds);
            pair.EvaluateRecoveryPose();
            var state = target.Animator.GetCurrentAnimatorStateInfo(0);
            if (!pair.IsRecovering || !state.IsName("Base Layer.GetUp") ||
                !float.IsFinite(state.normalizedTime) || !float.IsFinite(target.Animator.speed))
                throw new InvalidOperationException("Recovery controller left GetUp or has a nonfinite clock.");
        }

        static Transform[] E10Bones(CharacterCombat fighter)
        {
            var bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                .Select(i => fighter.Animator.GetBoneTransform((HumanBodyBones)i)).Where(b => b).Distinct().ToArray();
            if (bones.Length == 0)
                throw new InvalidOperationException("Recovery has no mapped humanoid bones.");
            return bones;
        }

        static Vector3[] E10Positions(Transform[] bones)
        {
            foreach (var bone in bones)
            {
                foreach (var vector in new[] { bone.position, bone.localPosition, bone.lossyScale })
                for (int axis = 0; axis < 3; axis++)
                    if (!float.IsFinite(vector[axis]))
                        throw new InvalidOperationException("Nonfinite recovery bone: " + bone.name);
                for (int axis = 0; axis < 4; axis++)
                    if (!float.IsFinite(bone.rotation[axis]))
                        throw new InvalidOperationException("Nonfinite recovery rotation: " + bone.name);
            }
            return bones.Select(b => b.position).ToArray();
        }

        static float E10Drift(Vector3[] current, Vector3[] previous)
        {
            float maximum = 0;
            for (int index = 0; index < current.Length; index++)
            {
                float distance = Vector3.Distance(current[index], previous[index]);
                if (!float.IsFinite(distance))
                    throw new InvalidOperationException("Nonfinite recovery drift.");
                maximum = Mathf.Max(maximum, distance);
            }
            return maximum;
        }

        static void E10Bounds(E10Case record, Transform[] bones)
        {
            foreach (var point in E10Positions(bones))
                record.bounds.Encapsulate(point);
        }

        static string E10Prefix(E10Case record) => GroundingCsv(record.source) + "," +
            GroundingCsv(record.target) + "," + record.direction + ",";
    }
}
