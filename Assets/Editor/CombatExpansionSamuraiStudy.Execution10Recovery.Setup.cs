using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static FrankBattlePairPlayback E10Begin(CharacterCombat[] fighters, CharacterCombat source,
            CharacterCombat target, int direction, E10Case record)
        {
            foreach (var fighter in fighters)
                fighter.ResetCombat();
            source.Animator.transform.SetPositionAndRotation(Vector3.left * direction * .85f,
                Quaternion.LookRotation(Vector3.right * direction));
            target.Animator.transform.SetPositionAndRotation(Vector3.right * direction * .85f,
                Quaternion.LookRotation(Vector3.left * direction));
            var move = MakePairMove(source, target, 10);
            var getUp = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(E10GetUp.Split(':')[0]))
                .OfType<AnimationClip>().Single(c => CombatExpansionInventory.Identity(c) == E10GetUp);
            var recovery = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(E10RecoveryPath);
            var grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(GroundingAssetPath(10));
            if (!getUp || !getUp.humanMotion || !float.IsFinite(getUp.length) || getUp.length <= .001f ||
                !recovery || !grounding ||
                CombatExpansionInventory.Identity(recovery) != "0e3315c06eebc2f62b838fffb249aca3:11400000" ||
                CombatExpansionInventory.Identity(grounding) != "54888ce163d25b68b81efc494e6b1403:11400000")
                throw new InvalidOperationException("Exact Execution10 or supine recovery assets are missing.");
            move.grounding = grounding;
            move.getUpAnim = getUp;
            move.sourcePair.getUp = getUp;
            move.sourcePair.recoveryGrounding = recovery;
            move.sourcePair.recoveryBlendSeconds = .12f;
            string attack = CombatExpansionInventory.Identity(move.attackAnim);
            string reaction = CombatExpansionInventory.Identity(move.hitAnim);
            if (attack != Guids[0] + ":7400020" || reaction != Guids[1] + ":7400020" ||
                move.sourcePair.attack != move.attackAnim || move.sourcePair.reaction != move.hitAnim ||
                move.attackRange != 1.7f || move.sourcePair.receiverOffset != Vector3.forward * 1.7f ||
                Quaternion.Angle(move.sourcePair.receiverRotation, Quaternion.Euler(0, 180, 0)) > .001f ||
                move.sourcePair.spacing || move.sourcePair.attacks || move.sourcePair.reactions ||
                move.sourcePair.attackerGetUp || move.sourcePair.standingRecoverySeconds != 0)
                throw new InvalidOperationException("Original full-duration native Execution10 pair changed.");
            if (recovery.tracks == null || !recovery.tracks.Any(t => t != null && t.receiver &&
                t.avatar == target.Animator.avatar && Mathf.Abs(t.duration - getUp.length) < .001f &&
                t.lift != null && t.lift.Length > 1 && t.lift.All(v => float.IsFinite(v) && v >= 0)))
                throw new InvalidOperationException("Missing finite existing supine grounding for victim Avatar.");
            CheckGroundingTracks(grounding.tracks, fighters);
            if (!source.ExecuteAttack(move, target) || source.SourcePlayback == null)
                throw new InvalidOperationException("Execution10 supine recovery pair was rejected.");
            var pair = source.SourcePlayback;
            try
            {
                pair.AttackerActor.Pose.transferFingers = true;
                pair.ReceiverActor.Pose.transferFingers = true;
                CheckPairWeapons(pair, pair.AttackerActor.Pose.weaponRenderers);
                float expected = Mathf.Max(move.attackAnim.length, move.hitAnim.length);
                if (!float.IsFinite(move.attackAnim.length) || !float.IsFinite(move.hitAnim.length) ||
                    !float.IsFinite(expected) || expected <= 0 || !float.IsFinite(pair.Duration) ||
                    Mathf.Abs(pair.Duration - expected) > .000001f || pair.Move.getUpAnim != getUp ||
                    pair.Move.sourcePair.getUp != getUp || pair.Move.grounding != grounding ||
                    pair.Move.sourcePair.recoveryGrounding != recovery ||
                    pair.Move.sourcePair.recoveryBlendSeconds != .12f)
                    throw new InvalidOperationException("Runtime pair identity or full native duration mismatch.");
                record.attack = attack;
                record.reaction = reaction;
                record.getUp = CombatExpansionInventory.Identity(getUp);
                record.grounding = CombatExpansionInventory.Identity(grounding);
                record.recoveryGrounding = CombatExpansionInventory.Identity(recovery);
                record.attackerDriver = CombatExpansionInventory.Identity(move.sourcePair.attackerDriver);
                record.receiverDriver = CombatExpansionInventory.Identity(move.sourcePair.receiverDriver);
                record.sourceAvatar = CombatExpansionInventory.Identity(source.Animator.avatar);
                record.targetAvatar = CombatExpansionInventory.Identity(target.Animator.avatar);
                record.sourceDuration = pair.Duration;
                record.recoveryDuration = getUp.length;
                return pair;
            }
            catch
            {
                pair.Cancel();
                throw;
            }
        }

        static void E10Complete(FrankBattlePairPlayback pair)
        {
            var complete = typeof(FrankBattlePairPlayback).GetMethod("CompleteSourceMotion",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (complete == null || !float.IsFinite(pair.SampleTime) || pair.SampleTime != pair.Duration)
                throw new InvalidOperationException("Recovery requires actual full source endpoint.");
            complete.Invoke(pair, null);
            if (!pair.IsRecovering || pair.AttackerActor || pair.ReceiverActor)
                throw new InvalidOperationException("Actual controller recovery failed to start or release actors.");
        }

        static void E10Cancel(FrankBattlePairPlayback pair, CharacterCombat source, CharacterCombat target)
        {
            pair.Cancel();
            if (pair.Playing || pair.IsRecovering || pair.AttackerActor || pair.ReceiverActor ||
                source.IsBusy || target.IsBusy ||
                !float.IsFinite(source.Animator.speed) || !float.IsFinite(target.Animator.speed) ||
                source.Animator.speed != 1 || target.Animator.speed != 1)
                throw new InvalidOperationException("Cancellation left owned pair, actors, busy state or speeds.");
        }
    }
}
