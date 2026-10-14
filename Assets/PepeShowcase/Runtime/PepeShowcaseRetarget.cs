using System;
using System.Collections.Generic;
using FrankRetarget;
using UnityEngine;

namespace ZeroSpace.Showcase
{
    public static class PepeShowcaseRetarget
    {
        public static void Adapt(FrankTestDriver template, Animator target)
        {
            var pose = template.pose;
            template.characterScale = target.transform.localScale;
            template.hips = Path(target, HumanBodyBones.Hips);
            template.targetLimbs = new FrankTestDriver.TargetLimb[pose.limbs.Length];
            for (int i = 0; i < pose.limbs.Length; i++)
            {
                var limb = pose.limbs[i];
                var upper = Bone(target, pose.sourceHumanAvatar, limb.sourceUpper);
                var middle = Bone(target, pose.sourceHumanAvatar, limb.sourceMiddle);
                var end = Bone(target, pose.sourceHumanAvatar, limb.sourceEnd);
                if (!upper || !middle || !end)
                    throw new ArgumentException("The custom Humanoid needs both arms and legs.");
                var knuckle = Bone(target, pose.sourceHumanAvatar, limb.sourceKnuckle);
                var joint = Bone(target, pose.sourceHumanAvatar, limb.sourceFingerJoint);
                template.targetLimbs[i] = new FrankTestDriver.TargetLimb
                {
                    upper = Path(target.transform, upper),
                    middle = Path(target.transform, middle),
                    end = Path(target.transform, end),
                    knuckle = knuckle && joint ? Path(target.transform, knuckle) : "",
                    joint = knuckle && joint ? Path(target.transform, joint) : ""
                };
                if (!knuckle || !joint)
                {
                    limb.sourceKnuckle = null;
                    limb.sourceFingerJoint = null;
                }
                limb.middleRest = middle.localPosition;
                limb.endRest = end.localPosition;
                limb.positionOffset = Vector3.zero;
                limb.rotationOffset = Quaternion.Inverse(RestRotation(pose.driver.transform,
                    pose.sourceHumanAvatar, limb.sourceEnd)) * RestRotation(target.transform, target.avatar, end);
            }
            var fingers = new List<FrankPoseRetarget.Finger>();
            var paths = new List<FrankTestDriver.TargetFinger>();
            foreach (var finger in pose.fingers)
            {
                var a = Bone(target, pose.sourceHumanAvatar, finger.sourceProximal);
                var b = Bone(target, pose.sourceHumanAvatar, finger.sourceIntermediate);
                var c = Bone(target, pose.sourceHumanAvatar, finger.sourceDistal);
                if (!a || !b || !c) continue;
                fingers.Add(finger);
                paths.Add(new FrankTestDriver.TargetFinger
                {
                    proximal = Path(target.transform, a),
                    intermediate = Path(target.transform, b),
                    distal = Path(target.transform, c)
                });
            }
            pose.fingers = fingers.ToArray();
            template.targetFingers = paths.ToArray();
            float sourceHeight = pose.sourceHips.position.y - template.transform.position.y;
            float targetHeight = target.GetBoneTransform(HumanBodyBones.Hips).position.y - target.transform.position.y;
            if (sourceHeight > .01f && targetHeight > .01f)
                template.transform.localScale *= targetHeight / sourceHeight;
        }

        static Transform Bone(Animator target, Avatar source, Transform original)
        {
            if (!original) return null;
            foreach (var bone in source.humanDescription.human)
                if (bone.boneName == original.name && Enum.TryParse(bone.humanName.Replace(" ", ""),
                    out HumanBodyBones human) && human < HumanBodyBones.LastBone)
                    return target.GetBoneTransform(human);
            return null;
        }

        static string Path(Animator animator, HumanBodyBones bone) =>
            Path(animator.transform, animator.GetBoneTransform(bone));

        static string Path(Transform root, Transform bone)
        {
            string result = bone.name;
            while (bone.parent && bone.parent != root)
            {
                bone = bone.parent;
                result = bone.name + "/" + result;
            }
            return result;
        }

        static Quaternion RestRotation(Transform root, Avatar avatar, Transform bone)
        {
            var rotations = new Dictionary<string, Quaternion>();
            foreach (var entry in avatar.humanDescription.skeleton)
                rotations[entry.name] = entry.rotation;
            Quaternion result = Quaternion.identity;
            while (bone && bone != root)
            {
                result = (rotations.TryGetValue(bone.name, out var value) ? value : bone.localRotation) * result;
                bone = bone.parent;
            }
            return root.rotation * result;
        }
    }
}
