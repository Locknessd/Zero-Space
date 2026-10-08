using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static CALanding CALandingFrame(FrankBattlePairPlayback pair, SwordRegion sword, CASkin[] skins,
            CASkin[] attackerSkins, CharacterCombat target, float seconds, ref Bounds bounds)
        {
            pair.EvaluateAt(seconds);
            foreach (var skin in skins.Concat(attackerSkins))
            {
                skin.Update();
                foreach (var vertex in skin.world)
                    bounds.Encapsulate(vertex);
            }
            float torso = float.PositiveInfinity;
            float supportMinimum = float.PositiveInfinity;
            CASkin supportSkin = null;
            int supportVertex = -1;
            int supportTriangle = -1;
            foreach (var skin in skins)
            for (int face = 0; face < skin.triangles.Length; face += 3)
            {
                int a = skin.triangles[face];
                int b = skin.triangles[face + 1];
                int c = skin.triangles[face + 2];
                if (skin.torso[a] && skin.torso[b] && skin.torso[c])
                    torso = Mathf.Min(torso, skin.world[a].y, skin.world[b].y, skin.world[c].y);
                if (skin.support[a] && skin.support[b] && skin.support[c])
                    for (int corner = 0; corner < 3; corner++)
                    {
                        int vertex = skin.triangles[face + corner];
                        if (skin.world[vertex].y >= supportMinimum)
                            continue;
                        supportMinimum = skin.world[vertex].y;
                        supportSkin = skin;
                        supportVertex = vertex;
                        supportTriangle = face / 3;
                    }
            }
            if (!float.IsFinite(torso))
                throw new InvalidOperationException("No exact axial torso triangles for " + target.name);
            if (supportSkin == null || !float.IsFinite(supportMinimum))
                throw new InvalidOperationException("No non-leg support triangles for " + target.name);
            int supportBone = Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                .Where(h => target.Animator.GetBoneTransform((HumanBodyBones)h))
                .OrderByDescending(h => supportSkin.humans[supportVertex][h]).ThenBy(h => h).First();
            if (supportSkin.humans[supportVertex][supportBone] <= 0)
                throw new InvalidOperationException("Support vertex has no mapped bone: " + target.name);
            return new CALanding
            {
                seconds = seconds,
                torsoMinimumY = torso,
                bodySupportMinimumY = supportMinimum,
                supportWorldPoint = supportSkin.world[supportVertex],
                supportBone = ((HumanBodyBones)supportBone).ToString(),
                supportRenderer = supportSkin.path,
                supportMesh = supportSkin.identity,
                supportTriangle = supportTriangle,
                supportVertex = supportVertex,
                torsoFacingUp = CATorsoFacingUp(target),
                victimMinimumY = skins.Min(s => s.world.Min(p => p.y)),
                attackerMinimumY = attackerSkins.Min(s => s.world.Min(p => p.y)),
                hips = target.Animator.GetBoneTransform(HumanBodyBones.Hips).position,
                head = target.Animator.GetBoneTransform(HumanBodyBones.Head).position,
                leftFoot = target.Animator.GetBoneTransform(HumanBodyBones.LeftFoot).position,
                rightFoot = target.Animator.GetBoneTransform(HumanBodyBones.RightFoot).position,
                bladeTip = sword.Tip
            };
        }

        static float CATorsoFacingUp(CharacterCombat target)
        {
            var animator = target.Animator;
            var left = animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            var right = animator.GetBoneTransform(HumanBodyBones.RightShoulder);
            var chest = animator.GetBoneTransform(HumanBodyBones.UpperChest);
            if (!chest)
                chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (!left || !right || !chest || !hips)
                throw new InvalidOperationException("Torso facing requires mapped shoulders/chest/hips: " + target.name);
            var lateral = right.position - left.position;
            var axial = chest.position - hips.position;
            if (lateral.sqrMagnitude <= 1e-12f || axial.sqrMagnitude <= 1e-12f)
                throw new InvalidOperationException("Degenerate torso facing axes: " + target.name);
            var normal = Vector3.Cross(lateral.normalized, axial.normalized);
            if (!float.IsFinite(normal.sqrMagnitude) || normal.sqrMagnitude <= 1e-12f)
                throw new InvalidOperationException("Invalid torso facing cross product: " + target.name);
            float facing = Vector3.Dot(normal.normalized, Vector3.up);
            if (!float.IsFinite(facing))
                throw new InvalidOperationException("Nonfinite torso facing: " + target.name);
            return Mathf.Clamp(facing, -1, 1);
        }

        static void CASelectLanding(CACase record)
        {
            var rows = record.landing;
            var minimum = rows.OrderBy(row => row.torsoMinimumY).ThenBy(row => row.seconds).First();
            record.globalMinimumTorsoSeconds = minimum.seconds;
            record.globalMinimumTorsoY = minimum.torsoMinimumY;
            var support = rows.OrderBy(row => row.bodySupportMinimumY).ThenBy(row => row.seconds).First();
            record.globalMinimumSupportY = support.bodySupportMinimumY;
            record.globalMinimumSupportSeconds = support.seconds;
            record.firstSupportNearGroundSeconds = rows.FirstOrDefault(row =>
                row.bodySupportMinimumY <= record.nearGroundThresholdM)?.seconds ?? -1;
            record.finalTorsoFacingUp = rows[rows.Count - 1].torsoFacingUp;
            record.supportAssessment = support.bodySupportMinimumY > record.nearGroundThresholdM
                ? "No sampled near-ground non-leg support surface; do not infer a ground impact."
                : support.bodySupportMinimumY < -.001f
                    ? "Non-leg support crosses ground; crossing does not establish a valid impact."
                    : "Near-ground non-leg support candidate; anatomy and motion review required before impact approval.";
            record.firstNearGroundSeconds = rows.FirstOrDefault(row =>
                row.torsoMinimumY <= record.nearGroundThresholdM)?.seconds ?? -1;
            // Collapse each flat local minimum into its first sample; terminal endpoint remains explicit.
            for (int index = 1; index < rows.Count - 1; index++)
            {
                if (rows[index].torsoMinimumY >= rows[index - 1].torsoMinimumY)
                    continue;
                int end = index;
                while (end + 1 < rows.Count && rows[end + 1].torsoMinimumY == rows[index].torsoMinimumY)
                    end++;
                if (end + 1 < rows.Count && rows[end + 1].torsoMinimumY > rows[index].torsoMinimumY)
                    record.localMinimaSeconds.Add(rows[index].seconds);
                index = end;
            }
            record.landingAssessment = minimum.torsoMinimumY > record.nearGroundThresholdM
                ? "No sampled near-ground torso candidate; visible cushion/geometry clearance may prevent contact."
                : minimum.torsoMinimumY < -.001f
                    ? "Torso geometry crosses ground; unsigned clearance cannot establish valid landing contact."
                    : "Near-ground torso candidate exists; cushion and motion review required before contact approval.";
        }

        static CASeek CACompareSeek(CALanding baseline, CALanding current)
        {
            var result = new CASeek
            {
                seconds = baseline.seconds,
                maxTrajectoryErrorM = Mathf.Max(Vector3.Distance(baseline.hips, current.hips),
                    Vector3.Distance(baseline.head, current.head),
                    Vector3.Distance(baseline.leftFoot, current.leftFoot),
                    Vector3.Distance(baseline.rightFoot, current.rightFoot)),
                torsoMinimumErrorM = Mathf.Abs(baseline.torsoMinimumY - current.torsoMinimumY),
                supportMinimumErrorM = Mathf.Abs(baseline.bodySupportMinimumY - current.bodySupportMinimumY),
                supportPointErrorM = Vector3.Distance(baseline.supportWorldPoint, current.supportWorldPoint),
                sameSupportBone = baseline.supportBone == current.supportBone,
                torsoFacingUpError = Mathf.Abs(baseline.torsoFacingUp - current.torsoFacingUp),
                fullBodyMinimumErrorM = Mathf.Max(Mathf.Abs(baseline.victimMinimumY - current.victimMinimumY),
                    Mathf.Abs(baseline.attackerMinimumY - current.attackerMinimumY)),
                bladeTipErrorM = Vector3.Distance(baseline.bladeTip, current.bladeTip)
            };
            return result;
        }

        static float[] CAImageTimes(CACase record)
        {
            var times = new List<float>();
            foreach (var contact in record.contacts)
                times.AddRange(new[] { contact.seconds - 2 * CADelta, contact.seconds, contact.seconds + 2 * CADelta });
            times.AddRange(new[] { 2.15f, record.duration, record.globalMinimumTorsoSeconds });
            if (record.firstNearGroundSeconds >= 0)
                times.Add(record.firstNearGroundSeconds);
            if (record.firstSupportNearGroundSeconds >= 0)
                times.AddRange(new[] { record.firstSupportNearGroundSeconds - 2 * CADelta,
                    record.firstSupportNearGroundSeconds, record.firstSupportNearGroundSeconds + 2 * CADelta });
            times.Add(record.globalMinimumSupportSeconds);
            times.AddRange(record.localMinimaSeconds.Take(4));
            times.AddRange(new[] { .25f, .5f, .75f }.Select(t => Mathf.Lerp(2.15f, record.duration, t)));
            return times.Where(t => t >= 0 && t <= record.duration).Distinct().OrderBy(t => t).ToArray();
        }
    }
}
