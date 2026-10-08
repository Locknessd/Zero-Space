using System;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordPlayCheck
    {
        static void CheckOwnership()
        {
            bool reset = LifecycleReset && LifecycleCancelled;
            Require(pair && pair.Move == move && (reset ||
                source.PlaybackId == sourceId && target.PlaybackId == targetId),
                "Move or participant identity changed within source playback.");
            Require(pair.SampleTime + .00001f >= previousSample, "Source clock moved backwards.");
            previousSample = pair.SampleTime;
            foreach (var fighter in fighters)
            {
                Require(fighter.gameObject.scene == scenes[Array.IndexOf(fighters, fighter)], "Fighter changed scenes.");
                foreach (var bone in fighter.Animator.GetComponentsInChildren<Transform>(true))
                {
                    var q = bone.rotation;
                    Require(Finite(bone.position) && float.IsFinite(q.x) && float.IsFinite(q.y) &&
                        float.IsFinite(q.z) && float.IsFinite(q.w), "Nonfinite runtime pose: " + bone.name);
                }
            }
            if (!pair.Playing)
                return;
            Require(source.SourcePlayback == pair && target.SourcePlayback == pair && source.IsBusy && target.IsBusy,
                "Shared ownership released a participant early.");
            Require(sourceEnded == 0 && targetEnded == 0, "Participant completed before shared pair release.");
            if (QueuePlayback)
                Require(game.IsEventQueueBusy, "Ordinary queue completed while source pair remained active.");
            foreach (var manager in equipment)
            {
                Require(manager.IsUnarmedPresentation && manager.ActiveWeapon == TrumpWeaponManager.WeaponType.None,
                    "Base equipment escaped presentation suppression.");
                foreach (var root in WeaponObjects(manager).Where(item => item))
                    // Unity searches the queried root even when includeInactive is false.
                    // Preserved enabled flags on an inactive weapon cannot cause hits or emission.
                    Require(!root.activeInHierarchy &&
                        !root.GetComponentsInChildren<Collider>(true)
                            .Any(c => c.enabled && c.gameObject.activeInHierarchy) &&
                        !root.GetComponentsInChildren<Collider2D>(true)
                            .Any(c => c.enabled && c.gameObject.activeInHierarchy) &&
                        !root.GetComponentsInChildren<TrailRenderer>(true)
                            .Any(t => t.emitting && t.enabled && t.gameObject.activeInHierarchy),
                        $"Suppressed equipment: manager={manager.name} root={root.name} " +
                        $"active={root.activeInHierarchy} " +
                        "colliders=" + string.Join(",", root.GetComponentsInChildren<Collider>(false).Select(c =>
                            c.name + ":enabled=" + c.enabled + ":active=" + c.gameObject.activeInHierarchy)) + " " +
                        "trails=" + string.Join(",", root.GetComponentsInChildren<TrailRenderer>(false).Select(t =>
                            t.name + ":emitting=" + t.emitting + ":active=" + t.gameObject.activeInHierarchy)) + ".");
            }
            if (!NativeSword || pair.IsRecovering)
                return;
            Require(pair.AttackerActor && pair.ReceiverActor && pair.AttackerActor.Pose != null &&
                pair.ReceiverActor.Pose != null, "GreatSword source actor ownership is incomplete.");
            var weapons = pair.AttackerActor.Pose.weaponRenderers;
            Require(weapons != null && weapons.Any(Visible), "Attacker source sword is not visible.");
            sawSourceSword = true;
            foreach (var weapon in weapons.Where(renderer => renderer))
                Require(ownedObjects.Contains(weapon.gameObject) &&
                    !weapon.GetComponentsInChildren<Collider>(false).Any(c => c.enabled),
                    "Source sword is not owned by the sequence or retained a live collider.");
            Require(pair.ReceiverActor.Pose.weaponRenderers == null ||
                !pair.ReceiverActor.Pose.weaponRenderers.Any(Visible), "Victim source actor retained a weapon.");
        }

        static bool Visible(Renderer renderer) => renderer && renderer.enabled && renderer.gameObject.activeInHierarchy;

        static void ObserveRecovery()
        {
            if (!pair || !pair.IsRecovering)
                return;
            ObserveGetUp();
            if (NativeSword)
                Require(!source.Animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.GetUp"),
                    "GreatSword attacker incorrectly entered receiver-only GetUp.");
            if (Lethal)
                Require(target.IsDead && !targetRecovered && pair.ReceiverActor,
                    "Accepted lethal victim lost its source terminal hold or entered GetUp.");
        }

        static void ObserveGetUp()
        {
            var state = target.Animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName("Base Layer.GetUp"))
                return;
            Require(!Lethal && !Interrupt && target.Animator.isActiveAndEnabled,
                "GetUp ran for an interrupted/lethal action or without animator ownership.");
            var expected = move.sourcePair.getUp ? move.sourcePair.getUp : move.getUpAnim;
            Require(expected && target.Animator.GetCurrentAnimatorClipInfo(0).Any(clip => clip.clip == expected),
                "Receiver GetUp does not display its authored clip.");
            Require(state.normalizedTime + .0001f >= targetRecoveryProgress, "Receiver GetUp clock moved backwards.");
            targetRecovered = true;
            recoveryFrames++;
            targetRecoveryProgress = Mathf.Max(targetRecoveryProgress, state.normalizedTime);
        }

        static void CheckCompletion()
        {
            Require(failure == null, failure);
            Require(HealthSnapshot() == healthBefore, "Local test changed authoritative health.");
            Require(Time.timeScale == priorTimeScale && !feedback.IsHolding && !feedback.IsSlowing &&
                !shake.IsShaking && lighting.ActiveFlashCount == 0 && feedback.flash.Progress >= 1 &&
                game.battleVfx.weaponTrails.ActiveTrailCount == 0,
                "Presentation clock, light, hit-stop, flash, camera or weapon trails retained ownership.");
            var camera = shake.GetComponent<MortalKombatCamera>();
            Require(!camera || !camera.IsFocusingAttack, "Gameplay camera retained attack focus.");
            Require(!source.IsBusy && !target.IsBusy && source.Animator.enabled &&
                (Lethal || target.Animator.enabled), "Participant busy or animator ownership failed cleanup.");
            Require(LifecycleReset && LifecycleCancelled ||
                source.PlaybackId == sourceId && target.PlaybackId == targetId,
                "Completion changed participant identities.");
            CheckEquipmentRestored(Lethal || LifecycleDisable);
            if (Guard)
            {
                Require(starts == 0 && contacts == 0 && sourceEnded == 0 && targetEnded == 0 &&
                    game.battleSfx.PlayedCueCount == audioBefore && game.battleVfx.PlayedEffectCount == effectsBefore &&
                    feedback.HitStopCount == holdsBefore && lighting.PlayedFlashCount == lightsBefore &&
                    shake.ShakeCount == shakesBefore && fighters.All(f => f.IsIdleAndSettled),
                    "Rejected range guard emitted gameplay or presentation activity.");
                return;
            }
            Require(starts == 1 && sourceEnded == 1 && targetEnded == 1,
                "Expected one start and one completion callback for each participant.");
            Require(!pair.AttackerActor && (Lethal ? pair.ReceiverActor : !pair.ReceiverActor),
                "Source actor cleanup differs from accepted survivor/lethal outcome.");
            Require(Object.FindObjectsByType<FrankTestActor>(FindObjectsInactive.Include).Length ==
                nativeBefore + (Lethal ? 1 : 0), "Unexpected source actor leak.");
            if (!Lethal)
                Require(ownedObjects.All(item => !item), "Source actor, driver, prop or trail survived completion.");
            if (NativeSword)
                Require(sawSourceSword, "No real frame displayed the owned attacker source sword.");
            if (LifecycleCancelled)
            {
                CheckSamuraiCancellation();
                return;
            }
            if (Interrupt)
            {
                Require(interrupted && contacts == 0 && observedCues.Count == 0 && !targetRecovered &&
                    !source.LastSequenceSucceeded && !target.LastSequenceSucceeded &&
                    fighters.All(f => f.IsIdleAndSettled), "Source interruption did not cancel cleanly before contact.");
                return;
            }
            Require(contacts == expectedCues.Length && observedCues.SetEquals(expectedCues),
                "Contact count or authored cue identities differ from the installed profile.");
            Require(sawAudio && sawEffects && sawHold && sawLights && sawShake && sawFlash &&
                (!expectSurface || sawSurface) && (move.weapon == TrumpWeaponManager.WeaponType.None || sawTrail),
                $"Missing live feedback: audio={sawAudio} vfx={sawEffects} hold={sawHold} light={sawLights} " +
                $"shake={sawShake} flash={sawFlash} surface={sawSurface}/{expectSurface} trails={sawTrail}.");
            Require(source.LastSequenceSucceeded && target.LastSequenceSucceeded && source.IsIdleAndSettled,
                "Accepted sequence failed or source did not return to idle.");
            if (Lethal)
                Require(target.IsDead && !targetRecovered && target.SourcePlayback == pair,
                    "Accepted lethal victim lost owned terminal hold or recovered.");
            else if (NativeSword)
                Require(targetRecovered && recoveryFrames > 1 && targetRecoveryProgress >= .9f && target.IsIdleAndSettled,
                    "Survivor GetUp did not visibly progress to completion and return to idle.");
            else
                Require(target.IsIdleAndSettled, "Legacy action did not return its victim to idle.");
        }

        static void CheckEquipmentRestored(bool lethal)
        {
            for (int i = 0; i < equipment.Length; i++)
            {
                var manager = equipment[i];
                bool held = lethal && manager.transform.IsChildOf(target.transform);
                Require(manager.IsUnarmedPresentation == held && manager.ActiveWeapon ==
                    (held ? TrumpWeaponManager.WeaponType.None : RequestedWeapon(manager)) &&
                    manager.CombatWeaponLocked == caseLocks[i] && manager.autoEquipWithAnimation == caseAutoEquip[i],
                    "Equipment did not restore the latest request or preserve its prior lock and automatic mode.");
                if (fixtures.Contains(manager.gameObject))
                    Require(held || manager.ActiveWeapon == caseWeapons[i], "Fixture equipment request changed.");
            }
        }

        static void CheckReleasedObjects()
        {
            Require(ownedObjects == null || ownedObjects.All(item => !item),
                "Previous action or reset retained an owned source actor, driver or weapon.");
            Require(Object.FindObjectsByType<FrankTestActor>(FindObjectsInactive.Include).Length == nativeBefore,
                "Previous action or reset leaked a native source actor.");
        }
    }
}
