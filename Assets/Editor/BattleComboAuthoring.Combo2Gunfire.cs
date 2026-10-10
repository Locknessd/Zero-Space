using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class BattleComboAuthoring
    {
        public static void RepairCombo2OpeningGunfire()
        {
            var report = new StringBuilder();
            WithStudy((scene, game, fighters) =>
            {
                var bank = game.battleVfx.timeline;
                Undo.RecordObject(bank, "Restore combo_02 opening gunfire");
                foreach (var source in fighters)
                {
                    foreach (var fighter in fighters)
                        fighter.ResetCombat();
                    var target = fighters.Single(f => f != source);
                    var move = source.heavyCombatMoves.Single(m => m.moveName == "combo_02");
                    source.transform.position = Vector3.zero;
                    target.transform.position = Vector3.right * move.attackRange;
                    if (!source.ExecuteAttack(move, target))
                        throw new InvalidOperationException("Cannot sample combo_02 gunfire.");
                    var playback = source.SourcePlayback;
                    try
                    {
                        var profile = bank.FindMove(move);
                        var cues = profile.cues.ToList();
                        // Native 30 fps frames immediately before each opening recoil.
                        foreach (int frame in new[] { 12, 17 })
                        {
                            float seconds = frame / 30f;
                            cues.RemoveAll(c => Mathf.Abs(c.seconds - seconds) < .0001f &&
                                c.contactSource == "Gun" && (c.group == "gun_shot" || c.group == "heavy_hit"));
                            playback.EvaluateAt(seconds);
                            var probe = new BattlePresentationContactSetup.ContactProbe(playback,
                                source, target, "Gun");
                            var chest = target.Animator.GetBoneTransform(HumanBodyBones.Chest);
                            Vector3 offset = chest.InverseTransformPoint(probe.BodyPoint(chest.position));
                            cues.Add(OpeningGunCue("gun_shot", seconds, target.Animator.avatar, offset));
                            cues.Add(OpeningGunCue("heavy_hit", seconds, target.Animator.avatar, offset));
                            report.AppendLine($"{source.name}/combo_02 frame={frame} time={seconds:R}s: " +
                                "muzzle flash and receiver hit restored.");
                        }
                        profile.cues = cues.OrderBy(c => c.seconds)
                            .ThenBy(c => c.group.EndsWith("hit", StringComparison.Ordinal) ? 1 : 0).ToArray();
                    }
                    finally
                    {
                        playback.Cancel();
                    }
                }
                EditorUtility.SetDirty(bank);
                AssetDatabase.SaveAssetIfDirty(bank);
            });
            File.WriteAllText(Report + "/Combo2GunfireRepair.txt", report.ToString());
        }

        static BattleSfxBank.Cue OpeningGunCue(string group, float seconds, Avatar avatar, Vector3 offset)
        {
            return new BattleSfxBank.Cue
            {
                seconds = seconds,
                group = group,
                contactSource = "Gun",
                hasContactPoint = true,
                contactBone = HumanBodyBones.Chest,
                contactOffset = offset,
                avatarContacts = new[] { new BattleSfxBank.ContactAnchor
                    { avatar = avatar, bone = HumanBodyBones.Chest, offset = offset } }
            };
        }
    }
}
