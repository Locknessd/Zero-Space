using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeDenseStudy
    {
        public sealed partial class SkinRegionProbe
        {
            static readonly HumanBodyBones[] NonTorsoBranches =
            {
                HumanBodyBones.Neck, HumanBodyBones.Head,
                HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
                HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
                HumanBodyBones.LeftHand, HumanBodyBones.RightHand
            };

            static HashSet<Transform> RegionBones(CharacterCombat fighter, HumanBodyBones root,
                Selection policy, List<string> summary)
            {
                Transform anchor = MappedBone(fighter, root);
                var region = new HashSet<Transform>(anchor.GetComponentsInChildren<Transform>(true));
                if (policy == Selection.TorsoWithoutHeadNeckOrArms)
                {
                    if (root != HumanBodyBones.Spine && root != HumanBodyBones.Chest &&
                        root != HumanBodyBones.UpperChest)
                        throw new ArgumentException("Bounded torso requires Spine, Chest or UpperChest.");
                    // These humanoid mappings are mandatory; a missing branch must not broaden selection.
                    MappedBone(fighter, HumanBodyBones.Head);
                    MappedBone(fighter, HumanBodyBones.LeftUpperArm);
                    MappedBone(fighter, HumanBodyBones.RightUpperArm);
                    foreach (var bone in NonTorsoBranches)
                    {
                        var excluded = fighter.Animator.GetBoneTransform(bone);
                        if (!excluded)
                            continue;
                        region.RemoveWhere(node => node == excluded || node.IsChildOf(excluded));
                    }
                    if (!region.Contains(anchor))
                        throw new InvalidOperationException("Torso anchor is inside an excluded anatomical branch.");
                }
                else if (policy != Selection.BoneAndDescendants)
                    throw new ArgumentOutOfRangeException(nameof(policy));
                var paths = new List<string>();
                foreach (var bone in region)
                    paths.Add(RegionBonePath(bone, fighter.Animator.transform));
                paths.Sort(StringComparer.Ordinal);
                summary.Add(fighter.name + "/" + root + ": policy=" + policy +
                    "; retained bone set (Animator-relative paths)=" + string.Join(" | ", paths));
                return region;
            }

            static string RegionBonePath(Transform node, Transform animator)
            {
                var path = new List<string>();
                while (node && node != animator)
                {
                    path.Add(node.name);
                    node = node.parent;
                }
                path.Reverse();
                return string.Join("/", path);
            }
        }
    }
}
