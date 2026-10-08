using System;
using System.Collections.Generic;
using System.Linq;
using FrankRetarget;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeDenseStudy
    {
        static SkinnedMeshRenderer[] BodySkins(CharacterCombat fighter) =>
            fighter.Animator.GetComponentsInChildren<SkinnedMeshRenderer>()
                .Where(skin => Visible(skin) && skin.sharedMesh && skin.sharedMesh.vertexCount >= 1000).ToArray();

        static Surface Receiver(CharacterCombat fighter, List<string> geometry, SkinnedMeshRenderer[] skins = null)
        {
            var result = new Surface();
            foreach (var skin in skins ?? BodySkins(fighter))
                AddBodyMesh(result, skin, geometry);
            result.Build();
            // A baked avatar larger than its skeleton is a space conversion failure, not a contact.
            var bones = Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>()
                .Where(b => b < HumanBodyBones.LastBone).Select(fighter.Animator.GetBoneTransform)
                .Where(t => t).ToArray();
            var bounds = new Bounds(bones[0].position, Vector3.zero);
            foreach (var bone in bones)
                bounds.Encapsulate(bone.position);
            geometry.Add(fighter.name + " body=" + result.Bounds + "; skeleton=" + bounds);
            if (result.Bounds.size.magnitude > bounds.size.magnitude * 2 + .5f)
                throw new InvalidOperationException("Baked body exceeds evaluated skeleton bounds: " + fighter.name);
            return result;
        }

        static bool Visible(Renderer renderer) => renderer && renderer.enabled && renderer.gameObject.activeInHierarchy;

        static void AddBodyMesh(Surface surface, SkinnedMeshRenderer skin, List<string> geometry)
        {
            var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                skin.BakeMesh(baked, false);
                var world = RenderedVertices(skin, baked, geometry);
                var triangles = baked.triangles;
                int offset = surface.vertices.Count;
                foreach (var vertex in world)
                {
                    if (!float.IsFinite(vertex.x) || !float.IsFinite(vertex.y) || !float.IsFinite(vertex.z))
                        throw new InvalidOperationException("Nonfinite body vertex: " + skin.name);
                    surface.vertices.Add(vertex);
                }
                foreach (var triangle in triangles)
                    surface.triangles.Add(offset + triangle);
                geometry.Add(skin.name + ": " + triangles.Length / 3 + " faces; scale=" + skin.transform.lossyScale +
                    "; space=explicit current bone * bindpose world skinning");
            }
            finally
            {
                Object.DestroyImmediate(baked);
            }
        }

        static Transform NearestAnchor(CharacterCombat receiver, Vector3 point, out HumanBodyBones chosen)
        {
            // Exclude fingers and face helpers: larger anatomical anchors are stable across avatar detail levels.
            var choices = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest,
                HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
                HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm,
                HumanBodyBones.RightLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.RightLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot };
            float best = float.PositiveInfinity;
            Transform result = null;
            chosen = HumanBodyBones.Hips;
            foreach (var bone in choices)
            {
                var anchor = receiver.Animator.GetBoneTransform(bone);
                if (!anchor)
                    continue;
                float distance = (anchor.position - point).sqrMagnitude;
                if (distance >= best)
                    continue;
                best = distance;
                result = anchor;
                chosen = bone;
            }
            if (!result)
                throw new InvalidOperationException("Receiver has no usable contact anchor.");
            return result;
        }
    }
}
