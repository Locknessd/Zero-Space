using System;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordPlayCheck
    {
        static bool Lifecycle => samuraiSuite && step >= BasicCases;
        static int LifecycleKind => (step - BasicCases) / 4;
        static bool LifecycleReset => Lifecycle && LifecycleKind == 0;
        static bool LifecycleDisable => Lifecycle && LifecycleKind == 1;
        static bool LifecyclePause => Lifecycle && LifecycleKind == 2;
        static bool LifecycleRepeat => Lifecycle && LifecycleKind == 3;
        static bool LifecycleCancelled => Lifecycle && interrupted;
        static bool QueuePlayback => Queued || LifecycleReset || LifecyclePause || LifecycleRepeat;
        static string LifecycleScenario => LifecycleReset ? "reset after contact" :
            LifecycleDisable ? "receiver disable after contact" : LifecyclePause ? "prone pause/resume" :
            "repeated queued activation";
        static int samuraiRepeat;
        static bool equipmentChanged, staleSourceChecked, staleRecoveryChecked;
        static FrankBattlePairPlayback stalePair;
        static CharacterCombat staleSource, staleTarget;
        static int staleSourceId, staleTargetId;
        static bool[] combatEnabled;
        static TrumpWeaponManager.WeaponType[] latestRequests;

        static void ResetSamuraiSuite()
        {
            samuraiRepeat = 0;
            stalePair = null;
            combatEnabled = null;
            pauseActive = false;
        }

        static void BeginSamuraiCase()
        {
            if (!samuraiSuite)
                return;
            if (combatEnabled == null)
                combatEnabled = fighters.Select(fighter => fighter.enabled).ToArray();
            if (pair)
            {
                stalePair = pair;
                // The component can be reused by the next playback; keep old participant IDs separately.
                staleSource = previousSamuraiSource;
                staleTarget = previousSamuraiTarget;
                staleSourceId = sourceId;
                staleTargetId = targetId;
            }
            previousSamuraiSource = source;
            previousSamuraiTarget = target;
            equipmentChanged = staleSourceChecked = staleRecoveryChecked = false;
            latestRequests = null;
            ResetSamuraiCleanup();
            ResetSamuraiPause();
            ResetSamuraiEvidence();
            BeginSamuraiMeasurements();
        }

        static CharacterCombat previousSamuraiSource, previousSamuraiTarget;

        static void ApplySamuraiLifecycle()
        {
            if (!Samurai)
                return;
            if (LifecycleDisable && interrupted)
                Require(!target.enabled && !target.IsBusy && !target.IsDead && !pair.Playing &&
                    !target.Animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.GetUp"),
                    "Disabled receiver reacquired playback or entered recovery before explicit reset.");
            if (pair.Playing && !pair.IsRecovering && !staleSourceChecked)
                CheckStaleSamuraiOwner(false);
            if (pair.IsRecovering && !staleRecoveryChecked)
                CheckStaleSamuraiOwner(true);
            if (!equipmentChanged && pair.Playing && contacts > 0)
                ChangeSuppressedEquipment();
            if (LifecyclePause)
                ApplySamuraiPause();
            if ((!LifecycleReset && !LifecycleDisable) || interrupted || !pair.Playing || contacts == 0)
                return;
            Require(contacts == 1 && !pair.IsRecovering && pair.SampleTime < expectedCues[1].seconds,
                "Missed source-motion reset/disable window between the first and second contacts.");
            // Observe the actual first-contact hold/flash before injecting the lifecycle change.
            if (pair.SampleTime < expectedCues[0].seconds + .15f)
                return;
            interrupted = true;
            if (LifecycleReset)
                game.ResetCombatQueue();
            else
                target.enabled = false;
            Require(!pair.Playing && !source.IsBusy && !target.IsBusy,
                "Reset/disable did not synchronously release the accepted source playback.");
        }

        static void ChangeSuppressedEquipment()
        {
            latestRequests = new TrumpWeaponManager.WeaponType[equipment.Length];
            for (int i = 0; i < equipment.Length; i++)
            {
                var manager = equipment[i];
                var latest = RequestedWeapon(manager) == TrumpWeaponManager.WeaponType.Katana
                    ? TrumpWeaponManager.WeaponType.DualDaggers : TrumpWeaponManager.WeaponType.Katana;
                manager.EquipWeapon(TrumpWeaponManager.WeaponType.None);
                manager.EquipWeapon(latest);
                latestRequests[i] = latest;
                caseWeapons[i] = latest;
                Require(RequestedWeapon(manager) == latest && manager.IsUnarmedPresentation &&
                    manager.ActiveWeapon == TrumpWeaponManager.WeaponType.None,
                    "Latest requested equipment escaped suppression or was discarded.");
            }
            equipmentChanged = true;
            CheckOwnership();
            report.AppendLine("EQUIPMENT two requests while suppressed: " + string.Join(",", latestRequests));
        }

        static void CheckStaleSamuraiOwner(bool recovering)
        {
            if (!stalePair || !staleSource || !staleTarget)
                return;
            var before = SamuraiPose();
            Require(!stalePair.NotifySourceRecoveryEnded(staleSource, staleSourceId, true) &&
                !stalePair.NotifySourceRecoveryEnded(staleTarget, staleTargetId, true) &&
                !stalePair.NotifySourceRecoveryEnded(staleTarget, staleTargetId, false),
                "Stale successful/failed recovery callback was accepted under a newer owner.");
            Require(pair.Playing && pair.IsRecovering == recovering && source.SourcePlayback == pair &&
                target.SourcePlayback == pair && source.PlaybackId == sourceId && target.PlaybackId == targetId &&
                source.IsBusy && target.IsBusy && sourceEnded == 0 && targetEnded == 0 &&
                SamuraiPoseDelta(before, SamuraiPose()) < .00001f,
                "Stale callback changed a newer pose, identity, phase or busy ownership.");
            if (recovering)
                staleRecoveryChecked = true;
            else
                staleSourceChecked = true;
            report.AppendLine($"STALE rejected phase={(recovering ? "recovery" : "source")} " +
                $"old={staleSourceId}/{staleTargetId} current={sourceId}/{targetId}");
        }

        static void CheckSamuraiCancellation()
        {
            Require(interrupted && contacts == 1 && observedCues.Count == 1 &&
                observedCues.Contains(expectedCues[0]) && !targetRecovered && source.IsIdleAndSettled &&
                target.IsIdleAndSettled && !source.IsDead && !target.IsDead,
                "Post-contact interruption must retain exactly its first confirmed contact and never recover.");
            Require(sawAudio && sawEffects && sawHold && sawLights && sawShake && sawFlash && sawTrail &&
                (!expectSurface || sawSurface),
                "First contact did not show all confirmed presentation before interruption.");
            if (LifecycleReset)
                Require(source.PlaybackId > sourceId && target.PlaybackId > targetId,
                    "Round reset failed to invalidate both old participant identities.");
            else
                Require(!target.enabled && !source.LastSequenceSucceeded && !target.LastSequenceSucceeded,
                    "Disabled receiver resurrected or cancellation reported success.");
        }

        static void CheckSamuraiCompletion()
        {
            if (!samuraiSuite)
                return;
            Require(game.battleVfx.ActiveEffectCount == 0 && game.battleSfx.ActiveVoiceCount == 0,
                "Samurai presentation retained active effects or audio after settling.");
            if (Samurai && !Guard && !Interrupt)
            {
                Require(equipmentChanged && latestRequests != null,
                    "Latest equipment request changes were not exercised under suppression.");
                for (int i = 0; i < equipment.Length; i++)
                    Require(RequestedWeapon(equipment[i]) == latestRequests[i],
                        "Latest request was lost during source cleanup or recovery.");
            }
            if (LifecyclePause)
                Require(pauseResumed && samuraiPresentationResumed && pausedSamuraiFrames >= 2 &&
                    targetRecoveryProgress > pausedRecoveryProgress + .1f,
                    "Paused prone recovery did not stop across real frames and then resume progression.");
            if (LifecycleRepeat && samuraiRepeat == 1)
                Require(staleSourceChecked && staleRecoveryChecked,
                    "Repeated activation did not reject stale callbacks in both source and recovery phases.");
            CheckSamuraiEvidence();
            ReportSamuraiMeasurements();
            if (LifecycleDisable)
            {
                target.enabled = true;
                game.ResetCombatQueue();
                CheckEquipmentRestored(false);
                Require(fighters.All(f => !f.IsDead && !f.IsBusy && f.Animator.enabled) &&
                    HealthSnapshot() == healthBefore, "Explicit reenable/reset failed to release the disabled hold.");
            }
        }

        static bool CompleteSamuraiPlay()
        {
            if (LifecycleRepeat && samuraiRepeat == 0)
            {
                samuraiRepeat = 1;
                report.AppendLine("REPEAT first activation settled; next activation uses the ordinary gameplay queue.");
                return false;
            }
            samuraiRepeat = 0;
            return true;
        }

        static void RestoreSamuraiLifecycle()
        {
            if (!samuraiSuite)
                return;
            if (pauseActive && game && game.battleSfx)
                game.battleSfx.SetMenuPaused(false);
            if (combatEnabled != null)
                for (int i = 0; i < fighters.Length; i++)
                    if (fighters[i])
                        fighters[i].enabled = combatEnabled[i];
        }
    }
}
