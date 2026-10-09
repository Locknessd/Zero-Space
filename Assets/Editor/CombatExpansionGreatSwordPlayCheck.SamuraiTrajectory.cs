using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordPlayCheck
    {
        static readonly StringBuilder samuraiTrajectoryRows = new StringBuilder();
        static readonly HashSet<string> samuraiTrajectorySkips = new HashSet<string>();
        static readonly Dictionary<Transform, Vector3[]> samuraiTrajectoryPrevious =
            new Dictionary<Transform, Vector3[]>();
        static string samuraiTrajectoryPath;
        static int samuraiTrajectoryFrame = -1, samuraiTrajectoryPreviousFrame = -1, samuraiTrajectorySamples;
        static float samuraiTrajectorySeconds;
        static bool samuraiTrajectoryDone, samuraiTrajectoryWritten;

        static void ResetSamuraiTrajectory()
        {
            FlushSamuraiTrajectory();
            samuraiTrajectoryRows.Clear();
            samuraiTrajectorySkips.Clear();
            samuraiTrajectoryPrevious.Clear();
            samuraiTrajectoryPath = null;
            samuraiTrajectoryFrame = samuraiTrajectoryPreviousFrame = -1;
            samuraiTrajectorySamples = 0;
            samuraiTrajectorySeconds = 0;
            samuraiTrajectoryDone = samuraiTrajectoryWritten = false;
        }

        static object SamuraiTrajectoryField(object owner, string name) =>
            ObservationField(owner, name).GetValue(owner);

        static void SamuraiTrajectorySkip(string reason)
        {
            if (samuraiTrajectorySkips.Add(reason))
                report.AppendLine($"RECOVERY_TRAJECTORY_SKIP case={step + 1} play={samuraiRepeat + 1} " +
                    $"frame={Time.frameCount} reason={reason}");
        }

        static void ObserveSamuraiTrajectory()
        {
            if (!Samurai10 || !pair || !pair.Playing || !pair.IsRecovering || samuraiTrajectoryDone ||
                samuraiTrajectoryFrame == Time.frameCount)
                return;
            samuraiTrajectoryFrame = Time.frameCount;
            try
            {
                if (!target || !target.Animator || target.SourcePlayback != pair)
                {
                    SamuraiTrajectorySkip("receiver/animator unavailable or recovery ownership lost");
                    return;
                }
                var animator = target.Animator;
                var state = animator.GetCurrentAnimatorStateInfo(0);
                var clip = move.sourcePair.getUp;
                if (!animator.isActiveAndEnabled || !state.IsName("Base Layer.GetUp") || !clip ||
                    !float.IsFinite(state.normalizedTime))
                {
                    SamuraiTrajectorySkip("active GetUp controller clock or clip unavailable");
                    return;
                }
                var pose = SamuraiTrajectoryField(pair, "hitRecoveryPose");
                if (pose == null || !Equals(SamuraiTrajectoryField(pose, "fighter"), target) ||
                    !Equals(SamuraiTrajectoryField(pose, "animator"), animator) ||
                    (int)SamuraiTrajectoryField(pose, "playbackId") != target.PlaybackId ||
                    !(bool)SamuraiTrajectoryField(pose, "applied"))
                {
                    SamuraiTrajectorySkip("owned applied recovery snapshot unavailable");
                    return;
                }
                float applied = (float)SamuraiTrajectoryField(pose, "appliedNormalizedTime");
                if (applied != state.normalizedTime)
                {
                    SamuraiTrajectorySkip("stored raw pose clock differs from current GetUp controller clock");
                    return;
                }
                var bones = (Transform[])SamuraiTrajectoryField(pose, "bones");
                var rotations = (Quaternion[])SamuraiTrajectoryField(pose, "rawRotations");
                var positions = (Vector3[])SamuraiTrajectoryField(pose, "rawPositions");
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
                if (bones == null || rotations == null || positions == null || !hips || bones.Length == 0 ||
                    bones.Length != rotations.Length || bones.Length != positions.Length || bones.Any(b => !b))
                {
                    SamuraiTrajectorySkip("raw snapshot bone arrays or hips unavailable");
                    return;
                }
                float seconds = state.normalizedTime * clip.length;
                if (samuraiTrajectorySamples > 0 && seconds < samuraiTrajectorySeconds)
                {
                    SamuraiTrajectorySkip("controller clock moved backwards; stopped diagnostic trajectory");
                    samuraiTrajectoryDone = true;
                    FlushSamuraiTrajectory();
                    return;
                }
                if (samuraiTrajectoryPath == null)
                    BeginSamuraiTrajectory();
                var locals = new Dictionary<Transform, Matrix4x4>();
                for (int i = 0; i < bones.Length; i++)
                {
                    Vector3 position = bones[i] == hips
                        ? (Vector3)SamuraiTrajectoryField(pose, "rawHipsLocal") : positions[i];
                    locals[bones[i]] = Matrix4x4.TRS(position, rotations[i], bones[i].localScale);
                }
                var worlds = new Dictionary<Transform, Matrix4x4>();
                string context = SamuraiTrajectoryContext(animator, state, seconds, applied);
                for (int i = 0; i < bones.Length; i++)
                {
                    var bone = bones[i];
                    Vector3 raw = SamuraiTrajectoryWorld(bone, locals, worlds).MultiplyPoint3x4(Vector3.zero);
                    Vector3 visible = bone.position;
                    bool previous = samuraiTrajectoryPrevious.TryGetValue(bone, out var before);
                    string rawDelta = previous ? SamuraiTrajectoryNumber(Vector3.Distance(raw, before[0])) : "";
                    string visibleDelta = previous ? SamuraiTrajectoryNumber(Vector3.Distance(visible, before[1])) : "";
                    samuraiTrajectoryRows.Append(context).Append(',').Append(i).Append(',')
                        .Append(SamuraiTrajectoryCsv(bone.name)).Append(',').Append(SamuraiTrajectoryVector(raw))
                        .Append(',').Append(SamuraiTrajectoryVector(visible)).Append(',').Append(rawDelta)
                        .Append(',').Append(visibleDelta).Append(',')
                        .Append(SamuraiTrajectoryNumber(Vector3.Distance(raw, visible))).AppendLine();
                    samuraiTrajectoryPrevious[bone] = new[] { raw, visible };
                }
                samuraiTrajectorySeconds = seconds;
                samuraiTrajectoryPreviousFrame = Time.frameCount;
                samuraiTrajectorySamples++;
                samuraiTrajectoryDone = seconds >= .25f || samuraiTrajectorySamples >= 2048;
                if (samuraiTrajectorySamples >= 2048 && seconds < .25f)
                    SamuraiTrajectorySkip("2048-frame diagnostic cap reached before 0.25 controller seconds");
                if (samuraiTrajectoryDone || samuraiTrajectorySamples % 16 == 0 ||
                    samuraiTrajectoryRows.Length >= 262144)
                    FlushSamuraiTrajectory();
            }
            catch (Exception exception)
            {
                SamuraiTrajectorySkip("observation unavailable: " + exception.GetType().Name + ": " +
                    exception.Message);
            }
        }

        static Matrix4x4 SamuraiTrajectoryWorld(Transform bone, Dictionary<Transform, Matrix4x4> locals,
            Dictionary<Transform, Matrix4x4> worlds)
        {
            if (!bone)
                return Matrix4x4.identity;
            if (worlds.TryGetValue(bone, out var world))
                return world;
            if (!locals.TryGetValue(bone, out var local))
                local = Matrix4x4.TRS(bone.localPosition, bone.localRotation, bone.localScale);
            world = SamuraiTrajectoryWorld(bone.parent, locals, worlds) * local;
            worlds[bone] = world;
            return world;
        }

        static string SamuraiTrajectoryContext(Animator animator, AnimatorStateInfo state, float seconds, float applied)
        {
            bool transition = animator.IsInTransition(0);
            var info = animator.GetAnimatorTransitionInfo(0);
            var next = animator.GetNextAnimatorStateInfo(0);
            string dt = samuraiTrajectoryPreviousFrame >= 0
                ? SamuraiTrajectoryNumber(seconds - samuraiTrajectorySeconds) : "";
            return string.Join(",", new[]
            {
                SamuraiTrajectoryNumber(Time.frameCount), SamuraiTrajectoryNumber(step + 1),
                SamuraiTrajectoryNumber(samuraiRepeat + 1), SamuraiTrajectoryCsv(source.name),
                SamuraiTrajectoryCsv(target.name), SamuraiTrajectoryNumber(Direction),
                SamuraiTrajectoryNumber(state.normalizedTime), SamuraiTrajectoryNumber(seconds),
                SamuraiTrajectoryNumber(applied), SamuraiTrajectoryNumber(samuraiTrajectoryPreviousFrame), dt,
                SamuraiTrajectoryNumber(Time.deltaTime), SamuraiTrajectoryNumber(Time.unscaledDeltaTime),
                transition ? "true" : "false", SamuraiTrajectoryNumber(state.fullPathHash),
                SamuraiTrajectoryNumber(state.length), SamuraiTrajectoryNumber(state.speed),
                SamuraiTrajectoryNumber(state.speedMultiplier), SamuraiTrajectoryNumber(animator.speed),
                SamuraiTrajectoryNumber(next.fullPathHash), SamuraiTrajectoryNumber(next.normalizedTime),
                SamuraiTrajectoryNumber(info.fullPathHash), SamuraiTrajectoryNumber(info.normalizedTime),
                SamuraiTrajectoryNumber(info.duration), SamuraiTrajectoryCsv(info.durationUnit.ToString()),
                info.anyState ? "true" : "false", SamuraiTrajectoryClips(animator.GetCurrentAnimatorClipInfo(0)),
                SamuraiTrajectoryClips(animator.GetNextAnimatorClipInfo(0))
            });
        }

        static string SamuraiTrajectoryClips(AnimatorClipInfo[] clips) => SamuraiTrajectoryCsv(string.Join(";",
            clips.Select(info => (info.clip ? info.clip.name : "<missing>") + "=" +
                SamuraiTrajectoryNumber(info.weight))));

        static string SamuraiTrajectoryNumber(IFormattable value) => value.ToString(null, CultureInfo.InvariantCulture);

        static string SamuraiTrajectoryVector(Vector3 value) => string.Join(",", SamuraiTrajectoryNumber(value.x),
            SamuraiTrajectoryNumber(value.y), SamuraiTrajectoryNumber(value.z));

        static string SamuraiTrajectoryCsv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

        static void BeginSamuraiTrajectory()
        {
            string name = $"{source.name}_{target.name}_{Direction}_Case{step + 1}_Play{samuraiRepeat + 1}.csv";
            foreach (char invalid in Path.GetInvalidFileNameChars())
                name = name.Replace(invalid, '_');
            samuraiTrajectoryPath = Path.Combine(Path.GetDirectoryName(ReportPath), "RecoveryTrajectory", name);
            samuraiTrajectoryRows.AppendLine("frame,case,play,source,target,direction,getup_normalized,getup_seconds," +
                "applied_normalized,previous_sample_frame,controller_dt,delta_time,unscaled_delta_time,in_transition," +
                "current_state_hash,state_length,state_speed,state_speed_multiplier,animator_speed,next_state_hash," +
                "next_normalized,transition_hash,transition_normalized,transition_duration,transition_duration_unit," +
                "transition_any_state,current_clips_weights,next_clips_weights,bone_index,bone_name," +
                "reconstructed_raw_x,reconstructed_raw_y,reconstructed_raw_z,visible_x,visible_y,visible_z," +
                "reconstructed_raw_delta_m,visible_delta_m,raw_to_visible_m");
            report.AppendLine("RECOVERY_TRAJECTORY file=" + samuraiTrajectoryPath +
                " provenance=reconstructed raw controller pose from stored locals before grounding/custom blend; " +
                "live scales and nonmapped ancestors cannot recover pre-evaluation ancestor changes; " +
                "not a direct evaluation-hook measurement; " +
                "player-loop harness/reflection/screenshots/CSV add overhead; " +
                "controller_dt uses successive sampled GetUp clocks; first delta is blank; " +
                "first 0.25s plus crossing frame.");
        }

        static void FlushSamuraiTrajectory()
        {
            if (samuraiTrajectoryPath == null || samuraiTrajectoryRows.Length == 0)
                return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(samuraiTrajectoryPath));
                if (samuraiTrajectoryWritten)
                    File.AppendAllText(samuraiTrajectoryPath, samuraiTrajectoryRows.ToString());
                else
                    File.WriteAllText(samuraiTrajectoryPath, samuraiTrajectoryRows.ToString());
                samuraiTrajectoryWritten = true;
                samuraiTrajectoryRows.Clear();
            }
            catch (Exception exception)
            {
                SamuraiTrajectorySkip("CSV output unavailable: " + exception.GetType().Name + ": " + exception.Message);
                samuraiTrajectoryRows.Clear();
                samuraiTrajectoryDone = true;
            }
        }
    }
}
