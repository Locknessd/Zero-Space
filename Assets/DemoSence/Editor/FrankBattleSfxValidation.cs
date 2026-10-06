using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void ValidateBattleSfx()
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var report = new StringBuilder();
            var tick = typeof(FrankBattlePairPlayback).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var audio = game.battleSfx;
                if (!audio || !audio.bank || game.leftCombat.battleSfx != audio || game.rightCombat.battleSfx != audio)
                    throw new Exception("Missing scene/fighter audio bindings");
                if (audio.masterVolume <= 0) throw new Exception("Battle SFX muted");
                var bank = audio.bank;
                if (bank.groups.SelectMany(g => g.clips).Distinct().Count() != 37) throw new Exception("Expected the 37 reviewed playback clips");
                foreach (var group in bank.groups)
                {
                    if (group.clips.Length != group.clipGains.Length || group.clips.Any(c => !c || c.samples <= 0 || c.length <= 0))
                        throw new Exception("Invalid audio clips/gains for " + group.id);
                }
                var listeners = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<AudioListener>()).Count(l => l.enabled);
                if (listeners != 1) throw new Exception("Expected exactly one active AudioListener, got " + listeners);
                var fighters = new[] { game.leftCombat, game.rightCombat };
                int cases = 0;
                foreach (var attacker in fighters)
                foreach (var move in attacker.lightCombatMoves.Concat(attacker.heavyCombatMoves))
                foreach (bool lethal in new[] { false, true })
                {
                    var receiver = fighters.Single(f => f != attacker);
                    foreach (var f in fighters) f.ResetCombat();
                    audio.ResetForMatch();
                    attacker.transform.position = Vector3.zero;
                    receiver.transform.position = Vector3.right * move.attackRange;
                    var profile = bank.FindMove(move);
                    float duration = Mathf.Max(move.sourcePair.attack.length, move.sourcePair.reactionDelay + move.sourcePair.reaction.length);
                    if (profile == null || profile.cues.Length == 0 || profile.cues.Count(c => c.finalLanding) != 1)
                        throw new Exception("Missing timeline/final landing: " + move.moveName);
                    if (profile.cues.Any(c => c.seconds < 0 || c.seconds > duration || bank.FindGroup(c.group) == null) ||
                        !profile.cues.Select(c => c.seconds).SequenceEqual(profile.cues.Select(c => c.seconds).OrderBy(t => t)))
                        throw new Exception("Invalid cue timing/group: " + move.moveName);
                    var heard = new List<string>();
                    Action<string, AudioClip> onCue = (id, clip) => heard.Add(id);
                    audio.CuePlayed += onCue;
                    try
                    {
                        if (!attacker.ExecuteAttack(move, receiver, lethal)) throw new Exception("Attack rejected");
                        var pair = attacker.SourcePlayback;
                        int playbackId = receiver.PlaybackId;
                        audio.PlayFightOnce();
                        // Irregular steps cross timestamps; repeats and reverse seeking must not replay cues.
                        for (float time = 0; time < duration; time += .173f)
                        {
                            pair.EvaluateAt(time);
                            audio.AdvanceSequence(pair, time);
                            int count = heard.Count;
                            audio.AdvanceSequence(pair, time);
                            audio.AdvanceSequence(pair, Mathf.Max(0, time - .3f));
                            if (heard.Count != count) throw new Exception("Duplicate cue on repeated/backwards sample");
                        }
                        pair.EvaluateAt(duration);
                        audio.AdvanceSequence(pair, duration);
                        var expected = new List<string> { "fight_start" };
                        expected.AddRange(profile.cues.Select(c => lethal && c.finalLanding ? "knockout_fall" : c.group));
                        if (!heard.SequenceEqual(expected)) throw new Exception("Cue order/count mismatch for " + move.moveName);
                        tick.Invoke(pair, null);
                        if (!lethal)
                        {
                            expected.Add("getup");
                            audio.BeginRecovery(pair);
                            if (receiver.PlaybackId != playbackId || !receiver.IsBusy) throw new Exception("Recovery lost sequence ownership");
                            for (int i = 0; i < 140; i++)
                            {
                                receiver.Animator.Update(move.sourcePair.getUp.length / 120f);
                                tick.Invoke(pair, null);
                            }
                        }
                        else
                        {
                            for (int i = 0; i < 30; i++) tick.Invoke(pair, null);
                            if (!receiver.IsDead || receiver.Animator.enabled) throw new Exception("Death pose ownership changed");
                        }
                        if (!heard.SequenceEqual(expected) || pair.Playing || attacker.IsBusy || receiver.IsBusy)
                            throw new Exception("End/recovery produced duplicate audio or blocked completion");
                        audio.PlayVictoryOnce(); audio.PlayVictoryOnce();
                        if (heard.Count(id => id == "victory") != 1) throw new Exception("Victory played more than once");
                        // Cancel discards the remaining timeline; it cannot emit delayed hit/getup cues.
                        foreach (var f in fighters) f.ResetCombat();
                        audio.ResetForMatch();
                        attacker.transform.position = Vector3.zero;
                        receiver.transform.position = Vector3.right * move.attackRange;
                        if (!attacker.ExecuteAttack(move, receiver)) throw new Exception("Restart rejected");
                        pair = attacker.SourcePlayback;
                        audio.AdvanceSequence(pair, .1f);
                        pair.Cancel();
                        int beforeCancelTick = heard.Count;
                        audio.AdvanceSequence(pair, duration);
                        audio.BeginRecovery(pair);
                        if (heard.Count != beforeCancelTick) throw new Exception("Audio fired after cancellation");
                        report.AppendLine($"PASS {attacker.name} {move.moveName} lethal={lethal}: {profile.cues.Length} ordered motion cues, " +
                            (lethal ? "one knockout and no getup" : "one getup and unchanged PlaybackId") + "; no replay, cancellation clean.");
                        cases++;
                    }
                    finally { audio.CuePlayed -= onCue; foreach (var f in fighters) f.ResetCombat(); }
                }
                report.AppendLine($"PASS {cases} source sequence cases, {bank.groups.Sum(g => g.clips.Length)} AudioClips, both fighter bindings, one listener, Fight/Victory deduplication.");
            }
            catch (Exception e) { report.AppendLine("FAIL " + e); throw; }
            finally
            {
                Directory.CreateDirectory("Temp/FrankRetarget");
                File.WriteAllText("Temp/FrankRetarget/battle-sfx-validation.txt", report.ToString());
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
