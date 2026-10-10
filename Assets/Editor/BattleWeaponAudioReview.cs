using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FrankRetarget.Editor;
using UnityEngine;

public static class BattleWeaponAudioReview
{
    public static void Validate()
    {
        var report = new StringBuilder();
        int cases = 0;
        int weaponCues = 0;
        int gunshots = 0;
        BattleComboAuthoring.WithStudy((scene, game, fighters) =>
        {
            var audio = game.battleSfx;
            var vfx = game.battleVfx;
            var bank = audio.bank;
            audio.enableAnnouncer = false;
            audio.enableHurtVoices = false;
            audio.enableContactLayers = true;
            audio.reproducibleVariation = true;
            foreach (var fighter in fighters)
            {
                fighter.battleSfx = audio;
                fighter.battleVfx = vfx;
            }
            foreach (var source in fighters)
            foreach (var move in source.lightCombatMoves.Concat(source.heavyCombatMoves))
            foreach (bool lethal in new[] { false, true })
            foreach (bool skip in new[] { false, true })
            {
                foreach (var fighter in fighters)
                    fighter.ResetCombat();
                audio.ResetForMatch();
                vfx.ResetForMatch();
                var target = fighters.Single(f => f != source);
                source.transform.position = Vector3.zero;
                target.transform.position = Vector3.right * move.attackRange;
                var heard = new List<string>();
                Action<string, AudioClip> observe = (id, clip) =>
                {
                    if (!clip || !bank.FindGroup(id).clips.Contains(clip))
                        throw new InvalidOperationException("Audio played an unassigned clip: " + id);
                    heard.Add(id);
                };
                audio.CuePlayed += observe;
                try
                {
                    if (!source.ExecuteAttack(move, target, lethal))
                        throw new InvalidOperationException("Audio review attack rejected: " + move.moveName);
                    var playback = source.SourcePlayback;
                    var profile = playback.PresentationProfile(bank);
                    var expected = new List<string>();
                    foreach (var cue in profile.cues)
                    {
                        string id = lethal && cue.finalLanding ? "knockout_fall" :
                            bank.ResolveWeaponGroup(move, cue, profile);
                        var group = bank.FindGroup(id);
                        if (group?.clips == null || group.clips.Length == 0)
                            throw new InvalidOperationException("Audio cue group is missing: " + id);
                        bool hit = cue.group == "light_hit" || cue.group == "heavy_hit" || cue.group == "stab_hit";
                        if (cue.contactSource == "Gun" || move.weapon == TrumpWeaponManager.WeaponType.None ||
                            Enum.TryParse<HumanBodyBones>(cue.contactSource, out _))
                        {
                            if (!(lethal && cue.finalLanding) && id != cue.group)
                                throw new InvalidOperationException("Sword audio replaced a gunshot or body cue.");
                        }
                        if (hit && cue.contactSource == "Weapon" && move.skill == BattleSkill.None)
                        {
                            if (!group.clips.All(c => UnityEditor.AssetDatabase.GetAssetPath(c)
                                .Contains("/WeaponPacks/")))
                                throw new InvalidOperationException("Weapon hit did not use a reviewed pack clip.");
                            weaponCues++;
                        }
                        if (hit && cue.contactSource == "Shield" && id != "shield_bash_hit")
                            throw new InvalidOperationException("Shield bash was given a cutting sound.");
                        expected.Add(id);
                        expected.AddRange(group.layers);
                    }
                    var times = skip ? new[] { playback.Duration } :
                        profile.cues.Select(c => c.seconds).Distinct().ToArray();
                    foreach (float time in times)
                    {
                        playback.EvaluateAt(time);
                        audio.AdvanceSequence(playback, time);
                        vfx.AdvanceSequence(playback, time);
                        int count = heard.Count;
                        audio.AdvanceSequence(playback, time);
                        vfx.AdvanceSequence(playback, time);
                        audio.AdvanceSequence(playback, Mathf.Max(0, time - .1f));
                        if (heard.Count != count || Mathf.Abs(playback.SampleTime - time) > .0001f)
                            throw new InvalidOperationException(
                                "Sound repeated or changed the native animation clock.");
                    }
                    if (!heard.OrderBy(id => id).SequenceEqual(expected.OrderBy(id => id)))
                        throw new InvalidOperationException("Lost or unexpected audio: " + move.moveName +
                            " expected=" + string.Join(",", expected) + " heard=" + string.Join(",", heard));
                    if (move.moveName == "combo_02" && heard.Count(id => id == "gun_shot") != 3)
                        throw new InvalidOperationException("Combo_02 opening gunshots were lost.");
                    gunshots += heard.Count(id => id == "gun_shot");
                    if (audio.DroppedCueCount != 0)
                        throw new InvalidOperationException("Preview dropped an audio cue.");
                    playback.Cancel();
                    int before = heard.Count;
                    audio.AdvanceSequence(playback, playback.Duration);
                    audio.BeginRecovery(playback);
                    if (heard.Count != before)
                        throw new InvalidOperationException("Cancelled attack emitted delayed audio.");
                    report.AppendLine($"PASS {source.name}/{move.moveName} lethal={lethal} skip={skip}: " +
                        $"{heard.Count} cues/layers, correct weapon/body/gun routing, deduplication and cancellation.");
                    cases++;
                }
                finally
                {
                    source.SourcePlayback?.Cancel();
                    audio.CuePlayed -= observe;
                }
            }
        });
        report.AppendLine($"PASS {cases} cases; {weaponCues} weapon-hit selections; {gunshots} gunshots retained.");
        report.AppendLine("Checked native timeline/contact callbacks in Editor preview; no hardware listening claim.");
        File.WriteAllText(BattleWeaponAudioSetup.ReportPath + "/Validation.txt", report.ToString());
    }
}
