using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FrankRetarget;
using FrankRetarget.Editor;
using UnityEditor;
using UnityEngine;

public static class BattleAudioSyncReview
{
    sealed class Expected
    {
        public BattleSfxBank.Cue cue;
        public string id;
        public bool layer;
        public float observedTime;
    }

    [MenuItem("Tools/Battle/Audio/Validate all animation sound timings")]
    public static void Validate() => Run(true);

    public static void Inspect() => Run(false);

    static void Run(bool strict)
    {
        Directory.CreateDirectory(BattleAudioTimingSetup.ReportPath);
        var report = new StringBuilder();
        var timings = new StringBuilder("fighter,move,lethal,step,group,cueSeconds,poseSeconds,errorSeconds\n");
        int cases = 0;
        int sounds = 0;
        int contacts = 0;
        float maxDrift = 0;
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
            foreach (bool lethal in strict ? new[] { false, true } : new[] { false })
            foreach (float step in strict ? new[] { 1f / 30f, .173f, float.PositiveInfinity } : new[] { .173f })
            {
                foreach (var fighter in fighters)
                    fighter.ResetCombat();
                audio.ResetForMatch();
                vfx.ResetForMatch();
                var target = fighters.Single(f => f != source);
                source.transform.position = Vector3.zero;
                target.transform.position = Vector3.right * move.attackRange;
                if (!source.ExecuteAttack(move, target, lethal))
                    throw new InvalidOperationException("Audio timing attack rejected: " + move.moveName);
                var pair = source.SourcePlayback;
                var profile = pair.PresentationProfile(bank);
                if (profile == null)
                    throw new InvalidOperationException("No presentation profile: " + move.moveName);
                var pending = new Dictionary<string, Queue<Expected>>();
                var heard = new List<Expected>();
                var hitTimes = new Dictionary<BattleSfxBank.Cue, float>();
                foreach (var cue in profile.cues)
                {
                    if (!float.IsFinite(cue.seconds) || cue.seconds < 0 || cue.seconds > pair.Duration)
                        throw new InvalidOperationException("Cue outside animation: " + move.moveName);
                    string id = lethal && cue.finalLanding ? "knockout_fall" :
                        bank.ResolveWeaponGroup(move, cue, profile);
                    AddExpected(pending, cue, id, false);
                    foreach (var layer in bank.FindGroup(id).layers ?? Array.Empty<string>())
                        AddExpected(pending, cue, layer, true);
                }
                Action<string, AudioClip> onSound = (id, clip) =>
                {
                    if (!pending.TryGetValue(id, out var queue) || queue.Count == 0)
                        throw new InvalidOperationException("Unexpected sound: " + id);
                    var expected = queue.Dequeue();
                    expected.observedTime = pair.SampleTime;
                    heard.Add(expected);
                    float drift = Mathf.Abs(pair.SampleTime - expected.cue.seconds);
                    maxDrift = Mathf.Max(maxDrift, drift);
                    timings.AppendLine(FormattableString.Invariant(
                        $"{source.name},{move.moveName},{lethal},{step},{id},{expected.cue.seconds:R},") +
                        FormattableString.Invariant($"{pair.SampleTime:R},{drift:R}"));
                    if (strict && drift > .0001f)
                        throw new InvalidOperationException($"Late sound: {move.moveName}/{id}: {drift:F4}s");
                    sounds++;
                };
                Action<BattleVfxPlayer.Impact> onContact = impact =>
                {
                    if (impact.playback != pair || impact.cue == null || hitTimes.ContainsKey(impact.cue))
                        throw new InvalidOperationException("Duplicate or foreign audio/VFX contact.");
                    if (Mathf.Abs(pair.SampleTime - impact.seconds) > .0001f)
                        throw new InvalidOperationException("VFX missed the shared contact pose.");
                    hitTimes.Add(impact.cue, impact.seconds);
                    contacts++;
                };
                audio.CuePlayed += onSound;
                vfx.ContactOccurred += onContact;
                try
                {
                    float time = 0;
                    while (time < pair.Duration)
                    {
                        time = Mathf.Min(pair.Duration, time + step);
                        pair.AdvanceTo(time);
                        int count = heard.Count;
                        pair.AdvanceTo(time);
                        pair.AdvanceTo(Mathf.Max(0, time - .05f));
                        if (heard.Count != count || Mathf.Abs(pair.SampleTime - time) > .0001f)
                            throw new InvalidOperationException("Audio repeated or moved the animation clock.");
                    }
                    if (pending.Values.Any(queue => queue.Count != 0))
                        throw new InvalidOperationException("Lost audio cue: " + move.moveName);
                    foreach (var sound in heard.Where(sound => !sound.layer && IsContact(sound.cue)))
                        if (!hitTimes.TryGetValue(sound.cue, out float hit) ||
                            Mathf.Abs(sound.observedTime - hit) > .0001f)
                            throw new InvalidOperationException("Impact sound did not coincide with hit VFX.");
                    if (audio.DroppedCueCount != 0)
                        throw new InvalidOperationException("Dropped battle audio.");
                    pair.Cancel();
                    int before = heard.Count;
                    pair.AdvanceTo(pair.Duration);
                    audio.AdvanceSequence(pair, pair.Duration);
                    audio.BeginRecovery(pair);
                    if (heard.Count != before)
                        throw new InvalidOperationException("Sound played after attack cancellation.");
                    report.AppendLine($"PASS {source.name}/{move.moveName} lethal={lethal} step={step}: " +
                        $"{heard.Count} sounds/layers; all contacts, deduplication and cancellation verified.");
                    cases++;
                }
                finally
                {
                    pair.Cancel();
                    audio.CuePlayed -= onSound;
                    vfx.ContactOccurred -= onContact;
                }
            }
        });
        report.AppendLine($"{cases} cases; {sounds} sounds/layers; {contacts} VFX contacts; " +
            $"maximum sound/pose drift {maxDrift * 1000:F3} ms.");
        if (strict)
            ValidateTransients(report);
        string prefix = strict ? "Validation" : "Before";
        File.WriteAllText(BattleAudioTimingSetup.ReportPath + "/" + prefix + ".txt", report.ToString());
        File.WriteAllText(BattleAudioTimingSetup.ReportPath + "/" + prefix + ".csv", timings.ToString());
    }

    static void AddExpected(Dictionary<string, Queue<Expected>> pending, BattleSfxBank.Cue cue,
        string id, bool layer)
    {
        if (!pending.TryGetValue(id, out var queue))
        {
            queue = new Queue<Expected>();
            pending.Add(id, queue);
        }
        queue.Enqueue(new Expected { cue = cue, id = id, layer = layer });
    }

    static bool IsContact(BattleSfxBank.Cue cue) => cue.group == "light_hit" || cue.group == "heavy_hit" ||
        cue.group == "stab_hit" || cue.group == "body_fall" || cue.group == "knockout_fall";

    static void ValidateTransients(StringBuilder report)
    {
        var bank = AssetDatabase.LoadAssetAtPath<BattleSfxBank>(BattlePresentationAudioSetup.BankPath);
        float worst = 0;
        int count = 0;
        foreach (var group in bank.groups.Where(group => BattleAudioTimingSetup.ShouldAlign(group.id)))
        for (int index = 0; index < group.clips.Length; index++)
        {
            var clip = group.clips[index];
            float offset = group.startOffsets[index];
            float remaining = Mathf.Max(0, BattleAudioTimingSetup.AttackOnset(clip) - offset) /
                Mathf.Max(.5f, Mathf.Min(group.pitch.x, group.pitch.y));
            if (offset < 0 || offset >= clip.length || remaining > .010f)
                throw new InvalidOperationException("Sound attack too late: " + group.id + "/" + clip.name);
            worst = Mathf.Max(worst, remaining);
            count++;
        }
        report.AppendLine($"PASS {count} selected clip variants/layers: sound attack within {worst * 1000:F3} ms " +
            "of its cue at the slowest authored pitch (30% peak RMS, 2 ms windows).");
        report.AppendLine("Native Editor playback/contact dispatch and waveform timing; " +
            "no hardware latency measurement.");
    }
}
