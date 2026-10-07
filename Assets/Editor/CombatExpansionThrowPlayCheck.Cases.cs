using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionThrowPlayCheck
    {
        static readonly HashSet<BattleSfxBank.Cue> observedCues = new HashSet<BattleSfxBank.Cue>();
        static BattleSfxBank.Cue[] expectedCues;
        static bool sawAudio, sawEffects, sawHold, sawLights, sawShake;

        static void StartCase()
        {
            Require(fighters.All(fighter => fighter.IsIdleAndSettled), "Next case requires two settled idle fighters.");
            int sourceIndex = Orientation % 2;
            source = fighters[sourceIndex];
            target = fighters[1 - sourceIndex];
            sourceSide = sourceIndex == 0 ? PlayerUI.Side.Left : PlayerUI.Side.Right;
            action = Guard ? Actions[step % 9 + 1] : Queued ? Actions[(step - GuardCases) % Actions.Length] :
                "Vol10_BRAIN";
            move = game.FindCombatAction(sourceSide, action);
            Require(move != null && move.IsValid && move.sourcePair != null && move.sourcePair.Valid,
                "Missing valid source-pair action: " + action);
            for (int i = 0; i < fighters.Length; i++)
                fighters[i].Animator.transform.SetPositionAndRotation(
                    positions[Orientation >= 2 ? 1 - i : i], rotations[i]);
            if (Throw)
            {
                Require(move.weapon == TrumpWeaponManager.WeaponType.None && move.sourcePair.getUp,
                    "Throw must be unarmed with receiver recovery.");
                if (action == "Vol10_BRAIN")
                    Require(move.sourcePair.attackerGetUp, "Brainbuster requires attacker recovery.");
            }
            var profile = game.battleSfx.bank.FindMove(move);
            Require(profile != null, "Missing source presentation profile.");
            expectedCues = profile.cues.Where(cue => cue.group == "body_fall" || cue.group == "light_hit" ||
                cue.group == "heavy_hit" || cue.group == "stab_hit").ToArray();
            if (Throw)
                Require(expectedCues.Length == (action == "Vol10_GSWING" ? 2 : 1) &&
                    expectedCues.All(cue => cue.group == "body_fall" && cue.damageOnLanding),
                    "Throw requires exact authored damaging landing contacts; grip is nondamaging.");
            pair = null;
            starts = contacts = sourceEnded = targetEnded = 0;
            sourceRecovered = targetRecovered = interrupted = false;
            sourceRecoveryProgress = targetRecoveryProgress = previousSample = 0;
            sawAudio = sawEffects = sawHold = sawLights = sawShake = false;
            observedCues.Clear();
            audioBefore = game.battleSfx.PlayedCueCount;
            effectsBefore = game.battleVfx.PlayedEffectCount;
            holdsBefore = feedback.HitStopCount;
            lightsBefore = lighting.PlayedFlashCount;
            shakesBefore = shake.ShakeCount;
            failure = null;
            settledAt = 0;
            caseBegan = EditorApplicationTime();
            activeCase = true;
            report.AppendLine($"START case={step + 1} scenario={Scenario} action={action} " +
                $"source={source.name} reversed={Orientation >= 2}");
            Write();
            if (Guard)
                CheckRangeGuard();
            else if (Queued)
                Require(game.EnqueueCombatAction(sourceSide, action), "Named gameplay queue rejected action.");
            else
            {
                PositionDirectPair(move.attackRange * .98f);
                Require(source.ExecuteAttack(move, target, Lethal), "Direct throw was rejected.");
                Require(pair && pair.Playing && starts == 1, "Direct throw did not publish a sequence start.");
            }
        }

        static double EditorApplicationTime() => UnityEditor.EditorApplication.timeSinceStartup;

        static void PositionDirectPair(float distance)
        {
            var sourceRoot = source.Animator.transform;
            var targetRoot = target.Animator.transform;
            var a = sourceRoot.position;
            var b = targetRoot.position;
            float sign = b.x >= a.x ? 1 : -1;
            float midpoint = (a.x + b.x) * .5f;
            a.x = midpoint - sign * distance * .5f;
            b.x = midpoint + sign * distance * .5f;
            sourceRoot.SetPositionAndRotation(a, Quaternion.LookRotation(Vector3.right * sign));
            targetRoot.SetPositionAndRotation(b, Quaternion.LookRotation(Vector3.left * sign));
        }

        static void CheckRangeGuard()
        {
            PositionDirectPair(move.attackRange + 1);
            sourceId = source.PlaybackId;
            targetId = target.PlaybackId;
            var handler = Debug.unityLogger.logHandler;
            var capture = new RangeLogCapture(handler, "CharacterCombat [" + source.name + "]: Mục tiêu ngoài tầm: ");
            bool accepted;
            try
            {
                Debug.unityLogger.logHandler = capture;
                accepted = source.ExecuteAttack(move, target);
            }
            finally
            {
                Debug.unityLogger.logHandler = handler;
            }
            Require(!accepted && capture.ExpectedErrors == 1, "Out-of-range guard did not reject with expected error.");
            Require(starts == 0 && contacts == 0 && sourceId == source.PlaybackId && targetId == target.PlaybackId,
                "Out-of-range rejection started a sequence or changed participant identity.");
        }

        sealed class RangeLogCapture : ILogHandler
        {
            readonly ILogHandler previous;
            readonly string prefix;
            public int ExpectedErrors { get; private set; }
            public RangeLogCapture(ILogHandler previous, string prefix)
            {
                this.previous = previous;
                this.prefix = prefix;
            }
            public void LogFormat(LogType type, UnityEngine.Object context, string format, params object[] args)
            {
                string message = string.Format(format, args);
                if (type == LogType.Error && context == source && message.StartsWith(prefix, StringComparison.Ordinal))
                    ExpectedErrors++;
                else
                    previous.LogFormat(type, context, format, args);
            }
            public void LogException(Exception exception, UnityEngine.Object context)
            {
                previous.LogException(exception, context);
            }
        }

        static void SequenceBegan(CharacterCombat actor, CombatTripletData data, bool lethal)
        {
            if (!running || !activeCase)
                return;
            starts++;
            if (actor != source || data != move || lethal != Lethal)
            {
                failure = "Unexpected sequence entered the validation queue.";
                return;
            }
            pair = source.SourcePlayback;
            sourceId = source.PlaybackId;
            targetId = target.PlaybackId;
        }

        static void Contact(BattleVfxPlayer.Impact impact)
        {
            if (!running || !activeCase)
                return;
            contacts++;
            if (impact.playback != pair || impact.move != move ||
                impact.attacker != source || impact.receiver != target)
                failure = "Contact belongs to a different source exchange.";
            else if (!contactIds.Add(impact.eventId) || !observedCues.Add(impact.cue))
                failure = "Duplicate contact identity or authored cue.";
            else if (!expectedCues.Contains(impact.cue) ||
                Mathf.Abs(impact.seconds - pair.SampleTime) > .0001f)
                failure = "Contact cue or displayed source sample time differs from its authored timeline.";
            else if (Throw && (impact.kind != BattleVfxPlayer.ContactKind.Ground || !impact.cue.damageOnLanding))
                failure = "Throw emitted an unsupported contact instead of an authored damaging landing.";
            ObservePresentation();
        }

        static void ObservePresentation()
        {
            sawAudio |= game.battleSfx.ActiveVoiceCount > 0 && game.battleSfx.PlayedCueCount > audioBefore;
            sawEffects |= game.battleVfx.ActiveEffectCount > 0 && game.battleVfx.PlayedEffectCount > effectsBefore;
            sawHold |= feedback.IsHolding && feedback.HitStopCount > holdsBefore;
            sawLights |= lighting.ActiveFlashCount > 0 && lighting.PlayedFlashCount > lightsBefore;
            sawShake |= shake.IsShaking && shake.ShakeCount > shakesBefore;
        }
    }
}
