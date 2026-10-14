using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget
{
    public sealed partial class FrankTestDriver
    {
        // Legacy drivers keep their reviewed calibration. Other avatars use the same anatomical palm frames.
        void BindHumanoid(Animator character)
        {
            var source = new Reference(pose.driver.transform, pose.sourceHumanAvatar);
            var target = new Reference(character.transform, character.avatar);
            pose.targetHips = target.Bone("Hips");
            foreach (var limb in pose.limbs)
            {
                string human = source.Human(limb.sourceEnd);
                bool arm = human.EndsWith("Hand", StringComparison.Ordinal);
                string side = human.StartsWith("Left", StringComparison.Ordinal) ? "Left" : "Right";
                limb.upper = target.Bone(side + (arm ? "UpperArm" : "UpperLeg"));
                limb.middle = target.Bone(side + (arm ? "LowerArm" : "LowerLeg"));
                limb.end = target.Bone(human);
                limb.middleRest = target.RestPosition(limb.middle);
                limb.endRest = target.RestPosition(limb.end);
                limb.rotationOffset = Quaternion.Inverse(source.Rotation(limb.sourceEnd)) * target.Rotation(limb.end);
                if (!arm)
                {
                    if (source.TryBone(side + "Toes", out var sourceToe) &&
                        target.TryBone(side + "Toes", out var targetToe))
                    {
                        limb.sourceToeContact = sourceToe;
                        limb.targetToeContact = targetToe;
                    }
                    continue;
                }
                limb.maximumStretch = 1.45f;
                limb.targetKnuckle = target.Bone(side + "MiddleProximal");
                limb.targetFingerJoint = target.Bone(side + "MiddleIntermediate");
                var sourceRing = source.Bone(side + "RingProximal");
                var targetRing = target.Bone(side + "RingProximal");
                var sf = Palm(source, limb.sourceEnd, limb.sourceKnuckle, sourceRing);
                var tf = Palm(target, limb.end, limb.targetKnuckle, targetRing);
                limb.rotationOffset = Quaternion.Inverse(source.Rotation(limb.sourceEnd)) * sf *
                    Quaternion.Inverse(tf) * target.Rotation(limb.end);
            }
            var mappedFingers = new List<FrankPoseRetarget.Finger>();
            foreach (var finger in pose.fingers)
            {
                if (!target.TryBone(source.Human(finger.sourceProximal), out var proximal) ||
                    !target.TryBone(source.Human(finger.sourceIntermediate), out var intermediate) ||
                    !target.TryBone(source.Human(finger.sourceDistal), out var distal))
                    continue;
                finger.proximal = proximal;
                finger.intermediate = intermediate;
                finger.distal = distal;
                mappedFingers.Add(finger);
            }
            pose.fingers = mappedFingers.ToArray();
        }

        static Quaternion Palm(Reference reference, Transform wrist, Transform middle, Transform ring)
        {
            Vector3 along = reference.Position(middle) - reference.Position(wrist);
            Vector3 across = reference.Position(ring) - reference.Position(middle);
            return Quaternion.LookRotation(along, Vector3.Cross(along, across));
        }

        sealed class Reference
        {
            readonly Transform root;
            readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
            readonly Dictionary<string, string> human = new Dictionary<string, string>();
            readonly Dictionary<string, SkeletonBone> rest = new Dictionary<string, SkeletonBone>();

            public Reference(Transform root, Avatar avatar)
            {
                this.root = root;
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    bones.TryAdd(t.name, t);
                foreach (var h in avatar.humanDescription.human)
                    human[h.humanName.Replace(" ", "")] = h.boneName;
                foreach (var s in avatar.humanDescription.skeleton)
                    rest.TryAdd(s.name, s);
            }

            public Transform Bone(string name) => bones[human[name]];
            public bool TryBone(string name, out Transform bone)
            {
                bone = null;
                return human.TryGetValue(name, out var mapped) && bones.TryGetValue(mapped, out bone);
            }
            public string Human(Transform bone)
            {
                foreach (var entry in human)
                    if (entry.Value == bone.name)
                        return entry.Key;
                throw new InvalidOperationException("Unmapped source human bone: " + bone.name);
            }

            public Vector3 RestPosition(Transform bone) => rest[bone.name].position;
            public Quaternion Rotation(Transform bone) => Matrix(bone).rotation;
            public Vector3 Position(Transform bone) => Matrix(bone).MultiplyPoint3x4(Vector3.zero);

            Matrix4x4 Matrix(Transform bone)
            {
                var matrix = Matrix4x4.identity;
                while (bone && bone != root)
                {
                    var s = rest[bone.name];
                    matrix = Matrix4x4.TRS(s.position, s.rotation, s.scale) * matrix;
                    bone = bone.parent;
                }
                return matrix;
            }
        }
    }
}
