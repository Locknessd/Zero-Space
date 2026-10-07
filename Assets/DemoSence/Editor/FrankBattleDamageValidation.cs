using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void ValidateBattleHitDamage()
        {
            Directory.CreateDirectory("GeneratedAssets/BattleDamageReview");
            var bank = AssetDatabase.LoadAssetAtPath<BattleSfxBank>("Assets/Audio/Battle/BattleSfxBank.asset");
            var report = new StringBuilder(); int cases = 0;
            foreach (var profile in bank.moves)
            foreach (long total in new[] {1L, 100, 103, (long)int.MaxValue + 13, long.MaxValue})
            {
                float duration = Mathf.Max(profile.attack.length, profile.reaction.length);
                var times = BattleHitDamageSequence.ContactTimes(profile, duration);
                if (times.Length == 0) throw new Exception("No damage contacts: " + profile.label);
                var amounts = new List<long>(); var health = new List<long>();
                var sequence = new BattleHitDamageSequence(total, 0, total, times, (hp, part) => { amounts.Add(part); health.Add(hp); });
                sequence.Advance(Mathf.Max(-1, times[0] - .01f));
                if (amounts.Count != 0) throw new Exception("Damage during windup.");
                foreach (float time in times)
                {
                    sequence.Advance(time); int count = amounts.Count;
                    sequence.Advance(time); sequence.Advance(time - .2f);
                    if (count != amounts.Count) throw new Exception("Repeated/reversed clock applied damage twice.");
                }
                if (amounts.Count != times.Length || amounts.Sum() != total || amounts.Max() - amounts.Min() > 1 ||
                    health.Last() != 0 || health.Take(health.Count - 1).Any(hp => hp <= 0))
                    throw new Exception("Unbalanced damage or premature zero HP: " + profile.label);
                var skipped = new List<long>();
                new BattleHitDamageSequence(total, 0, total, times, (hp, part) => skipped.Add(part)).Advance(duration);
                if (!skipped.SequenceEqual(amounts)) throw new Exception("Frame skip lost damage.");
                cases++;
            }
            var thirds = new List<long>();
            new BattleHitDamageSequence(1000, 900, 100, new[] {1f, 2f, 3f}, (hp, part) => thirds.Add(part)).Complete();
            if (!thirds.SequenceEqual(new[] {33L, 33, 34})) throw new Exception("Expected 100 damage split 33/33/34.");
            int called = 0;
            var cancelled = new BattleHitDamageSequence(1000, 900, 100, new[] {1f, 2f}, (hp, part) => called++);
            cancelled.Advance(1); cancelled.Cancel(); cancelled.Complete();
            if (called != 1) throw new Exception("Cancelled damage returned.");
            var overkill = new List<long>();
            new BattleHitDamageSequence(73, 0, 1000, new[] {1f, 2f, 3f, 4f}, (hp, part) => overkill.Add(hp)).Complete();
            if (overkill.Take(3).Any(hp => hp <= 0) || overkill.Last() != 0) throw new Exception("Overkill reached zero before last contact.");
            report.AppendLine($"PASS {cases} profile/total cases: exact totals, portions differ by at most 1, zero HP at last contact, windup, frame skip and duplicate/reversed clock.");
            report.AppendLine("PASS 100 damage / 3 hits = 33 + 33 + 34; tiny and 64-bit totals, overkill and cancellation.");
            File.WriteAllText("GeneratedAssets/BattleDamageReview/Validation.txt", report.ToString());
        }

        public static void BattleDamagePlayCheck() => FrankBattleSfxPlayCheck.Start(false, true);
    }

    // Observes the real queue, sampled source clock, native HUD and effect cues.
    public static class FrankBattleDamagePlayProbe
    {
        static GameManager game;
        static CharacterCombat source;
        static FrankBattlePairPlayback playback;
        static MemeBattleUI.FighterSlot slot;
        static float[] contacts;
        static long before, after, total, maximum;
        static int playbackId, observed;
        static string failure, turn;
        static readonly List<long> portions = new List<long>();
        public static int CheckedHits { get; private set; }

        public static void Begin(GameManager battle, CharacterCombat attacker, CombatTripletData move, int step, bool lethal)
        {
            Detach(); game = battle; source = attacker;
            bool left = source == game.leftCombat; slot = left ? game.uiManager.right : game.uiManager.left;
            var healthLabel = slot.healthText.text.Split('/');
            before = long.Parse(healthLabel[0]); maximum = long.Parse(healthLabel[1]);
            var continuation = move.grapple ? move.grapple.For(move.grappleOutcome) : null;
            float duration = continuation != null ? move.grapple.decisionSeconds + continuation.Duration :
                Mathf.Max(move.sourcePair.attack.length, move.sourcePair.reactionDelay + move.sourcePair.reaction.length);
            contacts = BattleHitDamageSequence.ContactTimes(game.battleSfx.bank.FindMove(move), duration);
            total = contacts.Length == 0 ? 0 : lethal ? before + 100 : 103;
            after = lethal ? 0 : before - total;
            observed = 0; portions.Clear(); playbackId = source.PlaybackId + 1; turn = "damage-" + step;
            var damage = new JObject { ["actorCharacterId"] = left ? "bot_a" : "bot_b", ["targetCharacterId"] = left ? "bot_b" : "bot_a",
                ["hpBeforeAtomic"] = before, ["hpAfterAtomic"] = after, ["damageAtomic"] = total,
                ["animationId"] = move.actionDefinition ? move.moveName :
                    move.weapon == TrumpWeaponManager.WeaponType.None ? "attack_light" : "attack_heavy" };
            string json = Event("damage-" + step, "DAMAGE_APPLIED", damage);
            game.ApplyRawMessage(json); game.ApplyRawMessage(json);
            string hp = Event("hp-" + step, "HP_CHANGED", new JObject { ["characterId"] = left ? "bot_b" : "bot_a", ["hpBeforeAtomic"] = before, ["hpAfterAtomic"] = after });
            game.ApplyRawMessage(hp); game.ApplyRawMessage(hp);
        }

        public static void Tick()
        {
            if (failure != null) throw new Exception(failure);
            if (!source || playback || source.PlaybackId != playbackId || !source.SourcePlayback || !source.SourcePlayback.Playing) return;
            playback = source.SourcePlayback;
            if (contacts.Length > 0 && playback.SampleTime >= contacts[0])
                throw new Exception("Probe attached after the first hit.");
            playback.TimelineAdvanced += Observe;
        }

        static void Observe(FrankBattlePairPlayback pair, float time)
        {
            try
            {
                if (contacts.Length == 0)
                {
                    if (slot.healthText.text != before + "/" + maximum)
                        throw new Exception("A non-damaging interaction changed health.");
                    return;
                }
                int reached = contacts.Count(c => c <= time);
                long lost = before - after;
                long cumulative = lost / contacts.Length * reached + Math.Max(0, reached - (contacts.Length - lost % contacts.Length));
                long expected = before - cumulative;
                if (slot.healthText.text != expected + "/" + maximum || Mathf.Abs(slot.hpSlider.value - expected / (float)maximum) > .0001f)
                    throw new Exception($"HUD lost synchronization at {pair.Move.moveName} t={time:F3}, contact={reached}: {slot.healthText.text}, expected {expected}.");
                if (reached > observed)
                {
                    for (int i = observed; i < reached; i++) portions.Add(total / contacts.Length + (i >= contacts.Length - total % contacts.Length ? 1 : 0));
                    if (slot.damageText.text != "-" + portions.Last()) throw new Exception("Damage popup has the whole total or the wrong portion.");
                    if (expected == 0 && reached != contacts.Length) throw new Exception("KO health reached zero early.");
                    CheckedHits += reached - observed; observed = reached;
                    if (pair.Move.moveName == "Heavy_8" && observed == 2)
                        ScreenCapture.CaptureScreenshot("GeneratedAssets/BattleDamageReview/" + source.name + "_SecondHit.png");
                }
            }
            catch (Exception exception) { failure = exception.Message; }
        }

        public static string Complete()
        {
            if (failure != null) throw new Exception(failure);
            if (observed != contacts.Length || portions.Sum() != total || slot.healthText.text != after + "/" + maximum)
                throw new Exception("Missing final per-hit HP/damage update.");
            string detail = $"PASS per-hit {source.name} {playback.Move.moveName}: {string.Join(" + ", portions)} = {total}; HP {before} -> {after}, exact contact frames.";
            string late = Event(turn + "-late-hp", "HP_CHANGED", new JObject { ["characterId"] = source == game.leftCombat ? "bot_b" : "bot_a", ["hpAfterAtomic"] = after });
            game.ApplyRawMessage(late); Detach(); return detail;
        }

        static string Event(string id, string type, JObject payload) => new JObject { ["type"] = "meme_battle_event", ["event"] = new JObject
        { ["matchId"] = "local-sfx-validation", ["eventId"] = id, ["eventType"] = type, ["turnId"] = turn, ["payload"] = payload } }.ToString();

        static void Detach() { if (playback) playback.TimelineAdvanced -= Observe; playback = null; }
        public static void Reset() { Detach(); game = null; source = null; failure = null; CheckedHits = 0; portions.Clear(); }
    }
}
