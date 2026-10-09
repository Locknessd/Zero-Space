using System;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static void ValidateInstallationEvidence(CAReport evidence)
        {
            if (evidence == null || evidence.scene != CombatExpansionInventory.Battle ||
                string.IsNullOrWhiteSpace(evidence.utc) || evidence.cases == null || evidence.cases.Count != 4 ||
                evidence.directionAgreement == null || evidence.directionAgreement.Count != 4 ||
                evidence.scope == null || !evidence.scope.Contains("Relative velocity subtracts"))
                throw new InvalidOperationException("Expected complete corrected four-case Execution01 Report.json.");
            foreach (string attacker in new[] { "Mankey", "Pepe" })
            foreach (int direction in new[] { 1, -1 })
            {
                var record = evidence.cases.Single(c => c.attacker == attacker && c.direction == direction);
                string victim = attacker == "Mankey" ? "Pepe" : "Mankey";
                float[] times = attacker == "Mankey" ? new[] { .395833343f, 2.0333333f }
                    : new[] { .3875f, 2.0291667f };
                if (record.victim != victim ||
                    record.attackerAvatar != CombatExpansionSamuraiSetup.AvatarIdentity(attacker) ||
                    record.victimAvatar != CombatExpansionSamuraiSetup.AvatarIdentity(victim) ||
                    Mathf.Abs(record.duration - CombatExpansionSamuraiSetup.Duration) > .00001f ||
                    Mathf.Abs(record.spacing - (attacker == "Mankey" ? 1.64f : 1.7f)) > .00001f ||
                    record.contacts == null || record.contacts.Count != 2 || record.landing == null ||
                    record.landing.Count < 240 || record.backwardSeeks == null || record.backwardSeeks.Count < 4 ||
                    record.backwardSeeks.Any(s => !s.withinOneMillimeter) ||
                    !float.IsFinite(record.finalTorsoFacingUp) || record.finalTorsoFacingUp > -.97f)
                    throw new InvalidOperationException("Incomplete Execution01 avatar/direction evidence.");
                for (int index = 0; index < 2; index++)
                {
                    var contact = record.contacts[index];
                    string bone = index == 1 && attacker == "Mankey" ? "LeftShoulder" : "UpperChest";
                    string[] groups = index == 0 ? new[] { "AxialTorso" } : attacker == "Mankey"
                        ? new[] { "LeftArm" } : new[] { "AxialTorso", "LeftArm" };
                    if (Mathf.Abs(contact.seconds - times[index]) > .000001f || contact.chosenBone != bone ||
                        !float.IsFinite(contact.minimumGapM) || contact.minimumGapM < 0 ||
                        contact.minimumGapM > .001f ||
                        !float.IsFinite(contact.targetBoneLocalPoint.sqrMagnitude) ||
                        !float.IsFinite(contact.relativeBladeVelocityVictimMps.sqrMagnitude) ||
                        contact.relativeBladeVelocityVictimMps.sqrMagnitude < .01f ||
                        Mathf.Abs(contact.finiteDifferenceDeltaSeconds - CADelta) > .0000001f ||
                        contact.tiedTriangles == null || contact.tiedTriangles.Count == 0 ||
                        !contact.tiedTriangles.SelectMany(t => t.dominantGroups).Distinct().OrderBy(g => g)
                            .SequenceEqual(groups.OrderBy(g => g)))
                        throw new InvalidOperationException(
                            "Execution01 contact anatomy, times or corrected velocity differ.");
                    var other = evidence.cases.Single(c => c.attacker == attacker && c.direction == -direction)
                        .contacts[index];
                    if (other.chosenBone != contact.chosenBone ||
                        Vector3.Distance(other.targetBoneLocalPoint, contact.targetBoneLocalPoint) > .001f ||
                        Vector3.Distance(other.relativeBladeVelocityVictimMps,
                            contact.relativeBladeVelocityVictimMps) > .001f)
                        throw new InvalidOperationException(
                            "Execution01 directions disagree on anchor or relative velocity.");
                }
                float landingTime = attacker == "Mankey" ? 2.666666746f : 2.712500095f;
                var landing = record.landing.Single(r => Mathf.Abs(r.seconds - landingTime) < .000001f);
                if (landing.supportBone != (attacker == "Mankey" ? "LeftUpperArm" : "Head") ||
                    Mathf.Abs(landing.bodySupportMinimumY - (attacker == "Mankey" ? .01048177f : .01f)) > .00001f)
                    throw new InvalidOperationException("Execution01 reviewed support landing is missing.");
            }
            foreach (var agreement in evidence.directionAgreement)
                if (!agreement.sameChosenBone || !float.IsFinite(agreement.boneLocalAnchorDifferenceM) ||
                    agreement.boneLocalAnchorDifferenceM > .001f || agreement.gapDifferenceM > .001f)
                    throw new InvalidOperationException("Execution01 direction agreement did not pass.");
        }

        static void ValidateInstallationIdentities(CACase record, CombatTripletData move,
            CharacterCombat source, CharacterCombat target, string grounding)
        {
            string attackerDriver = source.name == "Mankey"
                ? "224e94b27f72de65aa8403d8a294fde0:2627981156790044268"
                : "e843971f00482b8af9b3e546a40aa446:7468503520573832048";
            string victimDriver = target.name == "Pepe"
                ? "22b682ea72ad141548c935b490a1b6ba:5840121589346898076"
                : "e9bda2f6da685faedac3c08af217f164:4668455661232588296";
            if (record.attackClip != CombatExpansionInventory.Identity(move.attackAnim) ||
                record.reactionClip != CombatExpansionInventory.Identity(move.hitAnim) ||
                record.attackerAvatar != CombatExpansionInventory.Identity(source.Animator.avatar) ||
                record.victimAvatar != CombatExpansionInventory.Identity(target.Animator.avatar) ||
                record.attackerDriver != attackerDriver || record.victimDriver != victimDriver ||
                record.attackerDriver != CombatExpansionInventory.Identity(move.sourcePair.attackerDriver) ||
                record.victimDriver != CombatExpansionInventory.Identity(move.sourcePair.receiverDriver) ||
                record.bladeMesh != "3e685dd57e9c78b49b20bef3e8358ae3:4300002" ||
                grounding != CombatExpansionInventory.Identity(move.grounding))
                throw new InvalidOperationException("Execution01 report native identities differ from saved assets.");
            var meshes = CASkins(target).Select(s => s.path + "=" + s.identity).ToArray();
            if (!meshes.SequenceEqual(record.targetSkinMeshes))
                throw new InvalidOperationException("Execution01 victim skin identity changed since measurement.");
        }
    }
}
