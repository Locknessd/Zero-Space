using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static CACase CANewCase(FrankBattlePairPlayback pair, CharacterCombat source, CharacterCombat target,
            int direction, float spacing, SwordRegion sword, CASkin[] skins)
        {
            return new CACase
            {
                attacker = source.name,
                victim = target.name,
                direction = direction,
                spacing = spacing,
                duration = pair.Duration,
                attackerAvatar = CombatExpansionInventory.Identity(source.Animator.avatar),
                victimAvatar = CombatExpansionInventory.Identity(target.Animator.avatar),
                attackClip = CombatExpansionInventory.Identity(pair.Move.sourcePair.attack),
                reactionClip = CombatExpansionInventory.Identity(pair.Move.sourcePair.reaction),
                attackerDriver = CombatExpansionInventory.Identity(pair.Move.sourcePair.attackerDriver),
                victimDriver = CombatExpansionInventory.Identity(pair.Move.sourcePair.receiverDriver),
                bladeMesh = CombatExpansionInventory.Identity(sword.renderer.GetComponent<MeshFilter>().sharedMesh),
                bladeRenderer = AnimationUtility.CalculateTransformPath(sword.renderer.transform,
                    pair.AttackerActor.activeDriver.transform),
                targetSkinMeshes = skins.Select(s => s.path + "=" + s.identity).ToArray(),
                targetSkinTriangleCounts = skins.Select(s => s.triangles.Length / 3).ToArray(),
                torsoTriangleCount = skins.Sum(s => Enumerable.Range(0, s.triangles.Length / 3)
                    .Count(f => s.torso[s.triangles[f * 3]] && s.torso[s.triangles[f * 3 + 1]] &&
                        s.torso[s.triangles[f * 3 + 2]])),
                supportTriangleCount = skins.Sum(s => Enumerable.Range(0, s.triangles.Length / 3)
                    .Count(f => s.support[s.triangles[f * 3]] && s.support[s.triangles[f * 3 + 1]] &&
                        s.support[s.triangles[f * 3 + 2]]))
            };
        }

        static void CACompareDirections(CAReport report)
        {
            foreach (var forward in report.cases.Where(c => c.direction == 1))
            {
                var reverse = report.cases.Single(c => c.attacker == forward.attacker && c.direction == -1);
                foreach (var first in forward.contacts)
                {
                    var second = reverse.contacts.Single(c => c.seconds == first.seconds);
                    report.directionAgreement.Add(new CAAgreement
                    {
                        attacker = forward.attacker,
                        seconds = first.seconds,
                        gapDifferenceM = Mathf.Abs(first.minimumGapM - second.minimumGapM),
                        sameChosenBone = first.chosenBone == second.chosenBone,
                        boneLocalAnchorDifferenceM = first.chosenBone == second.chosenBone
                            ? Vector3.Distance(first.targetBoneLocalPoint, second.targetBoneLocalPoint) : -1,
                        hipsLocalAnchorDifferenceM = Vector3.Distance(first.targetHipsLocalPoint,
                            second.targetHipsLocalPoint),
                        bladeLocalAnchorDifferenceM = Vector3.Distance(first.sourceBladeLocalPoint,
                            second.sourceBladeLocalPoint),
                        victimLocalVelocityDifferenceMps = Vector3.Distance(first.bladeVelocityVictimMps,
                            second.bladeVelocityVictimMps),
                        victimFloorDifferenceM = Mathf.Abs(first.victimMinimumY - second.victimMinimumY),
                        attackerFloorDifferenceM = Mathf.Abs(first.attackerMinimumY - second.attackerMinimumY)
                    });
                }
            }
        }

        static void CAVector(StringBuilder rows, Vector3 point)
        {
            rows.Append(FormattableString.Invariant($",{point.x:R},{point.y:R},{point.z:R}"));
        }

        static void CAWriteCsv(CACase record, string stem, string output = CAOutput)
        {
            var rows = new StringBuilder("attacker,victim,direction,spacingM,sourceSeconds,torsoMinimumWorldY," +
                "victimMinimumWorldY,attackerMinimumWorldY,hipsWorldX,hipsWorldY,hipsWorldZ," +
                "headWorldX,headWorldY,headWorldZ,leftFootWorldX,leftFootWorldY,leftFootWorldZ," +
                "rightFootWorldX,rightFootWorldY,rightFootWorldZ,bodySupportMinimumY," +
                "supportWorldPointX,supportWorldPointY,supportWorldPointZ,supportBone,torsoFacingUp\n");
            foreach (var row in record.landing)
            {
                rows.Append(FormattableString.Invariant($"{record.attacker},{record.victim},{record.direction},{record.spacing:R},{row.seconds:R},{row.torsoMinimumY:R},{row.victimMinimumY:R},{row.attackerMinimumY:R}"));
                foreach (var point in new[] { row.hips, row.head, row.leftFoot, row.rightFoot })
                    CAVector(rows, point);
                rows.Append(FormattableString.Invariant($",{row.bodySupportMinimumY:R}"));
                CAVector(rows, row.supportWorldPoint);
                rows.AppendLine(FormattableString.Invariant($",{row.supportBone},{row.torsoFacingUp:R}"));
            }
            File.WriteAllText(output + "/" + stem + "_Landing.csv", rows.ToString());
            rows = new StringBuilder("attacker,victim,direction,spacingM,sourceSeconds,gapM,attackerMinimumY," +
                "victimMinimumY,chosenBone,targetBoneLocalX,targetBoneLocalY,targetBoneLocalZ," +
                "bladeLocalX,bladeLocalY,bladeLocalZ,bladeVelocityVictimX,bladeVelocityVictimY," +
                "bladeVelocityVictimZ,tiedTrianglePairs\n");
            foreach (var row in record.contacts)
            {
                rows.Append(FormattableString.Invariant($"{record.attacker},{record.victim},{record.direction},{record.spacing:R},{row.seconds:R},{row.minimumGapM:R},{row.attackerMinimumY:R},{row.victimMinimumY:R},{row.chosenBone}"));
                CAVector(rows, row.targetBoneLocalPoint);
                CAVector(rows, row.sourceBladeLocalPoint);
                CAVector(rows, row.bladeVelocityVictimMps);
                rows.AppendLine("," + row.tiedTriangles.Count);
            }
            File.WriteAllText(output + "/" + stem + "_Contacts.csv", rows.ToString());
        }
    }
}
