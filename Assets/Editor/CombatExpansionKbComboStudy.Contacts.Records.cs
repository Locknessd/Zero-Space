using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        [Serializable]
        sealed class ContactsReport
        {
            public string status = "RUNNING_PROVISIONAL";
            public string scope = ContactsScope;
            public string startedUtc = DateTime.UtcNow.ToString("O");
            public string updatedUtc;
            public string completedUtc;
            public string failure;
            public int completedCases;
            public SourceRecord[] sources;
            public List<ContactsCase> cases = new List<ContactsCase>();
        }

        [Serializable]
        sealed class ContactsCase
        {
            public string status = "RUNNING_PROVISIONAL";
            public string startedUtc = DateTime.UtcNow.ToString("O");
            public string completedUtc;
            public string name;
            public string attacker;
            public string receiver;
            public string phase = "initialize";
            public string failure;
            public int assignment;
            public int laneSign;
            public float spacing;
            public float duration;
            public int samples;
            public int reverseSamples;
            public float worstGapReseekMetres;
            public float worstClearanceReseekMetres;
            public float worstPoseReseekMetres;
            public float worstPoseReseekDegrees;
            public float minimumAttackerY = float.MaxValue;
            public float minimumReceiverY = float.MaxValue;
            public string trajectory;
            public string backwardsSeek;
            public string[] sheets;
            public List<string> selection = new List<string>();
            public List<ContactEvidence> best = new List<ContactEvidence>();
            public List<ContactContext> sheetContext = new List<ContactContext>();
        }

        [Serializable]
        sealed class ContactContext
        {
            public float seconds;
            public string label;
        }

        [Serializable]
        sealed class ContactEvidence
        {
            public string status = "PROVISIONAL_NEAREST_SAMPLE_NOT_ACCEPTED";
            public int strike;
            public string limb;
            public string surface;
            public string targetAnchorBone;
            public float seconds;
            public float pairClock;
            public float gap;
            public Vector3 sourceWorld;
            public Vector3 targetWorld;
            public Vector3 sourceBoneLocal;
            public Vector3 targetBoneLocal;
            public Vector3 handPivot;
            public Vector3 targetPivot;
            public Vector3 handVelocity;
            public Vector3 handDirection;
            public Vector3 relativeVelocity;
            public float closingSpeed;
            public bool velocityAvailable;
            public float movementInterval;
            public float attackerMinimumY;
            public float receiverMinimumY;
        }

        sealed class ContactSnapshot
        {
            public float seconds;
            public PoseSample[] poses;
            public ContactEvidence[] contacts;
        }

        static ContactEvidence ContactRow(float seconds, float clock, int strike, string surface,
            CombatExpansionAxeDenseStudy.BodyContact contact, Transform hand, Transform anchor,
            float attackerY, float receiverY, ContactEvidence previous)
        {
            var row = new ContactEvidence
            {
                strike = strike,
                limb = strike == 1 ? "LeftHand" : "RightHand",
                surface = surface,
                targetAnchorBone = contact.nearestBone.ToString(),
                seconds = seconds,
                pairClock = clock,
                gap = contact.gap,
                sourceWorld = contact.sourcePoint,
                targetWorld = contact.bodyPoint,
                sourceBoneLocal = hand.InverseTransformPoint(contact.sourcePoint),
                targetBoneLocal = contact.boneOffset,
                handPivot = hand.position,
                targetPivot = anchor.position,
                attackerMinimumY = attackerY,
                receiverMinimumY = receiverY
            };
            if (!Finite(row.sourceBoneLocal) || !float.IsFinite(attackerY) || !float.IsFinite(receiverY))
                throw new InvalidOperationException("Nonfinite contact anchors or body clearance.");
            if (previous != null && previous.strike == strike && seconds > previous.seconds)
            {
                row.movementInterval = seconds - previous.seconds;
                row.velocityAvailable = true;
                row.handVelocity = (hand.position - previous.handPivot) / row.movementInterval;
                row.handDirection = row.handVelocity.normalized;
                row.relativeVelocity = ((hand.position - anchor.position) -
                    (previous.handPivot - previous.targetPivot)) / row.movementInterval;
                row.closingSpeed = Vector3.Dot(row.relativeVelocity, (anchor.position - hand.position).normalized);
            }
            return row;
        }

        static string ContactCsv(IEnumerable<ContactEvidence> rows)
        {
            var text = new StringBuilder("seconds,pairClock,strike,limb,targetSurface,targetAnchorBone,gapM," +
                "sourceWorldX,sourceWorldY,sourceWorldZ,targetWorldX,targetWorldY,targetWorldZ," +
                "sourceBoneLocalX,sourceBoneLocalY,sourceBoneLocalZ,targetBoneLocalX,targetBoneLocalY,targetBoneLocalZ," +
                "handPivotX,handPivotY,handPivotZ,targetPivotX,targetPivotY,targetPivotZ," +
                "handVelocityX,handVelocityY,handVelocityZ,handDirectionX,handDirectionY,handDirectionZ," +
                "relativeVelocityX,relativeVelocityY,relativeVelocityZ,closingSpeedMps,velocityAvailable," +
                "movementInterval,attackerMinimumY,receiverMinimumY,status\n");
            foreach (var row in rows)
            {
                text.Append(FormattableString.Invariant($"{row.seconds:R},{row.pairClock:R},{row.strike},"));
                text.Append(row.limb + "," + row.surface + "," + row.targetAnchorBone + "," +
                    row.gap.ToString("R", CultureInfo.InvariantCulture) + ",");
                foreach (var vector in new[] { row.sourceWorld, row.targetWorld, row.sourceBoneLocal,
                    row.targetBoneLocal, row.handPivot, row.targetPivot, row.handVelocity, row.handDirection,
                    row.relativeVelocity })
                    text.Append(FormattableString.Invariant($"{vector.x:R},{vector.y:R},{vector.z:R},"));
                text.Append(FormattableString.Invariant($"{row.closingSpeed:R},{row.velocityAvailable},"));
                text.Append(FormattableString.Invariant($"{row.movementInterval:R},{row.attackerMinimumY:R},"));
                text.AppendLine(row.receiverMinimumY.ToString("R", CultureInfo.InvariantCulture) + "," + row.status);
            }
            return text.ToString();
        }
    }
}
