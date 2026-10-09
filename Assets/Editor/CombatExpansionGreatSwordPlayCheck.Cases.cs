using System;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordPlayCheck
    {
        static GameObject[] ownedObjects;
        static TrumpWeaponManager.WeaponType[] caseWeapons;
        static bool[] caseLocks, caseAutoEquip;
        static int Direction => Orientation >= 2 ? -1 : 1;

        static void StartCase()
        {
            Require(fighters.All(f => f.IsIdleAndSettled), "Next case requires two settled idle fighters.");
            source = fighters[Orientation % 2];
            target = fighters[1 - Orientation % 2];
            sourceSide = Orientation % 2 == 0 ? PlayerUI.Side.Left : PlayerUI.Side.Right;
            action = Guard ? Actions[samuraiSuite ? 1 : step % 4 + 1]
                : Queued ? Actions[(step - GuardCases) % Actions.Length]
                : samuraiSuite ? Actions[1] : Lethal ? Actions[2] : Actions[4];
            move = game.FindCombatAction(sourceSide, action);
            Require(move != null && move.IsValid && move.sourcePair != null && move.sourcePair.Valid,
                "Missing valid registered source-pair action: " + action);
            float midpoint = (positions[0].x + positions[1].x) * .5f;
            float separation = Mathf.Abs(positions[0].x - positions[1].x);
            var a = positions[Array.IndexOf(fighters, source)];
            var b = positions[Array.IndexOf(fighters, target)];
            a.x = midpoint - Direction * separation * .5f;
            b.x = midpoint + Direction * separation * .5f;
            source.Animator.transform.SetPositionAndRotation(a, Quaternion.LookRotation(Vector3.right * Direction));
            target.Animator.transform.SetPositionAndRotation(b, Quaternion.LookRotation(Vector3.left * Direction));
            ValidateProfile();
            BeginSamuraiCase();
            pair = null;
            ownedObjects = null;
            starts = contacts = sourceEnded = targetEnded = recoveryFrames = 0;
            targetRecovered = interrupted = false;
            targetRecoveryProgress = previousSample = 0;
            sawAudio = sawEffects = sawHold = sawLights = sawShake = sawFlash = sawTrail = sawSourceSword = false;
            observedCues.Clear();
            audioBefore = game.battleSfx.PlayedCueCount;
            effectsBefore = game.battleVfx.PlayedEffectCount;
            holdsBefore = feedback.HitStopCount;
            lightsBefore = lighting.PlayedFlashCount;
            shakesBefore = shake.ShakeCount;
            trailsBefore = game.battleVfx.weaponTrails.SampledPoseCount;
            caseWeapons = equipment.Select(manager => manager.ActiveWeapon).ToArray();
            caseLocks = equipment.Select(manager => manager.CombatWeaponLocked).ToArray();
            caseAutoEquip = equipment.Select(manager => manager.autoEquipWithAnimation).ToArray();
            failure = null;
            settledAt = 0;
            caseBegan = Now;
            activeCase = true;
            report.AppendLine($"START case={step + 1} scenario={Scenario} action={action} " +
                $"source={source.name} avatar={source.Animator.avatar.name} direction={Direction}");
            Write();
            if (Guard)
                CheckRangeGuard();
            else if (QueuePlayback)
                Require(game.EnqueueCombatAction(sourceSide, action), "Ordinary gameplay queue rejected action.");
            else
            {
                PositionDirectPair(move.attackRange * .98f);
                Require(source.ExecuteAttack(move, target, Lethal), "Direct accepted action was rejected.");
                Require(pair && pair.Playing && starts == 1, "Direct action failed to publish a sequence start.");
            }
        }

        static void PositionDirectPair(float distance)
        {
            var a = source.Animator.transform.position;
            var b = target.Animator.transform.position;
            float midpoint = (a.x + b.x) * .5f;
            a.x = midpoint - Direction * distance * .5f;
            b.x = midpoint + Direction * distance * .5f;
            source.Animator.transform.SetPositionAndRotation(a, Quaternion.LookRotation(Vector3.right * Direction));
            target.Animator.transform.SetPositionAndRotation(b, Quaternion.LookRotation(Vector3.left * Direction));
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
            Require(!accepted && capture.ExpectedErrors == 1, "Range guard must reject with exactly its expected error.");
            Require(starts == 0 && contacts == 0 && sourceId == source.PlaybackId && targetId == target.PlaybackId,
                "Range rejection started a sequence or changed participant identity.");
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
            if (!running)
                return;
            if (!activeCase)
            {
                failure = "Late sequence after case completion.";
                return;
            }
            starts++;
            if (actor != source || data != move || lethal != Lethal || Guard || starts != 1)
            {
                failure = "Unexpected sequence entered the validation queue.";
                return;
            }
            pair = source.SourcePlayback;
            sourceId = source.PlaybackId;
            targetId = target.PlaybackId;
            ownedObjects = new[] { pair.AttackerActor, pair.ReceiverActor }
                .Where(actorObject => actorObject)
                .SelectMany(actorObject => actorObject.GetComponentsInChildren<Transform>(true))
                .Select(item => item.gameObject).Distinct().ToArray();
        }

        static void SequenceEnded(CharacterCombat fighter, int playbackId, bool succeeded)
        {
            if (!running)
                return;
            try
            {
                Require(activeCase && !Guard && (fighter == source || fighter == target),
                    "Unexpected or late participant completion.");
                if (fighter == source)
                {
                    sourceEnded++;
                    Require(playbackId == sourceId, "Attacker completion identity mismatch.");
                }
                else
                {
                    targetEnded++;
                    Require(playbackId == targetId, "Receiver completion identity mismatch.");
                    ObserveGetUp();
                }
                Require(sourceEnded <= 1 && targetEnded <= 1, "Duplicate participant completion.");
                Require(pair && !pair.Playing && !source.IsBusy && !target.IsBusy,
                    "Completion callback preceded shared ownership release.");
                Require(succeeded == (!Interrupt && !LifecycleCancelled),
                    "Completion callback reported the wrong outcome.");
            }
            catch (Exception error)
            {
                failure = error.Message;
            }
        }
    }
}
