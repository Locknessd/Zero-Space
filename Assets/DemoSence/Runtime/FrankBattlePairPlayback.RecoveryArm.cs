using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget
{
    public sealed partial class FrankBattlePairPlayback
    {
        // During entry only, keep the wrist on a supported world trajectory instead
        // of allowing independent shoulder/elbow quaternion arcs to cross the floor.
        sealed class RecoveryArm
        {
            readonly Transform root;
            readonly Transform upper;
            readonly Transform elbow;
            readonly Transform hand;
            readonly Transform[] support;
            readonly Vector3[] sourceSupport;
            readonly Vector3[] targetSupport;
            Vector3 sourceHand;
            Vector3 sourceElbow;
            Quaternion sourceRotation;
            Vector3 targetHand;
            Vector3 targetElbow;
            Quaternion targetRotation;
            bool Valid => root && upper && elbow && hand;

            public RecoveryArm(Animator animator, bool left)
            {
                root = animator.transform;
                upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
                elbow = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                var points = new List<Transform>();
                if (Valid)
                {
                    sourceHand = hand.position;
                    sourceElbow = elbow.position;
                    sourceRotation = hand.rotation;
                    for (int index = 0; index < (int)HumanBodyBones.LastBone; index++)
                    {
                        var bone = animator.GetBoneTransform((HumanBodyBones)index);
                        if (bone && (bone == hand || bone.IsChildOf(hand))) points.Add(bone);
                    }
                }
                support = points.ToArray();
                sourceSupport = new Vector3[support.Length];
                targetSupport = new Vector3[support.Length];
                for (int index = 0; index < support.Length; index++)
                    sourceSupport[index] = support[index].position;
            }

            public void Rebase()
            {
                if (!Valid) return;
                sourceHand = root.InverseTransformPoint(sourceHand);
                sourceElbow = root.InverseTransformPoint(sourceElbow);
                sourceRotation = Quaternion.Inverse(root.rotation) * sourceRotation;
                for (int index = 0; index < support.Length; index++)
                    sourceSupport[index] = root.InverseTransformPoint(sourceSupport[index]);
            }

            public void CaptureController()
            {
                if (!Valid) return;
                targetHand = hand.position;
                targetElbow = elbow.position;
                targetRotation = hand.rotation;
                for (int index = 0; index < support.Length; index++)
                    targetSupport[index] = support[index].position;
            }

            public void Blend(float weight)
            {
                if (!Valid || weight <= 0 || weight >= 1) return;
                Vector3 wrist = Vector3.Lerp(root.TransformPoint(sourceHand), targetHand, weight);
                Vector3 hint = Vector3.Lerp(root.TransformPoint(sourceElbow), targetElbow, weight);
                Quaternion rotation = Quaternion.Slerp(root.rotation * sourceRotation, targetRotation, weight);
                hand.rotation = rotation;
                // Finger local rotations still use the regular entry blend. Maintain
                // their interpolated endpoint support heights while placing the wrist;
                // this prevents a rotating palm from driving its fingers underground.
                float supportOffset = 0;
                for (int index = 0; index < support.Length; index++)
                {
                    float height = Mathf.Lerp(root.TransformPoint(sourceSupport[index]).y,
                        targetSupport[index].y, weight);
                    float projected = wrist.y + support[index].position.y - hand.position.y;
                    supportOffset = Mathf.Max(supportOffset, height - projected);
                }
                wrist.y += supportOffset;
                Solve(wrist, hint);
                hand.rotation = rotation;
            }

            void Solve(Vector3 wrist, Vector3 hint)
            {
                Vector3 shoulder = upper.position;
                float first = Vector3.Distance(shoulder, elbow.position);
                float second = Vector3.Distance(elbow.position, hand.position);
                Vector3 axis = wrist - shoulder;
                float distance = axis.magnitude;
                if (first < .00001f || second < .00001f || distance < .00001f) return;
                axis /= distance;
                distance = Mathf.Clamp(distance, Mathf.Abs(first - second) + .00001f, first + second - .00001f);
                wrist = shoulder + axis * distance;
                float along = (first * first - second * second + distance * distance) / (2 * distance);
                float radius = Mathf.Sqrt(Mathf.Max(0, first * first - along * along));
                Vector3 center = shoulder + axis * along;
                Vector3 pole = Vector3.ProjectOnPlane(hint - center, axis);
                if (pole.sqrMagnitude < .0000001f)
                    pole = Vector3.ProjectOnPlane(elbow.position - center, axis);
                if (pole.sqrMagnitude < .0000001f)
                    pole = Vector3.Cross(axis, Vector3.right);
                if (pole.sqrMagnitude < .0000001f)
                    pole = Vector3.Cross(axis, Vector3.forward);
                pole.Normalize();
                pole = SupportedPole(pole, axis, center, radius, hint.y);
                Vector3 bend = center + pole * radius;
                upper.rotation = Quaternion.FromToRotation(elbow.position - shoulder, bend - shoulder) * upper.rotation;
                elbow.rotation = Quaternion.FromToRotation(hand.position - elbow.position,
                    wrist - elbow.position) * elbow.rotation;
            }

            static Vector3 SupportedPole(Vector3 pole, Vector3 axis, Vector3 center, float radius, float height)
            {
                Vector3 up = Vector3.ProjectOnPlane(Vector3.up, axis);
                float verticalRange = up.magnitude * radius;
                if (verticalRange <= .000001f || center.y + pole.y * radius >= height) return pole;
                up.Normalize();
                float minimum = Mathf.Clamp((height - center.y) / verticalRange, -1, 1);
                Vector3 side = pole - up * Vector3.Dot(pole, up);
                if (side.sqrMagnitude < .0000001f) side = Vector3.Cross(axis, up);
                return up * minimum + side.normalized * Mathf.Sqrt(Mathf.Max(0, 1 - minimum * minimum));
            }
        }
    }
}
