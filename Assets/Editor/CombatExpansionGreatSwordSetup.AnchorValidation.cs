using System;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordSetup
    {
        sealed class ForwardContact
        {
            public BattlePresentationContactSetup.BladeContactProbe probe;
            public Matrix4x4 boneFrame;
        }

        sealed class AnchorDistances
        {
            public float forwardBlade;
            public float forwardBody;
            public float reverseBlade;
            public float reverseBody;

            public bool Valid => ContactDistanceWithin(forwardBlade, .12f) &&
                ContactDistanceWithin(reverseBlade, .12f) && ContactDistanceWithin(forwardBody, .025f) &&
                ContactDistanceWithin(reverseBody, .025f);

            public float WorstBlade => Mathf.Max(forwardBlade, reverseBlade);

            public override string ToString()
            {
                return FormattableString.Invariant(
                    $"forwardBladeGap={forwardBlade:R}m forwardBodyGap={forwardBody:R}m ") +
                    FormattableString.Invariant(
                        $"reverseBladeGap={reverseBlade:R}m reverseBodyGap={reverseBody:R}m");
            }
        }

        static void SelectCommonAnchor(BattleSfxBank.Cue cue, BattleSfxBank.ContactAnchor anchor,
            ForwardContact forward, BattlePresentationContactSetup.BladeContactProbe reverse,
            Transform reverseBone, Vector3 reverseContact, string label, StringBuilder report)
        {
            if (forward == null || forward.probe == null)
                throw new InvalidOperationException(label + " missing forward contact geometry.");
            var reverseOffset = reverseBone.InverseTransformPoint(reverseContact);
            if (!Finite(reverseOffset))
                throw new InvalidOperationException(label + " invalid reverse contact offset.");
            AnchorDistances best = null;
            var bestOffset = anchor.offset;
            float bestFraction = 0;
            var candidates = new StringBuilder();
            for (int i = 0; i <= 20; i++)
            {
                float fraction = i / 20f;
                var offset = Vector3.Lerp(anchor.offset, reverseOffset, fraction);
                var forwardWorld = forward.boneFrame.MultiplyPoint3x4(offset);
                var reverseWorld = reverseBone.TransformPoint(offset);
                var distances = new AnchorDistances
                {
                    forwardBlade = forward.probe.BladeDistance(forwardWorld),
                    forwardBody = forward.probe.BodyDistance(forwardWorld),
                    reverseBlade = reverse.BladeDistance(reverseWorld),
                    reverseBody = reverse.BodyDistance(reverseWorld)
                };
                candidates.AppendLine(FormattableString.Invariant($"fraction={fraction:R} ") + distances);
                if (!Finite(offset) || !Finite(forwardWorld) || !Finite(reverseWorld) || !distances.Valid ||
                    best != null && distances.WorstBlade >= best.WorstBlade)
                    continue;
                best = distances;
                bestOffset = offset;
                bestFraction = fraction;
            }
            if (best == null)
                throw new InvalidOperationException(label + " has no common valid anchor among 21 candidates.\n" +
                    candidates);
            anchor.offset = bestOffset;
            if (cue.avatarContacts[0] == anchor)
            {
                cue.contactBone = anchor.bone;
                cue.contactOffset = bestOffset;
            }
            report.AppendLine(label + FormattableString.Invariant(
                $" selectedCommonAnchor bone={anchor.bone} fraction={bestFraction:R} ") + best +
                FormattableString.Invariant($" offset=({bestOffset.x:R},{bestOffset.y:R},{bestOffset.z:R})"));
        }
    }
}
