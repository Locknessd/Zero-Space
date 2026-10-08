using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static FrankBattlePairPlayback BeginGroundingStudy(CharacterCombat[] fighters, CharacterCombat source,
            CharacterCombat target, int direction, FrankPairGrounding grounding)
        {
            foreach (var fighter in fighters)
                fighter.ResetCombat();
            source.Animator.transform.SetPositionAndRotation(Vector3.left * direction * .85f,
                Quaternion.LookRotation(Vector3.right * direction));
            target.Animator.transform.SetPositionAndRotation(Vector3.right * direction * .85f,
                Quaternion.LookRotation(Vector3.left * direction));
            var move = MakePairMove(source, target, 1);
            move.grounding = grounding;
            try
            {
                if (!source.ExecuteAttack(move, target) || !source.SourcePlayback || !source.SourcePlayback.Playing)
                    throw new InvalidOperationException("Samurai grounding pair was rejected.");
                var pair = source.SourcePlayback;
                if (pair.Move.grounding != grounding)
                    throw new InvalidOperationException("Source playback grounding does not match the requested asset.");
                pair.AttackerActor.Pose.transferFingers = true;
                pair.ReceiverActor.Pose.transferFingers = true;
                return pair;
            }
            catch
            {
                if (source.SourcePlayback)
                    source.SourcePlayback.Cancel();
                throw;
            }
        }

        static void CheckGroundingFighters(CharacterCombat[] fighters)
        {
            if (fighters.Length != 2 || fighters[0] == fighters[1] ||
                fighters[0].Animator.avatar == fighters[1].Animator.avatar)
                throw new InvalidOperationException("Grounding requires two distinct fighters and avatars.");
            foreach (var fighter in fighters)
                if (!fighter.Animator.GetBoneTransform(HumanBodyBones.Hips))
                    throw new InvalidOperationException("Missing hips for " + fighter.name);
        }

        static int GroundingIntervals(float duration, int rate)
        {
            if (!float.IsFinite(duration) || duration <= 0 || duration * rate > int.MaxValue - 1)
                throw new InvalidOperationException("Invalid Samurai grounding duration: " + duration);
            return Mathf.Max(1, Mathf.CeilToInt(duration * rate));
        }

        static float GroundingClearance(CharacterCombat fighter)
        {
            foreach (var node in fighter.Animator.GetComponentsInChildren<Transform>(true))
            {
                var matrix = node.localToWorldMatrix;
                for (int component = 0; component < 16; component++)
                    if (!float.IsFinite(matrix[component]))
                        throw new InvalidOperationException("Nonfinite grounding transform: " + node.name);
            }
            float clearance = BattlePresentationContactSetup.MeasureGroundClearance(fighter);
            if (!float.IsFinite(clearance))
                throw new InvalidOperationException("Missing or invalid visible body geometry: " + fighter.name);
            return clearance;
        }

        static void CheckGroundingTracks(FrankPairGrounding.Track[] tracks, CharacterCombat[] fighters)
        {
            if (tracks == null || tracks.Length != 4 || tracks.Any(track => track == null))
                throw new InvalidOperationException("Expected exactly four Samurai grounding tracks.");
            foreach (var fighter in fighters)
            foreach (bool receiver in new[] { false, true })
            {
                var matches = tracks.Where(track => track.avatar == fighter.Animator.avatar &&
                    track.receiver == receiver).ToArray();
                if (matches.Length != 1)
                    throw new InvalidOperationException("Missing or duplicate grounding avatar/role: " + fighter.name);
                var track = matches[0];
                int intervals = GroundingIntervals(track.duration, GroundingBakeRate);
                if (track.lift == null || track.lift.Length != intervals + 1)
                    throw new InvalidOperationException("Invalid grounding sample count: " + fighter.name);
                for (int frame = 0; frame < track.lift.Length; frame++)
                {
                    float lift = track.lift[frame];
                    if (!float.IsFinite(lift) || lift < 0 || lift > GroundingMaximumLift)
                        throw new InvalidOperationException(FormattableString.Invariant(
                            $"Invalid grounding lift: fighter={fighter.name}; receiver={receiver}; ") +
                            FormattableString.Invariant($"seconds={track.duration * frame / intervals:R}; ") +
                            FormattableString.Invariant($"measured stored lift={lift:R}; limit={GroundingMaximumLift:R} m. ") +
                            "Manual adaptation required; see bake CSV for raw clearance and base lift.");
                }
            }
        }

        static string GroundingRole(int role) => role == 0 ? "attacker" : "receiver";

        static string GroundingCsv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

        static void WriteGroundingReport(string name, string contents)
        {
            Directory.CreateDirectory(BladeOutput);
            File.WriteAllText(Path.Combine(BladeOutput, name), contents);
        }
    }
}
