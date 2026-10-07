using System;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionThrowPlayCheck
    {
        static void CheckOwnership()
        {
            Require(source.PlaybackId == sourceId && target.PlaybackId == targetId,
                "Participant identity changed within a source sequence or recovery.");
            foreach (var fighter in fighters)
            {
                Require(fighter.gameObject.scene == scenes[Array.IndexOf(fighters, fighter)],
                    "A fighter changed scenes.");
                foreach (var bone in fighter.Animator.GetComponentsInChildren<Transform>(true))
                {
                    var p = bone.position;
                    var q = bone.rotation;
                    Require(float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z) &&
                        float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w),
                        "Nonfinite runtime pose: " + bone.name);
                }
            }
            if (!pair.Playing)
                return;
            Require(source.SourcePlayback == pair && target.SourcePlayback == pair && source.IsBusy && target.IsBusy,
                "Shared source/recovery ownership released a participant early.");
            Require(sourceEnded == 0 && targetEnded == 0, "Participant completed before shared pair completion.");
            if (Queued)
                Require(game.IsEventQueueBusy, "Gameplay queue completed while the pair was still active.");
            foreach (var manager in equipment)
                Require(manager.IsUnarmedPresentation && !manager.dualDaggersSet.activeSelf &&
                    manager.ActiveWeapon == TrumpWeaponManager.WeaponType.None &&
                    !manager.GetComponentsInChildren<Collider>(false).Any(collider => collider.enabled),
                    "Owned equipment escaped suppression during source motion or recovery.");
            if (Throw)
                foreach (var actor in new[] { pair.AttackerActor, pair.ReceiverActor })
                    if (actor && actor.Pose != null)
                        Require(!actor.Pose.weaponRenderers.Any(renderer => renderer && renderer.enabled),
                            "An unarmed source actor retained native weapon renderers.");
        }

        static void ObserveRecovery()
        {
            if (!pair || !pair.IsRecovering)
                return;
            ObserveGetUp(source, ref sourceRecovered, ref sourceRecoveryProgress);
            ObserveGetUp(target, ref targetRecovered, ref targetRecoveryProgress);
            if (Lethal)
                Require(target.IsDead && !targetRecovered && pair.ReceiverActor,
                    "Accepted lethal receiver lost its terminal source pose or entered GetUp.");
        }

        static void ObserveGetUp(CharacterCombat fighter, ref bool entered, ref float progress)
        {
            var animator = fighter.Animator;
            var state = animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName("Base Layer.GetUp"))
                return;
            Require(animator.isActiveAndEnabled, "Recovery controller did not regain animator ownership.");
            var expected = fighter == source ? move.sourcePair.attackerGetUp : move.sourcePair.getUp;
            if (!expected)
                expected = move.getUpAnim;
            Require(expected && animator.GetCurrentAnimatorClipInfo(0).Any(clip => clip.clip == expected),
                "GetUp state does not display the authored recovery clip.");
            entered = true;
            progress = Mathf.Max(progress, state.normalizedTime);
        }

        static void SequenceEnded(CharacterCombat fighter, int playbackId, bool succeeded)
        {
            if (!running || !activeCase || Guard)
                return;
            try
            {
                if (fighter == source)
                {
                    sourceEnded++;
                    Require(playbackId == sourceId, "Attacker completed with a different playback identity.");
                    ObserveGetUp(source, ref sourceRecovered, ref sourceRecoveryProgress);
                }
                else if (fighter == target)
                {
                    targetEnded++;
                    Require(playbackId == targetId, "Receiver completed with a different playback identity.");
                    ObserveGetUp(target, ref targetRecovered, ref targetRecoveryProgress);
                }
                Require(pair && !pair.Playing && !source.IsBusy && !target.IsBusy,
                    "Completion callback ran before both participants released shared ownership.");
                Require(succeeded == !Interrupt, "Sequence completion reported the wrong recovery outcome.");
                if (!Interrupt && Throw)
                {
                    Require(Lethal || targetRecovered, "Surviving receiver completed without observed GetUp.");
                    Require(action != "Vol10_BRAIN" || sourceRecovered,
                        "Brainbuster attacker completed without observed GetUp.");
                }
            }
            catch (Exception error)
            {
                failure = error.Message;
            }
        }

        static void CheckCompletion()
        {
            Require(failure == null, failure);
            Require(HealthSnapshot() == healthBefore, "Local validation mutated authoritative health.");
            Require(Time.timeScale == priorTimeScale && !feedback.IsHolding && !feedback.IsSlowing &&
                !shake.IsShaking && lighting.ActiveFlashCount == 0,
                "Presentation clock, light, hit-stop or camera impulse did not release ownership.");
            var camera = shake.GetComponent<MortalKombatCamera>();
            Require(!camera || !camera.IsFocusingAttack, "Gameplay camera retained attack focus.");
            Require(!source.IsBusy && !target.IsBusy && source.Animator.enabled &&
                (Lethal || target.Animator.enabled), "Participant busy or animator ownership failed cleanup.");
            Require(source.PlaybackId == sourceId && target.PlaybackId == targetId,
                "Completion changed source identities.");
            if (Guard)
            {
                Require(starts == 0 && contacts == 0 && game.battleSfx.PlayedCueCount == audioBefore &&
                    game.battleVfx.PlayedEffectCount == effectsBefore,
                    "Rejected range guard emitted a sequence or presentation cue.");
                Require(equipment.All(manager => !manager.IsUnarmedPresentation),
                    "Rejected range guard acquired equipment ownership.");
                return;
            }
            Require(starts == 1 && sourceEnded == 1 && targetEnded == 1,
                "Expected exactly one sequence start and one completion per participant.");
            Require(contacts == expectedCues.Length && observedCues.SetEquals(expectedCues),
                "Source contact count or unique authored cues differ from the expected timeline.");
            Require(sawAudio && sawEffects && sawHold && sawLights && sawShake,
                $"Missing live presentation response: audio={sawAudio} vfx={sawEffects} " +
                $"hold={sawHold} light={sawLights} shake={sawShake}.");
            Require(!pair.AttackerActor && (Lethal ? pair.ReceiverActor : !pair.ReceiverActor),
                "Source actor cleanup did not match the accepted survivor/lethal outcome.");
            Require(Object.FindObjectsByType<FrankTestActor>().Length ==
                nativeBefore + (Lethal ? 1 : 0), "Unexpected native source actor leak.");
            for (int i = 0; i < equipment.Length; i++)
            {
                bool terminal = Lethal && fighters[i] == target;
                Require(equipment[i].IsUnarmedPresentation == terminal && equipment[i].ActiveWeapon ==
                    (terminal ? TrumpWeaponManager.WeaponType.None : TrumpWeaponManager.WeaponType.DualDaggers),
                    "Equipment suppression did not restore its request or retain an accepted terminal hold.");
            }
            if (Interrupt)
            {
                Require(interrupted && sourceRecovered && targetRecovered &&
                    !source.LastSequenceSucceeded && !target.LastSequenceSucceeded &&
                    fighters.All(fighter => fighter.IsIdleAndSettled),
                    "Recovery cancellation was not exercised with both GetUp controllers active and restored.");
                return;
            }
            Require(source.LastSequenceSucceeded && target.LastSequenceSucceeded,
                "Paired sequence did not complete successfully.");
            if (Throw)
            {
                Require(Lethal || targetRecovered && targetRecoveryProgress >= .9f && target.IsIdleAndSettled,
                    "Receiver GetUp did not visibly advance to completion and return to idle.");
                if (action == "Vol10_BRAIN")
                    Require(sourceRecovered && sourceRecoveryProgress >= .9f && source.IsIdleAndSettled,
                        "Brainbuster attacker GetUp did not visibly complete and return to idle.");
            }
            Require(!Lethal || target.IsDead && !targetRecovered,
                "Accepted lethal victim incorrectly recovered or lost death state.");
        }
    }
}
