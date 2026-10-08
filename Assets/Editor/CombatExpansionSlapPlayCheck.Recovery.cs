using System;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapPlayCheck
    {
        static readonly HumanBodyBones[] Landmarks = { HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
            HumanBodyBones.Head, HumanBodyBones.Hips, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
        static Vector3[] lastSourcePose, pausedPositions;
        static Quaternion[] pausedRotations;
        static Transform[] pausedBones;
        static float previousSample, pausedSample, maxSnap;
        static float[] lastClocks, pausedClocks;
        static int pausedFrames;
        static bool staleChecked;

        static float Clock(CharacterCombat fighter) =>
            fighter.Animator.GetCurrentAnimatorStateInfo(0).normalizedTime * fighter.idleAnim.length;

        static Vector3[] Pose() => fighters.SelectMany(f => Landmarks.Select(b =>
            f.Animator.GetBoneTransform(b).position)).ToArray();

        static float PoseDelta(Vector3[] before, Vector3[] after) =>
            before.Select((value, i) => Vector3.Distance(value, after[i])).Max();

        static void ObserveRecovery()
        {
            Require(pair.Move == move, "Shared playback changed action.");
            Require(pair.SampleTime + .00001f >= previousSample, "Source clock moved backwards.");
            previousSample = pair.SampleTime;
            if (!pair.Playing)
                return;
            Require(source.SourcePlayback == pair && target.SourcePlayback == pair &&
                source.PlaybackId == sourceId && target.PlaybackId == targetId && source.IsBusy && target.IsBusy &&
                sourceEnded == 0 && targetEnded == 0 && (!Queued || game.IsEventQueueBusy),
                "Queue, identity or busy ownership released before shared completion.");
            foreach (var fighter in fighters)
                foreach (var bone in fighter.Animator.GetComponentsInChildren<Transform>(true))
                {
                    var point = bone.position;
                    Require(float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z),
                        "Nonfinite live pose: " + fighter.name + "/" + bone.name);
                }
            CheckUnarmed();
            if (!pair.IsRecovering)
            {
                lastSourcePose = Pose();
                return;
            }
            foreach (var fighter in fighters)
                Require(fighter.Animator.isActiveAndEnabled &&
                    fighter.Animator.GetCurrentAnimatorStateInfo(0).IsName("Base Layer.Idle"),
                    "Standing survivor did not recover through Base Layer.Idle.");
            if (!recovered)
            {
                Require(lastSourcePose != null, "No last visible source pose sampled.");
                var first = Pose();
                maxSnap = PoseDelta(lastSourcePose, first);
                for (int i = 0; i < first.Length; i++)
                {
                    float delta = Vector3.Distance(lastSourcePose[i], first[i]);
                    Require(delta <= .002f, $"Recovery entry snap {delta:F6}m: " +
                        $"fighter={fighters[i / Landmarks.Length].name} bone={Landmarks[i % Landmarks.Length]} " +
                        $"source={lastSourcePose[i]:F6} recovery={first[i]:F6}");
                }
                recovered = true;
            }
            if (oldPair && !staleChecked)
            {
                var before = Pose();
                Require(!oldPair.NotifySourceRecoveryEnded(source, oldSourceId, true) &&
                    !oldPair.NotifySourceRecoveryEnded(target, oldTargetId, true) &&
                    pair.IsRecovering && source.IsBusy && target.IsBusy &&
                    PoseDelta(before, Pose()) < .00001f,
                    "Stale callback released new recovery ownership or restored a stale pose.");
                staleChecked = true;
            }
            recoveryFrames++;
            for (int i = 0; i < fighters.Length; i++)
            {
                float current = Clock(fighters[i]);
                Require(current + .00001f >= lastClocks[i], "Standing controller clock moved backwards.");
                lastClocks[i] = current;
            }
        }

        static void ApplyScenario()
        {
            if (Mode == Scenario.Pause && acted && !resumed)
            {
                Require(pair.IsRecovering && Time.timeScale == 0, "Pause released standing ownership.");
                Require(Mathf.Abs(pair.SampleTime - pausedSample) < .00001f, "Paused source clock advanced.");
                for (int i = 0; i < fighters.Length; i++)
                    Require(Mathf.Abs(Clock(fighters[i]) - pausedClocks[i]) < .00001f,
                        "Paused standing controller clock advanced.");
                for (int i = 0; i < pausedBones.Length; i++)
                    Require(Vector3.Distance(pausedBones[i].position, pausedPositions[i]) < .0001f &&
                        Quaternion.Angle(pausedBones[i].rotation, pausedRotations[i]) < .02f,
                        "Paused corrected pose changed: " + pausedBones[i].name);
                pausedFrames++;
                if (Now - pausedAt >= .25 && pausedFrames >= 2)
                {
                    Time.timeScale = priorTimeScale;
                    resumed = true;
                }
                return;
            }
            if (acted || !pair.Playing)
                return;
            if (Mode == Scenario.Interrupt && contacts == 1)
            {
                // Allow the existing .04s light hold and live flash to be observed before cancelling source motion.
                if (pair.SampleTime < expectedCue.seconds + .15f)
                    return;
                acted = cancelled = true;
                pair.Cancel();
            }
            else if (pair.IsRecovering && recovered)
            {
                if (Mode == Scenario.Pause)
                {
                    Require(lastClocks.All(time => time < .3f), "Missed standing recovery pause window.");
                    pausedBones = fighters.SelectMany(f =>
                        f.Animator.GetComponentsInChildren<Transform>(true)).ToArray();
                    pausedPositions = pausedBones.Select(bone => bone.position).ToArray();
                    pausedRotations = pausedBones.Select(bone => bone.rotation).ToArray();
                    pausedSample = pair.SampleTime;
                    pausedClocks = fighters.Select(Clock).ToArray();
                    pausedAt = Now;
                    acted = true;
                    Time.timeScale = 0;
                }
                else if (Mode == Scenario.Reset || Mode == Scenario.Disable)
                {
                    Require(lastClocks.All(time => time < .3f), "Missed recovery cancellation window.");
                    acted = cancelled = true;
                    if (Mode == Scenario.Reset)
                        game.ResetCombatQueue();
                    else
                        target.enabled = false;
                }
            }
        }

        static void CheckUnarmed()
        {
            foreach (var manager in equipment)
            {
                Require(manager.IsUnarmedPresentation && manager.ActiveWeapon == TrumpWeaponManager.WeaponType.None,
                    "Unarmed presentation did not suppress base equipment.");
                foreach (var root in WeaponObjects(manager).Where(item => item))
                    Require(!root.activeInHierarchy &&
                        !root.GetComponentsInChildren<Collider>(true).Any(c =>
                            c.enabled && c.gameObject.activeInHierarchy) &&
                        !root.GetComponentsInChildren<Collider2D>(true).Any(c =>
                            c.enabled && c.gameObject.activeInHierarchy) &&
                        !root.GetComponentsInChildren<TrailRenderer>(true).Any(t =>
                            t.emitting && t.enabled && t.gameObject.activeInHierarchy),
                        "Suppressed equipment retained visible props, colliders or trails: " + root.name);
            }
            Require(game.battleVfx.weaponTrails.ActiveTrailCount == 0, "Unarmed slap emitted a weapon trail.");
            if (pair.IsRecovering)
                return;
            foreach (var actor in new[] { pair.AttackerActor, pair.ReceiverActor })
                Require(actor && actor.Pose != null && (actor.Pose.weaponRenderers == null ||
                    !actor.Pose.weaponRenderers.Any(r => r && r.enabled && r.gameObject.activeInHierarchy)),
                    "Native slap driver retained a visible weapon.");
        }
    }
}
