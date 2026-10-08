using System;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordRecoveryPlayCheck
    {
        static Transform[] pausedBones;
        static Vector3[] pausedPositions;
        static Quaternion[] pausedRotations;
        static float pausedSample, pausedProgress;
        static GameObject[] ownedObjects;
        static int pausedFrames;

        static void StartCase()
        {
            ResetCase();
            Require(fighters.All(f => f.IsIdleAndSettled), "Both fighters must return to idle between cases.");
            source = fighters[Orientation % 2];
            target = fighters[1 - Orientation % 2];
            move = CombatExpansionGreatSwordStudy.MakeMove(source, target, MoveIndex);
            Require(move.IsValid && move.sourcePair.Valid && move.sourcePair.getUp && move.grounding &&
                move.sourcePair.recoveryGrounding && move.sourcePair.recoveryBlendSeconds == .12f,
                "Requires the authored source move, recovery clip, grounding and .12s blend.");
            float direction = Orientation >= 2 ? -1 : 1;
            float midpoint = (positions[0].x + positions[1].x) * .5f;
            var a = positions[Array.IndexOf(fighters, source)];
            var b = positions[Array.IndexOf(fighters, target)];
            a.x = midpoint - direction * move.attackRange * .5f;
            b.x = midpoint + direction * move.attackRange * .5f;
            source.Animator.transform.SetPositionAndRotation(a, Quaternion.LookRotation(Vector3.right * direction));
            target.Animator.transform.SetPositionAndRotation(b, Quaternion.LookRotation(Vector3.left * direction));
            sourceId = source.PlaybackId + 1;
            targetId = target.PlaybackId + 1;
            sourceEnded = targetEnded = recoveryFrames = pausedFrames = 0;
            acted = recovered = resumed = false;
            previousSample = previousProgress = progress = 0;
            settledAt = pausedAt = 0;
            failure = null;
            caseBegan = Now;
            activeCase = true;
            report.AppendLine($"START case={step + 1} scenario={Mode} move={move.moveName} " +
                $"source={source.name} direction={direction}");
            Write();
            Require(source.ExecuteAttack(move, target, Mode == Scenario.Lethal), "Gameplay attack rejected.");
            pair = source.SourcePlayback;
            Require(pair && pair.Playing && pair.Move == move && target.SourcePlayback == pair &&
                pair.AttackerActor && pair.ReceiverActor, "Shared source playback did not acquire both participants.");
            // Include the actors, instantiated drivers and their native weapon children, including inactive props.
            ownedObjects = new[] { pair.AttackerActor, pair.ReceiverActor }
                .SelectMany(actor => actor.GetComponentsInChildren<Transform>(true))
                .Select(item => item.gameObject).Distinct().ToArray();
            Require(source.PlaybackId == sourceId && target.PlaybackId == targetId,
                "Gameplay entry did not issue exactly one identity per fighter.");
        }

        static void Observe()
        {
            Require(pair && pair.Move == move, "Source pair disappeared or changed move.");
            Require(pair.SampleTime + .00001f >= previousSample, "Source clock moved backwards.");
            previousSample = pair.SampleTime;
            if (pair.Playing)
            {
                Require(source.PlaybackId == sourceId && target.PlaybackId == targetId &&
                    source.SourcePlayback == pair && target.SourcePlayback == pair && source.IsBusy && target.IsBusy,
                    "Shared ownership or participant identity released early.");
                Require(sourceEnded == 0 && targetEnded == 0, "Callback published while source pair still plays.");
                foreach (var manager in equipment)
                    Require(manager.IsUnarmedPresentation &&
                        manager.ActiveWeapon == TrumpWeaponManager.WeaponType.None &&
                        !manager.dualDaggersSet.activeSelf &&
                        !manager.GetComponentsInChildren<Collider>(false).Any(c => c.enabled),
                        "Base equipment escaped source presentation suppression.");
            }
            foreach (var fighter in fighters)
                foreach (var bone in fighter.Animator.GetComponentsInChildren<Transform>(true))
                {
                    var p = bone.position;
                    var q = bone.rotation;
                    Require(float.IsFinite(p.x) && float.IsFinite(p.y) && float.IsFinite(p.z) &&
                        float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w),
                        "Nonfinite pose on " + fighter.name + "/" + bone.name);
                }
            if (!pair.IsRecovering)
                return;
            var state = target.Animator.GetCurrentAnimatorStateInfo(0);
            if (!state.IsName("Base Layer.GetUp"))
                return;
            Require(Mode != Scenario.Lethal && target.Animator.isActiveAndEnabled,
                "Lethal victim recovered or surviving receiver failed controller ownership.");
            Require(target.Animator.GetCurrentAnimatorClipInfo(0).Any(c => c.clip == move.sourcePair.getUp),
                "GetUp displays the wrong authored clip.");
            Require(state.normalizedTime + .0001f >= previousProgress, "GetUp clock moved backwards.");
            previousProgress = state.normalizedTime;
            progress = Mathf.Max(progress, state.normalizedTime);
            recovered = true;
            recoveryFrames++;
        }

        static void ApplyScenario()
        {
            if (Mode == Scenario.Pause && acted && !resumed)
            {
                Require(pair.IsRecovering && Time.timeScale == 0, "Pause released recovery or its clock.");
                Require(Mathf.Abs(pair.SampleTime - pausedSample) < .00001f &&
                    Mathf.Abs(previousProgress - pausedProgress) < .00001f, "Clock advanced while paused.");
                for (int i = 0; i < pausedBones.Length; i++)
                    Require(pausedBones[i] && Vector3.Distance(pausedBones[i].position, pausedPositions[i]) < .0001f &&
                        Quaternion.Angle(pausedBones[i].rotation, pausedRotations[i]) < .02f,
                        "Corrected bone pose changed while paused: " + pausedBones[i]?.name);
                pausedFrames++;
                if (Now - pausedAt >= .2 && pausedFrames >= 2)
                {
                    Time.timeScale = priorTimeScale;
                    resumed = true;
                }
                return;
            }
            if (acted || !pair.IsRecovering || !recovered)
                return;
            if (Mode == Scenario.Pause)
            {
                Require(progress * move.sourcePair.getUp.length < .12f,
                    "Real frames missed the first .12s recovery blend; pause coverage cannot be claimed.");
                pausedBones = fighters.SelectMany(f =>
                    f.Animator.GetComponentsInChildren<Transform>(true)).ToArray();
                pausedPositions = pausedBones.Select(bone => bone.position).ToArray();
                pausedRotations = pausedBones.Select(bone => bone.rotation).ToArray();
                pausedSample = pair.SampleTime;
                pausedProgress = previousProgress;
                pausedAt = Now;
                acted = true;
                Time.timeScale = 0;
            }
            else if (Mode == Scenario.Cancel || Mode == Scenario.Reset || Mode == Scenario.Disable)
            {
                if (progress < .1f)
                    return;
                Require(progress < .85f, "Real frames missed mid-recovery interruption window.");
                acted = true;
                if (Mode == Scenario.Cancel)
                    pair.Cancel();
                else if (Mode == Scenario.Reset)
                    game.ResetCombatQueue();
                else
                    target.enabled = false;
            }
        }

        static void SequenceEnded(CharacterCombat fighter, int playbackId, bool succeeded)
        {
            if (!running || !activeCase)
                return;
            try
            {
                Require(fighter == source || fighter == target, "Unexpected participant callback.");
                if (fighter == source)
                {
                    sourceEnded++;
                    Require(playbackId == sourceId, "Attacker callback identity mismatch.");
                }
                else
                {
                    targetEnded++;
                    Require(playbackId == targetId, "Receiver callback identity mismatch.");
                }
                Require(sourceEnded <= 1 && targetEnded <= 1, "Duplicate participant callback.");
                Require(succeeded == Success, "Wrong participant completion outcome.");
                Require(pair && !pair.Playing && !source.IsBusy && !target.IsBusy,
                    "Completion callback preceded shared release.");
            }
            catch (Exception error)
            {
                failure = error.Message;
            }
        }

        static void CheckCompletion()
        {
            Require(failure == null, failure);
            Require(sourceEnded == 1 && targetEnded == 1, "Expected one completion callback per participant.");
            Require(!pair.AttackerActor && (Mode == Scenario.Lethal ? pair.ReceiverActor : !pair.ReceiverActor),
                "Source actor ownership differs from accepted survivor/terminal outcome.");
            Require(!source.IsBusy && !target.IsBusy && Time.timeScale == priorTimeScale,
                "Busy state or presentation clock failed to release.");
            if (Mode != Scenario.Reset)
                Require(source.PlaybackId == sourceId && target.PlaybackId == targetId &&
                    source.LastSequenceSucceeded == Success && target.LastSequenceSucceeded == Success,
                    "Sequence identity or final outcome changed.");
            else
                Require(source.PlaybackId > sourceId && target.PlaybackId > targetId,
                    "Round reset failed to invalidate sequence identities.");
            if (Mode == Scenario.Lethal)
                Require(target.IsDead && !recovered && target.SourcePlayback == pair && source.IsIdleAndSettled,
                    "Accepted lethal receiver lost owned terminal hold or entered recovery.");
            else
            {
                Require(recovered && recoveryFrames > 1, "Recovery was not observed over multiple real frames.");
                Require(ownedObjects.All(item => !item), "Source actors, drivers or props survived cleanup.");
                if (Success)
                    Require(progress >= .9f && fighters.All(f => f.IsIdleAndSettled),
                        "GetUp did not progress to completion and return control.");
                else
                    Require(acted && fighters.All(f => f.IsIdleAndSettled),
                        "Interruption was not exercised or failed to settle controller state.");
            }
            if (Mode == Scenario.Pause)
                Require(resumed && pausedFrames >= 2, "Pause/resume real-frame coverage missing.");
            for (int i = 0; i < equipment.Length; i++)
            {
                bool held = fighters[i] == target && (Mode == Scenario.Lethal || Mode == Scenario.Disable);
                Require(equipment[i].IsUnarmedPresentation == held && equipment[i].ActiveWeapon ==
                    (held ? TrumpWeaponManager.WeaponType.None : TrumpWeaponManager.WeaponType.DualDaggers),
                    "Equipment restored incorrectly or lost an intentional terminal/inactive hold.");
            }
        }
    }
}
