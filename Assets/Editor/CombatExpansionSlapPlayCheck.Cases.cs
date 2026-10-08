using System;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapPlayCheck
    {
        static GameObject[] ownedObjects;
        static bool[] caseLocks, caseAutoEquip;
        static int oldSourceId, oldTargetId;
        static FrankBattlePairPlayback oldPair;

        static void StartCase()
        {
            Require(fighters.All(f => f.IsIdleAndSettled), "Next play requires settled idle fighters.");
            source = fighters[Orientation % 2];
            target = fighters[1 - Orientation % 2];
            sourceSide = Orientation % 2 == 0 ? PlayerUI.Side.Left : PlayerUI.Side.Right;
            action = "SlapFace_Sequence" + (step % 8 / 4 + 1);
            move = game.FindCombatAction(sourceSide, action);
            Require(move != null && move.IsValid && move.sourcePair != null && move.sourcePair.Valid,
                "Missing registered Slap action: " + action);
            Require(source.lightCombatMoves.Contains(move), "Slap is absent from lightCombatMoves.");
            PositionPair(Mathf.Max(move.attackRange + 1, Mathf.Abs(positions[0].x - positions[1].x)));
            ValidateProfile();
            oldPair = pair;
            oldSourceId = sourceId;
            oldTargetId = targetId;
            pair = null;
            ownedObjects = null;
            starts = contacts = sourceEnded = targetEnded = recoveryFrames = pausedFrames = 0;
            acted = resumed = recovered = cancelled = staleChecked = false;
            previousSample = maxSnap = 0;
            lastSourcePose = null;
            lastClocks = new float[2];
            sawAudio = sawEffects = sawHold = sawLights = sawShake = sawFlash = sawSurface = false;
            audioBefore = game.battleSfx.PlayedCueCount;
            effectsBefore = game.battleVfx.PlayedEffectCount;
            holdsBefore = feedback.HitStopCount;
            lightsBefore = lighting.PlayedFlashCount;
            shakesBefore = shake.ShakeCount;
            caseLocks = equipment.Select(item => item.CombatWeaponLocked).ToArray();
            caseAutoEquip = equipment.Select(item => item.autoEquipWithAnimation).ToArray();
            failure = null;
            settledAt = pausedAt = 0;
            caseBegan = Now;
            activeCase = true;
            report.AppendLine($"START case={step + 1} scenario={Mode} action={action} " +
                $"avatar={source.Animator.avatar.name} direction={Direction} play={repeat + 1}");
            Write();
            if (Guard)
                CheckRangeGuard();
            else if (Queued)
                Require(game.EnqueueCombatAction(sourceSide, action), "Gameplay queue rejected registered Slap.");
            else
            {
                PositionPair(move.attackRange * .98f);
                Require(source.ExecuteAttack(move, target), "Direct registered Slap recovery fixture rejected.");
            }
        }

        static void PositionPair(float distance)
        {
            float midpoint = (positions[0].x + positions[1].x) * .5f;
            var a = positions[Array.IndexOf(fighters, source)];
            var b = positions[Array.IndexOf(fighters, target)];
            a.x = midpoint - Direction * distance * .5f;
            b.x = midpoint + Direction * distance * .5f;
            source.Animator.transform.SetPositionAndRotation(a, Quaternion.LookRotation(Vector3.right * Direction));
            target.Animator.transform.SetPositionAndRotation(b, Quaternion.LookRotation(Vector3.left * Direction));
        }

        static void CheckRangeGuard()
        {
            PositionPair(move.attackRange + 1);
            sourceId = source.PlaybackId;
            targetId = target.PlaybackId;
            var handler = Debug.unityLogger.logHandler;
            var capture = new RangeLogCapture(handler);
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
            Require(!accepted && capture.ExpectedErrors == 1,
                "Range precondition must reject with exactly its expected error.");
        }

        sealed class RangeLogCapture : ILogHandler
        {
            readonly ILogHandler previous;
            public int ExpectedErrors { get; private set; }
            public RangeLogCapture(ILogHandler previous) { this.previous = previous; }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                string message = string.Format(format, args);
                string prefix = "CharacterCombat [" + source.name + "]: Mục tiêu ngoài tầm: ";
                if (type == LogType.Error && context == source && message.StartsWith(prefix, StringComparison.Ordinal))
                    ExpectedErrors++;
                else
                    previous.LogFormat(type, context, format, args);
            }
            public void LogException(Exception error, Object context) { previous.LogException(error, context); }
        }

        static void SequenceBegan(CharacterCombat actor, CombatTripletData data, bool lethal)
        {
            if (!running)
                return;
            try
            {
                starts++;
                Require(activeCase && !Guard && actor == source && data == move && !lethal && starts == 1,
                    "Unexpected sequence or lethal outcome in survivor suite.");
                pair = source.SourcePlayback;
                sourceId = source.PlaybackId;
                targetId = target.PlaybackId;
                Require(pair && target.SourcePlayback == pair, "Shared pair failed ownership acquisition.");
                ownedObjects = new[] { pair.AttackerActor, pair.ReceiverActor }
                    .Where(actorObject => actorObject)
                    .SelectMany(actorObject => actorObject.GetComponentsInChildren<Transform>(true))
                    .Select(item => item.gameObject).Distinct().ToArray();
                // Send old recovery callbacks under a newer owner; they must be rejected without pose changes.
                if (oldPair)
                {
                    var before = Pose();
                    Require(!oldPair.NotifySourceRecoveryEnded(source, oldSourceId, true) &&
                        !oldPair.NotifySourceRecoveryEnded(target, oldTargetId, true),
                        "A stale recovery callback was accepted by the new owner.");
                    Require(source.PlaybackId == sourceId && target.PlaybackId == targetId &&
                        source.IsBusy && target.IsBusy && PoseDelta(before, Pose()) < .00001f,
                        "A stale callback released newer ownership or restored an old pose.");
                }
            }
            catch (Exception error)
            {
                failure = error.Message;
            }
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
                }
                Require(sourceEnded <= 1 && targetEnded <= 1, "Duplicate participant completion.");
                Require(pair && !pair.Playing && !source.IsBusy && !target.IsBusy,
                    "Completion preceded shared ownership release.");
                Require(succeeded == Success, "Wrong completion outcome.");
                if (Success)
                    foreach (var fighterItem in fighters)
                        Require(Clock(fighterItem) >= .3f - .0001f,
                            "Busy released before the controller consumed .3s of standing recovery.");
            }
            catch (Exception error)
            {
                failure = error.Message;
            }
        }

        static void CheckCompletion()
        {
            Require(failure == null, failure);
            Require(HealthSnapshot() == healthBefore, "Local validation changed authoritative health.");
            Require(Time.timeScale == priorTimeScale && !feedback.IsHolding && !feedback.IsSlowing &&
                !shake.IsShaking && lighting.ActiveFlashCount == 0 && feedback.flash.Progress >= 1 &&
                game.battleVfx.ActiveEffectCount == 0 && game.battleSfx.ActiveVoiceCount == 0 &&
                game.battleVfx.weaponTrails.ActiveTrailCount == 0, "Presentation failed to settle.");
            Require(fighters.All(f => !f.IsBusy && f.Animator.enabled && f.IsIdleAndSettled),
                "Movement or animator ownership failed to return to idle.");
            Require(Vector3.Dot(source.Animator.transform.forward, target.Animator.transform.forward) < -.9f,
                "Final fighters no longer face opposing directions.");
            if (Mode == Scenario.Disable)
                target.enabled = true;
            for (int i = 0; i < equipment.Length; i++)
            {
                var manager = equipment[i];
                // Disabled receivers retain suppression until authoritative reset by design.
                bool held = Mode == Scenario.Disable && manager.transform.IsChildOf(target.transform);
                Require(manager.IsUnarmedPresentation == held && manager.ActiveWeapon ==
                    (held ? TrumpWeaponManager.WeaponType.None : RequestedWeapon(manager)) &&
                    manager.CombatWeaponLocked == caseLocks[i] && manager.autoEquipWithAnimation == caseAutoEquip[i],
                    "Equipment request, suppression, lock or automatic mode changed.");
            }
            if (Guard)
            {
                Require(starts == 0 && contacts == 0 && sourceEnded == 0 && targetEnded == 0 &&
                    source.PlaybackId == sourceId && target.PlaybackId == targetId &&
                    game.battleSfx.PlayedCueCount == audioBefore && game.battleVfx.PlayedEffectCount == effectsBefore &&
                    feedback.HitStopCount == holdsBefore && lighting.PlayedFlashCount == lightsBefore &&
                    shake.ShakeCount == shakesBefore, "Rejected range emitted playback or victim feedback.");
                return;
            }
            Require(starts == 1 && contacts == 1 && sourceEnded == 1 && targetEnded == 1,
                "Expected one real contact and exactly one callback per participant per play.");
            Require(!pair.AttackerActor && !pair.ReceiverActor, "Source actor references survived completion.");
            CheckReleasedObjects();
            Require(sawAudio && sawEffects && sawHold && sawLights && sawShake && sawFlash &&
                (!expectSurface || sawSurface), $"Missing real presentation audio={sawAudio} vfx={sawEffects} " +
                $"hold={sawHold} light={sawLights} shake={sawShake} flash={sawFlash} surface={sawSurface}.");
            if (Mode != Scenario.Reset)
                Require(source.PlaybackId == sourceId && target.PlaybackId == targetId,
                    "Playback identities changed before the next sequence.");
            if (Success)
                Require(recovered && recoveryFrames >= 2 &&
                    source.LastSequenceSucceeded && target.LastSequenceSucceeded,
                    "Standing recovery did not span real frames and finish successfully.");
            else
                Require(acted && cancelled, "Cancellation scenario was not exercised.");
            if (Mode == Scenario.Pause)
                Require(resumed && pausedFrames >= 2, "Recovery pause/resume not observed over real frames.");
            if (Mode == Scenario.Reset)
                Require(source.PlaybackId > sourceId && target.PlaybackId > targetId,
                    "Round reset did not invalidate the cancelled identities.");
            if (Mode == Scenario.Disable)
            {
                game.ResetCombatQueue();
                Require(equipment.All(manager => !manager.IsUnarmedPresentation),
                    "Disabled receiver retained equipment after reenable/reset.");
            }
            CheckEnvironment();
        }

        static void CheckReleasedObjects()
        {
            Require(ownedObjects == null || ownedObjects.All(item => !item), "Owned source object leaked.");
            Require(Object.FindObjectsByType<FrankTestActor>(FindObjectsInactive.Include).Length == nativeBefore,
                "Native source actor count changed.");
        }
    }
}
