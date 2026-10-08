using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiSetup
    {
        internal static string AvatarIdentity(string name) => name == "Mankey"
            ? "59dc3f26824c72872aa4cf6baeec671e:9000000" : "757d9d3f1766533aa895e81a07aa5197:9000000";

        internal static void ValidateMove(CombatTripletData move, CharacterCombat source, CharacterCombat target)
        {
            var pair = move.sourcePair;
            if (!source.Animator || !target.Animator ||
                CombatExpansionInventory.Identity(source.Animator.avatar) != AvatarIdentity(source.name) ||
                CombatExpansionInventory.Identity(target.Animator.avatar) != AvatarIdentity(target.name))
                throw new InvalidOperationException("Execution01 avatar identity mismatch.");
            if (move.moveName != Id || !move.requiresExplicitSelection ||
                move.weapon != TrumpWeaponManager.WeaponType.Katana ||
                !move.attackAnim || !move.hitAnim || !move.grounding || !move.getUpAnim || pair == null ||
                pair.attack != move.attackAnim || pair.reaction != move.hitAnim || pair.getUp != move.getUpAnim ||
                !pair.attackerDriver || !pair.receiverDriver || !pair.showWeapon || pair.attackerWeaponPrefab ||
                !pair.transferReceiverFingers || pair.constrainDepthAfterSpacing || pair.spacing ||
                Mathf.Abs(pair.recoveryBlendSeconds - .12f) > .00001f || pair.standingRecoverySeconds != 0 ||
                Mathf.Abs(pair.entryBlendSeconds - .12f) > .00001f ||
                Mathf.Abs(pair.maximumAlignmentError - .15f) > .00001f ||
                Vector3.Distance(pair.receiverOffset, Vector3.forward * move.attackRange) > .00001f ||
                Quaternion.Angle(pair.receiverRotation, Quaternion.Euler(0, 180, 0)) > .001f ||
                Mathf.Abs(move.attackAnim.length - AttackDuration) > .00001f ||
                Mathf.Abs(move.hitAnim.length - Duration) > .00001f)
                throw new InvalidOperationException("Invalid Execution01 pair configuration: " +
                    UnityEngine.JsonUtility.ToJson(move));
            if (CombatExpansionInventory.Identity(move.attackAnim) != "3e685dd57e9c78b49b20bef3e8358ae3:7400002" ||
                CombatExpansionInventory.Identity(move.hitAnim) != "b01b411d366a5a344bddf3428a7bc8f7:7400002" ||
                CombatExpansionInventory.Identity(move.getUpAnim) !=
                    "157da7dff6b3b3148a29246060747ce5:1827226128182048838")
                throw new InvalidOperationException("Execution01 native clip identity mismatch.");
            foreach (var asset in new UnityEngine.Object[] { move.attackAnim, move.hitAnim, move.getUpAnim,
                pair.attackerDriver, pair.receiverDriver, source.Animator.avatar, target.Animator.avatar })
                if (!EditorUtility.IsPersistent(asset) || EditorUtility.IsDirty(asset))
                    throw new InvalidOperationException("Required source asset is transient or dirty: " + asset.name);
            if (pair.receiverDriver.GetComponentsInChildren<Renderer>(true).Length != 0 ||
                pair.receiverDriver.GetComponentsInChildren<Collider>(true).Length != 0 ||
                pair.attackerDriver.GetComponentsInChildren<Collider>(true).Length != 0 ||
                pair.attackerDriver.pose.weaponRenderers.Length != 2)
                throw new InvalidOperationException(
                    "Execution01 drivers require armed A and renderer/collider-free B.");
            var tracks = move.grounding.tracks;
            if (tracks == null || tracks.Length != 4 || tracks.Any(t => t == null ||
                Mathf.Abs(t.duration - Duration) > .00001f || t.lift == null || t.lift.Length < 761 ||
                t.lift.Any(v => !float.IsFinite(v) || v < 0 || v > .3f)))
                throw new InvalidOperationException("Execution01 grounding is stale or incomplete.");
            foreach (var avatar in new[] { source.Animator.avatar, target.Animator.avatar })
            foreach (bool receiver in new[] { false, true })
                if (tracks.Count(t => t.avatar == avatar && t.receiver == receiver) != 1)
                    throw new InvalidOperationException("Execution01 grounding lacks a unique avatar/role track.");
            if (!pair.recoveryGrounding || pair.recoveryGrounding.tracks == null)
                throw new InvalidOperationException("Missing prone recovery grounding.");
            foreach (var avatar in new[] { source.Animator.avatar, target.Animator.avatar })
            {
                var track = pair.recoveryGrounding.tracks.Single(t => t != null && t.avatar == avatar && t.receiver);
                if (Mathf.Abs(track.duration - move.getUpAnim.length) > .0001f || track.lift == null ||
                    track.lift.Length < 2 || track.lift.Any(v => !float.IsFinite(v) || v < 0))
                    throw new InvalidOperationException("Invalid prone recovery grounding track.");
            }
        }

        static void ValidateAudio(BattleSfxBank bank)
        {
            var pending = new Queue<string>(new[] { "stab_hit", "heavy_hit", "thrust_swing", "blade_swing",
                "body_fall", "knockout_fall", "getup", "hurt_voice", "ko_impact" });
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (pending.Count > 0)
            {
                string id = pending.Dequeue();
                if (!visited.Add(id))
                    continue;
                var group = bank.FindGroup(id);
                if (group == null || !group.output || group.clips == null || group.clips.Length == 0 ||
                    group.clips.Any(c => !c))
                    throw new InvalidOperationException("Missing routed Execution01 audio/layer: " + id);
                foreach (string layer in group.layers ?? Array.Empty<string>())
                    if (!string.IsNullOrWhiteSpace(layer))
                        pending.Enqueue(layer);
            }
        }
    }
}
