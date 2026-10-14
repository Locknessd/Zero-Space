using System;
using System.Linq;
using FrankRetarget;
using UnityEngine;

public static partial class BattleRosterSetup
{
    static void BakeRosterContacts(FrankBattlePairPlayback pair, CharacterCombat source,
        CharacterCombat target, CombatTripletData move, BattleSfxBank.Move profile)
    {
        if (profile?.cues == null)
            return;
        foreach (var cue in profile.cues)
        {
            bool landing = cue.group == "ground_impact" || cue.group == "landing_dust" || cue.finalLanding ||
                cue.group == "body_fall" || cue.group == "knockout_fall";
            bool contact = cue.group == "light_hit" || cue.group == "heavy_hit" || cue.group == "stab_hit";
            if (!landing && !contact)
                continue;
            pair.EvaluateAt(cue.seconds);
            string selection = cue.contactSource;
            if (string.IsNullOrEmpty(selection))
                selection = move.weapon != TrumpWeaponManager.WeaponType.None ? "Weapon" : "RightHand";
            if (landing || move.skill != BattleSkill.None)
                selection = "Gun";
            var probe = new BattlePresentationContactSetup.ContactProbe(pair, source, target, selection);
            HumanBodyBones bone;
            Vector3 offset;
            float gap = 0;
            if (landing)
                probe.GroundAnchor(out bone, out offset);
            else if (move.moveName.StartsWith("SlapFace_", StringComparison.Ordinal) && contact)
            {
                bone = HumanBodyBones.Head;
                var hand = source.Animator.GetBoneTransform((HumanBodyBones)Enum.Parse(
                    typeof(HumanBodyBones), selection));
                var limb = pair.AttackerActor.Pose.limbs.First(l => l.end == hand);
                var point = probe.FacePoint(limb.sourceEnd.position);
                offset = target.Animator.GetBoneTransform(bone).InverseTransformPoint(point);
            }
            else if (selection == "Gun")
            {
                bone = HumanBodyBones.Chest;
                var anchor = target.Animator.GetBoneTransform(bone);
                offset = anchor.InverseTransformPoint(probe.BodyPoint(anchor.position));
            }
            else
                gap = probe.MeasureSparse(out _, out bone, out offset);
            var entry = new BattleSfxBank.ContactAnchor
            {
                avatar = target.Animator.avatar,
                bone = bone,
                offset = offset
            };
            cue.avatarContacts = (cue.avatarContacts ?? Array.Empty<BattleSfxBank.ContactAnchor>())
                .Where(a => a.avatar != entry.avatar).Concat(new[] { entry }).ToArray();
            calibrationReport.AppendLine(" contact " + target.GetComponent<BattleCharacterAppearance>().CurrentCharacterId +
                "/" + move.moveName + "/" + cue.group + " time=" + cue.seconds + " gap=" + gap + "m");
        }
    }
}
