using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class BattleAttackReview
    {
        // Native frame 78 is the knee impact after the receiver rotates across the attacker's body.
        const float Light3ImpactSeconds = 78f / 60f;

        public static void RepairLight3()
        {
            var report = new StringBuilder();
            BattleComboAuthoring.WithStudy((scene, game, fighters) =>
            {
                var bank = game.battleVfx.timeline;
                var profile = bank.FindMove(fighters[0].lightCombatMoves.Single(m => m.moveName == "Light_3"));
                var selections = new[] { "LeftUpperLeg" };
                var anchors = selections.ToDictionary(s => s, s => new List<BattleSfxBank.ContactAnchor>());
                var gaps = selections.ToDictionary(s => s, s => new List<float>());
                foreach (var source in fighters)
                {
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var target = fighters.Single(f => f != source);
                    var move = source.lightCombatMoves.Single(m => m.moveName == "Light_3");
                    if (bank.FindMove(move) != profile)
                        throw new InvalidOperationException("Light_3 no longer uses a shared native timeline.");
                    source.transform.position = Vector3.zero;
                    target.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Cannot measure Light_3 knee impact.");
                    var playback = source.SourcePlayback;
                    try
                    {
                        playback.EvaluateAt(Light3ImpactSeconds);
                        foreach (string selection in selections)
                        {
                            var probe = new BattlePresentationContactSetup.ContactProbe(playback,
                                source, target, selection);
                            float gap = probe.MeasureQuick(out var point, out _, out _);
                            var bone = HumanBodyBones.Chest;
                            var offset = target.Animator.GetBoneTransform(bone).InverseTransformPoint(point);
                            gaps[selection].Add(gap);
                            anchors[selection].Add(new BattleSfxBank.ContactAnchor
                            {
                                avatar = target.Animator.avatar,
                                bone = bone,
                                offset = offset
                            });
                            report.AppendLine($"{source.name} {Light3ImpactSeconds:R}s {selection}: " +
                                $"gap={gap:R}m; receiver={bone}; offset={offset.ToString("R")}");
                        }
                    }
                    finally
                    {
                        playback.Cancel();
                    }
                }
                File.WriteAllText(Output + "/Light3Repair.txt", report.ToString());
                string limb = selections.OrderBy(s => gaps[s].Max()).First();
                if (gaps[limb].Max() > .15f)
                    throw new InvalidOperationException("Light_3 knee impact requires timing refinement.");
                var existing = profile.cues.SingleOrDefault(c => IsContact(c) &&
                    Mathf.Abs(c.seconds - Light3ImpactSeconds) < .0001f);
                if (existing == null)
                {
                    if (profile.cues.Any(IsContact))
                        throw new InvalidOperationException("Preserve existing Light_3 impacts before repairing.");
                    existing = new BattleSfxBank.Cue
                    {
                        seconds = Light3ImpactSeconds,
                        group = "heavy_hit"
                    };
                    profile.cues = profile.cues.Append(existing).OrderBy(c => c.seconds).ToArray();
                }
                existing.hasContactPoint = true;
                existing.contactSource = limb;
                existing.contactBone = anchors[limb][0].bone;
                existing.contactOffset = anchors[limb][0].offset;
                existing.avatarContacts = anchors[limb].ToArray();
                EditorUtility.SetDirty(bank);
                AssetDatabase.SaveAssetIfDirty(bank);
                report.AppendLine("Added one knee impact with separate receiver skin anchors for both avatars.");
            });
            File.WriteAllText(Output + "/Light3Repair.txt", report.ToString());
        }

        public static void StudyLight3()
        {
            Directory.CreateDirectory(Output);
            var report = new StringBuilder("fighter,seconds,leftFoot,rightFoot,leftLowerLeg,rightLowerLeg\n");
            BattleComboAuthoring.WithStudy((scene, game, fighters) =>
            {
                foreach (var source in fighters)
                {
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var target = fighters.Single(f => f != source);
                    var move = source.lightCombatMoves.Single(m => m.moveName == "Light_3");
                    source.transform.position = Vector3.zero;
                    target.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Cannot study Light_3.");
                    var playback = source.SourcePlayback;
                    try
                    {
                        var times = Enumerable.Range(0, 24).Select(i => .3f + i / 15f).ToArray();
                        Capture(scene, playback, times, Output + "/Light3_" + source.name + "_motion.png");
                        foreach (float time in times)
                        {
                            playback.EvaluateAt(time);
                            var bones = new[]
                            {
                                source.Animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                                source.Animator.GetBoneTransform(HumanBodyBones.RightFoot),
                                target.Animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg),
                                target.Animator.GetBoneTransform(HumanBodyBones.RightLowerLeg)
                            };
                            report.AppendLine(source.name + "," + time.ToString("R") + "," +
                                string.Join(",", bones.Select(b => b.position.ToString("R"))));
                        }
                    }
                    finally
                    {
                        playback.Cancel();
                    }
                }
            });
            File.WriteAllText(Output + "/Light3Motion.csv", report.ToString());
        }
    }
}
