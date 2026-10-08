using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string BladeHeader = "attacker,victim,direction,seconds,bladeBodyGapM,nearestBone," +
            "bladeX,bladeY,bladeZ,bodyX,bodyY,bodyZ,boneLocalX,boneLocalY,boneLocalZ," +
            "tipX,tipY,tipZ,victimMinimumY,attackerHipsX,attackerHipsY,attackerHipsZ," +
            "victimHipsX,victimHipsY,victimHipsZ,victimLeftFootY,victimRightFootY\n";

        readonly struct BladeRecord
        {
            public readonly float seconds;
            public readonly CombatExpansionAxeDenseStudy.BodyContact contact;
            public readonly Vector3 tip;
            public readonly float minimumY;

            public BladeRecord(float seconds, CombatExpansionAxeDenseStudy.BodyContact contact,
                Vector3 tip, float minimumY)
            {
                if (!float.IsFinite(contact.gap) || contact.gap < 0 || !float.IsFinite(minimumY))
                    throw new InvalidOperationException("Invalid blade or victim geometry.");
                this.seconds = seconds;
                this.contact = contact;
                this.tip = tip;
                this.minimumY = minimumY;
            }
        }

        static IEnumerable<float> BladeTimes(float duration, bool grounded)
        {
            var times = new List<float>();
            for (int frame = 0; frame <= Mathf.CeilToInt(duration * 30); frame++)
                times.Add(Mathf.Min(frame / 30f, duration));
            foreach (var window in new[] { (grounded ? .35 : .4, 1.05), (1.75, 2.35) })
            {
                for (int frame = 0; frame <= (int)Math.Floor((window.Item2 - window.Item1) * 240); frame++)
                    times.Add((float)(window.Item1 + frame / 240.0));
                times.Add((float)window.Item2);
            }
            return times.Concat(BladeSheetTimes(duration, grounded)).Distinct().OrderBy(time => time);
        }

        static float[] BladeSheetTimes(float duration, bool grounded) => new[]
        {
            0, .3f, .4f, .5f, .6f, .7f, .8f, .9f, 1.05f, 1.3f, 1.6f,
            1.75f, 1.85f, 1.95f, 2.05f, 2.15f, 2.25f, 2.35f, 2.6f, duration
        }.Concat(grounded ? new[] { .35f, .375f } : Array.Empty<float>()).OrderBy(time => time).ToArray();

        static void AppendBladeRow(StringBuilder rows, CharacterCombat source, CharacterCombat target,
            int direction, BladeRecord record)
        {
            var hit = record.contact;
            var a = source.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
            var b = target.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
            float leftY = target.Animator.GetBoneTransform(HumanBodyBones.LeftFoot).position.y;
            float rightY = target.Animator.GetBoneTransform(HumanBodyBones.RightFoot).position.y;
            rows.Append(FormattableString.Invariant(
                $"{source.name},{target.name},{direction},{record.seconds:R},{hit.gap:R},{hit.nearestBone},"));
            foreach (var vector in new[] { hit.sourcePoint, hit.bodyPoint, hit.boneOffset, record.tip })
                rows.Append(FormattableString.Invariant($"{vector.x:R},{vector.y:R},{vector.z:R},"));
            rows.Append(FormattableString.Invariant($"{record.minimumY:R},{a.x:R},{a.y:R},{a.z:R},"));
            rows.AppendLine(FormattableString.Invariant($"{b.x:R},{b.y:R},{b.z:R},{leftY:R},{rightY:R}"));
        }
    }
}
