using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class BattleSlapPositionReview
    {
        const string Output = "GeneratedAssets/SlapPositionReview";
        static readonly MethodInfo Complete = typeof(FrankBattlePairPlayback).GetMethod(
            "CompleteSourceMotion", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly MethodInfo Late = typeof(FrankBattlePairPlayback).GetMethod(
            "LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Inspect() => Run("Before");
        public static void Validate() => Run("After");

        static void Run(string label)
        {
            Directory.CreateDirectory(Output);
            var rows = new StringBuilder("attacker,move,direction,stage,fighter,rootX,rootY,rootZ," +
                "hipsX,hipsY,hipsZ,leftFootX,leftFootY,leftFootZ,rightFootX,rightFootY,rightFootZ\n");
            var report = new StringBuilder();
            BattleComboAuthoring.WithStudy((scene, game, fighters) =>
            {
                foreach (var source in fighters)
                foreach (var move in source.lightCombatMoves.Where(m => m.moveName.StartsWith("SlapFace_")))
                foreach (int direction in new[] { 1, -1 })
                {
                    var target = fighters.Single(f => f != source);
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    source.Animator.transform.SetPositionAndRotation(Vector3.left * direction * .4f,
                        Quaternion.LookRotation(Vector3.right * direction));
                    target.Animator.transform.SetPositionAndRotation(Vector3.right * direction * .4f,
                        Quaternion.LookRotation(Vector3.left * direction));
                    source.Animator.Update(0);
                    target.Animator.Update(0);
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Slap position fixture rejected: " + move.moveName);
                    var playback = source.SourcePlayback;
                    try
                    {
                        playback.EvaluateAt(0);
                        Record(rows, source, move, direction, "start", fighters);
                        playback.EvaluateAt(playback.Duration);
                        Record(rows, source, move, direction, "terminal", fighters);
                        var before = fighters.Select(f => f.Animator.GetBoneTransform(HumanBodyBones.Hips).position)
                            .ToArray();
                        Complete.Invoke(playback, null);
                        Record(rows, source, move, direction, "handoff", fighters);
                        var after = fighters.Select(f => f.Animator.GetBoneTransform(HumanBodyBones.Hips).position)
                            .ToArray();
                        float entry = before.Select((p, i) => Vector3.Distance(p, after[i])).Max();
                        float midpoint = (source.Animator.transform.position.x +
                            target.Animator.transform.position.x) * .5f;
                        for (int frame = 1; frame <= 21; frame++)
                        {
                            playback.PrepareRecoveryPoseEvaluation();
                            source.Animator.Update(1f / 60);
                            target.Animator.Update(1f / 60);
                            Late.Invoke(playback, null);
                            if (label == "After" && playback.IsRecovering)
                                CheckStableEvaluation(playback, fighters, frame == 6);
                            if (frame % 6 == 0 || frame == 21)
                                Record(rows, source, move, direction, "recovery" + frame, fighters);
                        }
                        report.AppendLine(FormattableString.Invariant(
                            $"{source.name}/{move.moveName}/{direction}: entry={entry:R}m; ") +
                            FormattableString.Invariant($"playing={playback.Playing}; ") +
                            FormattableString.Invariant($"sourceRoot={source.Animator.transform.position:F6}; ") +
                            FormattableString.Invariant($"targetRoot={target.Animator.transform.position:F6}"));
                        if (entry > .002f || playback.Playing || fighters.Any(f => f.IsBusy || !f.IsIdleAndSettled))
                            throw new InvalidOperationException("Slap recovery did not preserve its entry and settle.");
                        if (label == "After")
                            CheckFinalSpacing(source, target, move, midpoint);
                    }
                    finally
                    {
                        playback.Cancel();
                    }
                }
            });
            File.WriteAllText(Output + "/" + label + ".csv", rows.ToString());
            File.WriteAllText(Output + "/" + label + ".txt", report.ToString());
            if (label == "After")
                ValidatePreservedMotion();
        }

        static void Record(StringBuilder rows, CharacterCombat source, CombatTripletData move, int direction,
            string stage, CharacterCombat[] fighters)
        {
            foreach (var fighter in fighters)
            {
                var animator = fighter.Animator;
                var root = animator.transform.position;
                var hips = animator.GetBoneTransform(HumanBodyBones.Hips).position;
                var left = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                var right = animator.GetBoneTransform(HumanBodyBones.RightFoot).position;
                rows.AppendLine(FormattableString.Invariant(
                    $"{source.name},{move.moveName},{direction},{stage},{fighter.name},") +
                    FormattableString.Invariant($"{root.x:R},{root.y:R},{root.z:R},{hips.x:R},{hips.y:R},{hips.z:R},") +
                    FormattableString.Invariant($"{left.x:R},{left.y:R},{left.z:R},{right.x:R},{right.y:R},{right.z:R}"));
            }
        }
    }
}
