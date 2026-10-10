using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class BattleAttackReview
    {
        public static void RepairFindings()
        {
            var report = new StringBuilder(File.Exists(Output + "/Repairs.txt")
                ? File.ReadAllText(Output + "/Repairs.txt") + "\nLanding refinement:\n" : "");
            BattleComboAuthoring.WithStudy((scene, game, fighters) =>
            {
                var bank = game.battleVfx.timeline;
                var source = fighters.Single(f => f.name == "Mankey");
                var target = fighters.Single(f => f != source);
                var move = source.lightCombatMoves.Single(m => m.moveName == "Vol10_CmnAtemi3");
                foreach (var fighter in fighters)
                    fighter.ResetCombat();
                source.transform.position = Vector3.zero;
                target.transform.position = Vector3.right * move.attackRange;
                if (!source.ExecuteAttack(move, target))
                    throw new InvalidOperationException("Cannot measure Atemi3 contact.");
                var playback = source.SourcePlayback;
                try
                {
                    var cue = bank.FindMove(move).cues.Single(c => c.group == "light_hit");
                    playback.EvaluateAt(cue.seconds);
                    var probe = new BattlePresentationContactSetup.ContactProbe(playback,
                        source, target, cue.contactSource);
                    cue.TryContactPosition(target.Animator, out var previous);
                    float before = probe.SourceDistance(previous);
                    float gap = probe.Measure(out _, out var bone, out var offset);
                    if (gap > .15f)
                        throw new InvalidOperationException("Atemi3 needs timing refinement: " + gap);
                    ReplaceAnchor(cue, target, bone, offset);
                    report.AppendLine($"Atemi3 Mankey at {cue.seconds:R}s: source gap {before:R}m -> {gap:R}m; " +
                        "native timing and other fighter anchors preserved.");
                }
                finally
                {
                    playback.Cancel();
                }
                foreach (var attacker in fighters)
                foreach (var combo in attacker.heavyCombatMoves.Where(m => m.moveName.StartsWith("combo_")))
                {
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var victim = fighters.Single(f => f != attacker);
                    attacker.transform.position = Vector3.zero;
                    victim.transform.position = Vector3.right * combo.attackRange;
                    if (!attacker.ExecuteAttack(combo, victim))
                        throw new InvalidOperationException("Cannot measure combo landing.");
                    playback = attacker.SourcePlayback;
                    try
                    {
                        var cue = bank.FindMove(combo).cues.Single(c => c.group == "body_fall");
                        float start = cue.seconds;
                        int landingFrames = combo.moveName == "combo_01" ? 96 : 30;
                        for (int frame = 0; frame <= landingFrames; frame++)
                        {
                            float time = Mathf.Min(playback.Duration, start + frame / 240f);
                            playback.EvaluateAt(time);
                            if (BattlePresentationContactSetup.EvaluatedFloor(victim) > .01f)
                                continue;
                            cue.seconds = time;
                            break;
                        }
                        playback.EvaluateAt(cue.seconds);
                        var probe = new BattlePresentationContactSetup.ContactProbe(playback, attacker, victim, "Gun");
                        probe.GroundAnchor(out var bone, out var offset);
                        cue.hasContactPoint = true;
                        cue.contactSource = "Receiver";
                        cue.contactBone = bone;
                        cue.contactOffset = offset;
                        ReplaceAnchor(cue, victim, bone, offset);
                        report.AppendLine($"{attacker.name}/{combo.moveName} landing {cue.seconds:R}s: " +
                            $"{bone} skin anchor; floor={probe.FloorHeight:R}m.");
                    }
                    finally
                    {
                        playback.Cancel();
                    }
                }
                EditorUtility.SetDirty(bank);
                AssetDatabase.SaveAssetIfDirty(bank);
            });
            File.WriteAllText(Output + "/Repairs.txt", report.ToString());
        }

        static void ReplaceAnchor(BattleSfxBank.Cue cue, CharacterCombat target,
            HumanBodyBones bone, Vector3 offset)
        {
            cue.avatarContacts = (cue.avatarContacts ?? Array.Empty<BattleSfxBank.ContactAnchor>())
                .Where(c => c.avatar != target.Animator.avatar)
                .Append(new BattleSfxBank.ContactAnchor
                    { avatar = target.Animator.avatar, bone = bone, offset = offset })
                .ToArray();
        }
    }
}
