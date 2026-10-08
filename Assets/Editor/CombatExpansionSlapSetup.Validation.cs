using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapSetup
    {
        static void ValidateMove(CombatTripletData move, Spec spec)
        {
            if (move == null || move.moveName != spec.Id || !move.requiresExplicitSelection ||
                !move.attackAnim || !move.hitAnim || !move.grounding || move.sourcePair == null ||
                move.sourcePair.attack != move.attackAnim || move.sourcePair.reaction != move.hitAnim ||
                !move.sourcePair.attackerDriver || !move.sourcePair.receiverDriver || move.sourcePair.showWeapon ||
                move.sourcePair.attackerWeaponPrefab || !move.sourcePair.transferReceiverFingers ||
                Mathf.Abs(move.attackRange - .8f) > .0001f ||
                Vector3.Distance(move.sourcePair.receiverOffset, Vector3.forward * .8f) > .0001f ||
                Quaternion.Angle(move.sourcePair.receiverRotation, Quaternion.Euler(0, 180, 0)) > .001f ||
                Mathf.Abs(move.sourcePair.entryBlendSeconds - .12f) > .0001f ||
                Mathf.Abs(move.sourcePair.standingRecoverySeconds - .3f) > .0001f)
                throw new InvalidOperationException("Incomplete grounded unarmed SlapFace source pair " + spec.Key);
            float duration = Mathf.Max(move.attackAnim.length, move.hitAnim.length);
            if (!float.IsFinite(spec.seconds) || spec.seconds < .1f ||
                spec.seconds + 1f / 120 > Mathf.Min(move.attackAnim.length, move.hitAnim.length) ||
                move.grounding.tracks == null || move.grounding.tracks.Length != 4 ||
                move.grounding.tracks.Any(t => t == null || Mathf.Abs(t.duration - duration) > .0001f))
                throw new InvalidOperationException("Invalid SlapFace contact or stale grounding duration: " + spec.Key);
        }

        static void ValidateEquipment(FrankBattlePairPlayback pair, CharacterCombat[] fighters)
        {
            foreach (var actor in new[] { pair.AttackerActor, pair.ReceiverActor })
            {
                if (actor.Pose.weaponRenderers.Length != 0 || !actor.Pose.transferFingers ||
                    actor.GetComponentsInChildren<Renderer>(true).Any(r => r.enabled && r.gameObject.activeInHierarchy) ||
                    actor.GetComponentsInChildren<Collider>(true).Any(c => c.enabled && c.gameObject.activeInHierarchy))
                    throw new InvalidOperationException("SlapFace native actor must transfer fingers and stay unarmed.");
            }
            foreach (var fighter in fighters)
            foreach (var manager in fighter.GetComponentsInChildren<TrumpWeaponManager>(true))
                if (!manager.IsUnarmedPresentation || manager.ActiveWeapon != TrumpWeaponManager.WeaponType.None)
                    throw new InvalidOperationException("SlapFace fighter lacks runtime-owned unarmed presentation.");
        }

        static void ValidateAudio(BattleSfxBank bank)
        {
            var required = new Queue<string>(new[] { "light_swing", "light_hit" });
            var visited = new HashSet<string>(StringComparer.Ordinal);
            while (required.Count > 0)
            {
                string id = required.Dequeue();
                if (!visited.Add(id))
                    continue;
                var group = bank.FindGroup(id);
                if (group == null || !group.output || group.clips == null || group.clips.Length == 0 ||
                    group.clips.Any(clip => !clip))
                    throw new InvalidOperationException("Missing routed SlapFace audio group or clip: " + id);
                foreach (string layer in group.layers ?? Array.Empty<string>())
                    if (!string.IsNullOrWhiteSpace(layer))
                        required.Enqueue(layer);
            }
        }
    }
}
