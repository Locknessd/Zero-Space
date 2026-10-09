using System;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static void E10MeasureInstallationLanding(FrankBattlePairPlayback pair, SwordRegion sword, CASkin[] skins,
            CASkin[] attackerSkins, CharacterCombat target, CACase record,
            CombatExpansionSamurai10Setup.Measurement measured, StringBuilder log)
        {
            var cue = measured.profile.cues.Single(c => c.group == "body_fall");
            var expected = record.landing.Single(r => Mathf.Abs(r.seconds - cue.seconds) < .000001f);
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            var actual = CALandingFrame(pair, sword, skins, attackerSkins, target, cue.seconds, ref bounds);
            E10FiniteEvidence(actual);
            if (actual.supportBone != expected.supportBone || actual.supportMesh != expected.supportMesh ||
                actual.supportRenderer != expected.supportRenderer || actual.supportVertex != expected.supportVertex ||
                actual.supportTriangle != expected.supportTriangle ||
                Vector3.Distance(actual.supportWorldPoint, expected.supportWorldPoint) > .001f ||
                Mathf.Abs(actual.bodySupportMinimumY - GroundingCushionFor(10, target, true)) > .002f ||
                actual.torsoFacingUp < .75f)
                throw new InvalidOperationException("Execution10 reviewed cushioned support vertex changed.");
            var bone = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), actual.supportBone);
            var transform = target.Animator.GetBoneTransform(bone);
            if (!transform)
                throw new InvalidOperationException("Execution10 support bone is unmapped.");
            var offset = transform.InverseTransformPoint(actual.supportWorldPoint);
            if (record.direction == 1)
            {
                cue.contactBone = bone;
                cue.contactOffset = offset;
                cue.avatarContacts = new[] { new BattleSfxBank.ContactAnchor
                {
                    avatar = target.Animator.avatar,
                    bone = bone,
                    offset = offset
                } };
                measured.regions[0] = target.name + " " + actual.supportBone + " non-leg supporting skin vertex; " +
                    actual.supportMesh + "; " + actual.supportRenderer + "; triangle=" + actual.supportTriangle +
                    "; vertex=" + actual.supportVertex;
            }
            if (cue.contactBone != bone || !cue.TryContactPosition(target.Animator, out var point) ||
                Vector3.Distance(point, actual.supportWorldPoint) > .001f)
                throw new InvalidOperationException("Execution10 saved landing anchor misses support by over 1mm.");
            // Track the selected physical vertex, not the changing minimum-support identity, before impact.
            var support = skins.Single(s => s.identity == actual.supportMesh && s.path == actual.supportRenderer);
            pair.EvaluateAt(cue.seconds - CADelta);
            support.Update();
            Vector3 previous = support.world[actual.supportVertex];
            pair.EvaluateAt(cue.seconds);
            Vector3 velocity = target.Animator.transform.InverseTransformDirection(
                (actual.supportWorldPoint - previous) / CADelta);
            if (!float.IsFinite(velocity.sqrMagnitude) || velocity.sqrMagnitude < .00000001f)
                throw new InvalidOperationException("Execution10 landing direction has no finite measured approach.");
            Vector3 direction = velocity.normalized;
            if (record.direction == 1)
                measured.directions[0] = direction;
            else if (Vector3.Distance(measured.directions[0], direction) > .001f)
                throw new InvalidOperationException("Execution10 landing direction differs between lanes.");
            pair.EvaluateAt(pair.Duration);
            var repeated = CALandingFrame(pair, sword, skins, attackerSkins, target, cue.seconds, ref bounds);
            var seek = CACompareSeek(actual, repeated);
            if (!seek.sameSupportBone || seek.supportPointErrorM > .001f || seek.maxTrajectoryErrorM > .001f ||
                seek.supportMinimumErrorM > .001f || seek.torsoFacingUpError > .0001f ||
                !cue.TryContactPosition(target.Animator, out var repeatedAnchor) ||
                Vector3.Distance(repeatedAnchor, repeated.supportWorldPoint) > .001f)
                throw new InvalidOperationException("Execution10 landing backward seek is unstable.");
            log.AppendLine(FormattableString.Invariant($"Landing victim={target.name}; lane={record.direction}; ") +
                FormattableString.Invariant($"time={cue.seconds:R}; supportY={point.y:R}; bone={bone}; ") +
                FormattableString.Invariant($"local=({offset.x:R},{offset.y:R},{offset.z:R}); ") +
                FormattableString.Invariant($"approachVictimMps=({velocity.x:R},{velocity.y:R},{velocity.z:R}); ") +
                measured.regions[0] + "; damaging body_fall; finalLanding=false.");
        }
    }
}
