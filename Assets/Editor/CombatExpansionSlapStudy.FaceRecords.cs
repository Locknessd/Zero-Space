using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        const string FaceHeader = "seconds,handHeadGapM,handX,handY,handZ,headX,headY,headZ," +
            "headLocalX,headLocalY,headLocalZ,attackerMinimumY,receiverMinimumY," +
            "attackerHipsX,attackerHipsY,attackerHipsZ,receiverHipsX,receiverHipsY,receiverHipsZ\n";

        readonly struct FaceRow
        {
            public readonly CombatExpansionAxeDenseStudy.BodyContact contact;
            public readonly float attackerY;
            public readonly float receiverY;
            public readonly Vector3 hand;
            public readonly Vector3 head;
            public readonly Quaternion handRotation;
            public readonly Quaternion headRotation;

            public FaceRow(CombatExpansionAxeDenseStudy.BodyContact contact, float attackerY, float receiverY,
                Transform hand, Transform head)
            {
                if (!float.IsFinite(attackerY) || !float.IsFinite(receiverY))
                    throw new InvalidOperationException("Nonfinite Slap body clearance.");
                this.contact = contact;
                this.attackerY = attackerY;
                this.receiverY = receiverY;
                this.hand = hand.position;
                this.head = head.position;
                handRotation = hand.rotation;
                headRotation = head.rotation;
            }
        }

        static float[] FaceSheetTimes(int sequence, float duration)
        {
            float start = sequence == 1 ? 1.25f : .7833333f;
            return Enumerable.Range(0, 22).Select(frame => start + frame / 60f)
                .Concat(new[] { 0, .12f, .4f, 2, 3, 5, duration }).Distinct().OrderBy(time => time).ToArray();
        }

        static IEnumerable<float> FaceTimes(int sequence, float duration, float[] sheetTimes)
        {
            float start = sequence == 1 ? 1.25f : .78f;
            float end = sequence == 1 ? 1.6f : 1.12f;
            return Enumerable.Range(0, Mathf.CeilToInt(duration * 30) + 1)
                .Select(frame => Mathf.Min(frame / 30f, duration))
                .Concat(Enumerable.Range(0, Mathf.CeilToInt((end - start) * 240) + 1)
                    .Select(frame => Mathf.Min(start + frame / 240f, end)))
                .Concat(sheetTimes).Distinct().OrderBy(time => time);
        }

        static void AppendFaceRow(StringBuilder rows, float seconds, FaceRow row,
            CharacterCombat source, CharacterCombat target)
        {
            rows.Append(FormattableString.Invariant($"{seconds:R},{row.contact.gap:R},"));
            foreach (var point in new[] { row.contact.sourcePoint, row.contact.bodyPoint, row.contact.boneOffset })
                rows.Append(FormattableString.Invariant($"{point.x:R},{point.y:R},{point.z:R},"));
            rows.Append(FormattableString.Invariant($"{row.attackerY:R},{row.receiverY:R},"));
            var a = source.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
            var b = target.Animator.GetBoneTransform(HumanBodyBones.Hips).position;
            rows.AppendLine(FormattableString.Invariant($"{a.x:R},{a.y:R},{a.z:R},{b.x:R},{b.y:R},{b.z:R}"));
        }

        static void DescribeFaceCapture(string stem, int sequence, Dictionary<float, FaceRow> records,
            List<string> scope)
        {
            float start = sequence == 1 ? 1.25f : .78f;
            float end = sequence == 1 ? 1.6f : 1.12f;
            var window = records.Where(row => row.Key >= start && row.Key <= end).ToArray();
            var best = window.OrderBy(row => row.Value.contact.gap).ThenBy(row => row.Key).First();
            var near = window.Where(row => row.Value.contact.gap <= .001f).ToArray();
            scope.Add(FormattableString.Invariant($"{stem} samples={records.Count}; ") +
                FormattableString.Invariant($"minimumGap={best.Value.contact.gap:R}m at {best.Key:R}s; ") +
                FormattableString.Invariant($"attackerMinimumY={records.Values.Min(row => row.attackerY):R}m; ") +
                FormattableString.Invariant($"receiverMinimumY={records.Values.Min(row => row.receiverY):R}m"));
            if (near.Length > 0)
                scope.Add(FormattableString.Invariant($"{stem} near-surface samples <=1mm: {near.Length}; ") +
                    FormattableString.Invariant($"first={near.Min(row => row.Key):R}s; last={near.Max(row => row.Key):R}s"));
        }
    }
}
