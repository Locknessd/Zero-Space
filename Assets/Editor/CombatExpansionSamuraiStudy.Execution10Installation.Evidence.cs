using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static CAReport E10InstallationEvidence()
        {
            CombatExpansionSamurai10Setup.RequireStatus(Execution10ContactsOutput + "/Status.txt",
                "CAPTURED four cases; backward seeks stable.");
            CombatExpansionSamurai10Setup.RequireStatus(E10Output + "/Status.txt",
                "PASS numeric checks for four cases; visual review required");
            string path = BladeOutput + "/Execution10GroundingValidation.txt";
            CombatExpansionSamurai10Setup.RequireStatus(path, "PASS\n");
            string grounding = File.ReadAllText(path);
            if (!grounding.Contains("Execution10; both assignments and both lane directions") ||
                !grounding.Contains("Pair cases=4/4; actor cases=8/8;") ||
                !grounding.Contains("violations=0.") || !grounding.Contains("minimum rate=361 Hz"))
                throw new InvalidOperationException("Original Execution10 361Hz grounding PASS is required.");
            CombatExpansionSamurai10Setup.RequireStatus(BladeOutput + "/Presentation/Status.txt", "PASS");
            var report = JsonUtility.FromJson<CAReport>(File.ReadAllText(Execution10ContactsOutput + "/Report.json"));
            if (report == null || report.scene != CombatExpansionInventory.Battle ||
                report.grounding != CombatExpansionSamurai10Setup.GroundingId ||
                string.IsNullOrWhiteSpace(report.utc) ||
                report.scope == null || !report.scope.Contains("Execution10") ||
                !report.scope.Contains("Relative velocity subtracts") || report.cases == null ||
                report.cases.Count != 4 || report.directionAgreement == null || report.directionAgreement.Count != 2)
                throw new InvalidOperationException("Complete original four-case Execution10 contact report required.");
            E10FiniteEvidence(report);
            foreach (string attacker in new[] { "Mankey", "Pepe" })
            foreach (int direction in new[] { 1, -1 })
            {
                var record = report.cases.Single(c => c.attacker == attacker && c.direction == direction);
                string target = attacker == "Mankey" ? "Pepe" : "Mankey";
                if (record.victim != target ||
                record.attackerAvatar != CombatExpansionSamurai10Setup.AvatarIdentity(attacker) ||
                    record.victimAvatar != CombatExpansionSamurai10Setup.AvatarIdentity(target) ||
                    Mathf.Abs(record.duration - CombatExpansionSamurai10Setup.Duration) > .000001f ||
                    Mathf.Abs(record.spacing - 1.7f) > .000001f || record.contacts == null ||
                    record.contacts.Count != 1 || record.landing == null || record.backwardSeeks == null ||
                    record.imageTimes == null || record.backwardSeeks.Count != record.imageTimes.Length ||
                    record.backwardSeeks.Count < 10 || record.finalTorsoFacingUp < .97f ||
                    record.targetSkinMeshes == null || record.targetSkinMeshes.Length == 0 ||
                    record.targetSkinTriangleCounts == null ||
                    record.targetSkinTriangleCounts.Length != record.targetSkinMeshes.Length ||
                    record.targetSkinTriangleCounts.Any(n => n <= 0) || record.supportTriangleCount <= 0)
                    throw new InvalidOperationException("Incomplete Execution10 avatar/direction contact evidence.");
                float landingTime = CombatExpansionSamurai10Setup.LandingTime(attacker);
                float stabTime = CombatExpansionSamurai10Setup.StabTime(attacker);
                var expectedTimes = CCTimes(1.3f, record.duration, 240).Append(landingTime).Distinct().OrderBy(t => t);
                if (!record.landing.Select(f => f.seconds).SequenceEqual(expectedTimes))
                    throw new InvalidOperationException("Execution10 full support sampling grid is incomplete.");
                var landing = record.landing.Single(f => Mathf.Abs(f.seconds - landingTime) < .000001f);
                float cushion = target == "Mankey" ? .005f : .01f;
                if (Mathf.Abs(landing.bodySupportMinimumY - cushion) > .002f || landing.torsoFacingUp < .75f ||
                    string.IsNullOrEmpty(landing.supportBone) || string.IsNullOrEmpty(landing.supportMesh) ||
                    string.IsNullOrEmpty(landing.supportRenderer) || landing.supportTriangle < 0 ||
                    landing.supportVertex < 0)
                    throw new InvalidOperationException("Reviewed Execution10 first cushioned landing is missing.");
                var contact = record.contacts.Single();
                if (Mathf.Abs(contact.seconds - stabTime) > .000001f || contact.chosenBone != "Head" ||
                    contact.minimumGapM < 0 || contact.minimumGapM > .001f || contact.anchorRoundtripErrorM > .001f ||
                    contact.anchorRoundtripErrorM < 0 || contact.relativeBladeVelocityVictimMps.sqrMagnitude < .01f ||
                    Mathf.Abs(contact.finiteDifferenceDeltaSeconds - CADelta) > .0000001f ||
                    contact.tieToleranceM != CATie || contact.tiedTriangles == null ||
                    contact.tiedTriangles.Count == 0 ||
                    contact.tiedTriangles.Any(t => t.dominantGroups == null || t.dominantGroups.Length == 0 ||
                        string.IsNullOrEmpty(t.mesh) || string.IsNullOrEmpty(t.renderer) || t.targetTriangle < 0 ||
                        t.bladeRegionTriangle < 0 || t.gapM < 0 || t.gapM > contact.minimumGapM + CATie))
                    throw new InvalidOperationException("Reviewed Execution10 downward Head blade candidate changed.");
                var opposite = report.cases.Single(c => c.attacker == attacker && c.direction == -direction).contacts.Single();
                if (opposite.chosenBone != contact.chosenBone ||
                    Vector3.Distance(opposite.targetBoneLocalPoint, contact.targetBoneLocalPoint) > .001f ||
                    Vector3.Distance(opposite.relativeBladeVelocityVictimMps, contact.relativeBladeVelocityVictimMps) > .001f)
                    throw new InvalidOperationException("Execution10 lane directions disagree on blade anchor/velocity.");
                foreach (float time in record.imageTimes)
                {
                    var seek = record.backwardSeeks.Single(s => s.seconds == time);
                    if (!seek.withinOneMillimeter || !seek.sameSupportBone || seek.torsoFacingUpError > .0001f ||
                        Mathf.Max(seek.maxTrajectoryErrorM, seek.torsoMinimumErrorM, seek.fullBodyMinimumErrorM,
                            seek.bladeTipErrorM, seek.contactGapErrorM, seek.contactAnchorErrorM,
                            seek.supportMinimumErrorM, seek.supportPointErrorM) > .001f)
                        throw new InvalidOperationException("Execution10 backward-seek evidence failed.");
                }
                if (!record.imageTimes.Contains(landingTime) || !record.imageTimes.Contains(stabTime))
                    throw new InvalidOperationException("Execution10 exact contact seeks are missing.");
            }
            foreach (var agreement in report.directionAgreement)
                if (!agreement.sameChosenBone || agreement.boneLocalAnchorDifferenceM > .001f ||
                    agreement.gapDifferenceM > .001f)
                    throw new InvalidOperationException("Execution10 blade lane agreement failed.");
            return report;
        }

        static void E10FiniteEvidence(object value)
        {
            if (value == null || value is string)
                return;
            if (value is float number)
            {
                if (!float.IsFinite(number))
                    throw new InvalidOperationException("Nonfinite Execution10 evidence.");
                return;
            }
            if (value.GetType().IsPrimitive || value.GetType().IsEnum)
                return;
            if (value is IEnumerable elements)
            {
                foreach (var element in elements)
                    E10FiniteEvidence(element);
                return;
            }
            foreach (var field in value.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (!field.IsNotSerialized)
                    E10FiniteEvidence(field.GetValue(value));
        }
    }
}
