using System;
using System.Collections.Generic;
using System.Linq;
using FrankRetarget;
using UnityEngine;
using Object = UnityEngine.Object;

public static partial class BattlePresentationContactSetup
{
    static Surface Receiver(CharacterCombat fighter, List<string> geometry)
    {
        var result = new Surface();
        foreach (var skin in fighter.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!Visible(skin) || !skin.sharedMesh || skin.sharedMesh.vertexCount < 1000) continue;
            AddMesh(result, skin, null, geometry);
        }
        result.Build();
        // A baked avatar larger than its skeleton is a space conversion failure, not a contact.
        var bones = Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>()
            .Where(b => b < HumanBodyBones.LastBone).Select(fighter.Animator.GetBoneTransform)
            .Where(t => t).ToArray();
        var bounds = new Bounds(bones[0].position, Vector3.zero);
        foreach (var bone in bones) bounds.Encapsulate(bone.position);
        geometry.Add(fighter.name + " body=" + result.Bounds + "; skeleton=" + bounds);
        if (result.Bounds.size.magnitude > bounds.size.magnitude * 2 + .5f)
            throw new InvalidOperationException("Baked body exceeds evaluated skeleton bounds: " + fighter.name);
        return result;
    }

    static Surface Striker(FrankBattlePairPlayback pair, CharacterCombat fighter, string selection,
        List<string> geometry)
    {
        var result = new Surface();
        if (selection == "Weapon" || selection == "Shield")
        {
            bool shield = selection == "Shield";
            foreach (var renderer in pair.AttackerActor.Pose.weaponRenderers)
            {
                if (!Visible(renderer) || renderer.name.IndexOf("case", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                if (renderer.name.IndexOf("gun", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;
                bool isShield = renderer.name.IndexOf("shield", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isShield == shield) AddMesh(result, renderer, null, geometry);
            }
        }
        else
        {
            if (!Enum.TryParse(selection, out HumanBodyBones bone) ||
                (bone != HumanBodyBones.LeftHand && bone != HumanBodyBones.RightHand &&
                bone != HumanBodyBones.LeftFoot && bone != HumanBodyBones.RightFoot &&
                bone != HumanBodyBones.LeftLowerArm && bone != HumanBodyBones.RightLowerArm &&
                bone != HumanBodyBones.LeftUpperLeg && bone != HumanBodyBones.RightUpperLeg &&
                bone != HumanBodyBones.LeftLowerLeg && bone != HumanBodyBones.RightLowerLeg))
                throw new InvalidOperationException("Unsupported authored striker: " + selection);
            var anchor = fighter.Animator.GetBoneTransform(bone);
            if (!anchor) throw new InvalidOperationException("Missing striker bone: " + selection);
            var excluded = bone == HumanBodyBones.LeftLowerArm
                ? fighter.Animator.GetBoneTransform(HumanBodyBones.LeftHand)
                : bone == HumanBodyBones.RightLowerArm ? fighter.Animator.GetBoneTransform(HumanBodyBones.RightHand)
                : bone == HumanBodyBones.LeftUpperLeg ? fighter.Animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg)
                : bone == HumanBodyBones.RightUpperLeg ? fighter.Animator.GetBoneTransform(HumanBodyBones.RightLowerLeg)
                : bone == HumanBodyBones.LeftLowerLeg ? fighter.Animator.GetBoneTransform(HumanBodyBones.LeftFoot)
                : bone == HumanBodyBones.RightLowerLeg ? fighter.Animator.GetBoneTransform(HumanBodyBones.RightFoot)
                : null;
            foreach (var skin in fighter.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (Visible(skin) && skin.sharedMesh && skin.sharedMesh.vertexCount >= 1000)
                    AddMesh(result, skin, anchor, geometry, excluded);
        }
        result.Build();
        return result;
    }

    static bool Visible(Renderer renderer) => renderer && renderer.enabled && renderer.gameObject.activeInHierarchy;

    static void AddMesh(Surface surface, Renderer renderer, Transform limb, List<string> geometry, Transform excluded = null)
    {
        Mesh baked = null;
        try
        {
            Mesh mesh;
            Matrix4x4 matrix;
            bool[] selected = null;
            Vector3[] rendered = null;
            if (renderer is SkinnedMeshRenderer skin)
            {
                if (!skin.sharedMesh) return;
                baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                // Bake only supplies topology and independent space diagnostics.
                // Contact vertices come directly from current bone matrices and original bind poses.
                skin.BakeMesh(baked, false);
                mesh = baked;
                matrix = Matrix4x4.identity;
                rendered = RenderedVertices(skin, baked, geometry);
                if (limb) selected = LimbVertices(skin, limb, excluded);
            }
            else
            {
                var filter = renderer.GetComponent<MeshFilter>();
                mesh = filter ? filter.sharedMesh : null;
                matrix = renderer.localToWorldMatrix;
            }
            if (!mesh) return;
            Vector3[] local;
            int[] triangles;
            if (!mesh.isReadable)
                ReadContactMesh(mesh, out local, out triangles);
            else
            {
                local = rendered ?? mesh.vertices;
                triangles = mesh.triangles;
            }
            int offset = surface.vertices.Count;
            foreach (var vertex in local)
            {
                Vector3 world = matrix.MultiplyPoint3x4(vertex);
                if (!float.IsFinite(world.x) || !float.IsFinite(world.y) || !float.IsFinite(world.z))
                    throw new InvalidOperationException("Nonfinite evaluated vertex: " + renderer.name);
                surface.vertices.Add(world);
            }
            int added = 0;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                if (selected != null && (!selected[triangles[i]] || !selected[triangles[i + 1]] ||
                    !selected[triangles[i + 2]])) continue;
                surface.triangles.Add(offset + triangles[i]);
                surface.triangles.Add(offset + triangles[i + 1]);
                surface.triangles.Add(offset + triangles[i + 2]);
                added++;
            }
            geometry.Add(renderer.name + ": " + added + " faces; scale=" + renderer.transform.lossyScale +
                "; space=" + (baked ? "explicit current bone * bindpose world skinning" : "static localToWorld"));
        }
        finally
        {
            if (baked) Object.DestroyImmediate(baked);
        }
    }

    static bool[] LimbVertices(SkinnedMeshRenderer skin, Transform limb, Transform excluded)
    {
        var bones = skin.bones;
        var weights = skin.sharedMesh.boneWeights;
        if (weights.Length != skin.sharedMesh.vertexCount)
            throw new InvalidOperationException("Limb mesh has no readable vertex weights: " + skin.name);
        var allowed = bones.Select(b => b && (b == limb || b.IsChildOf(limb)) &&
            (!excluded || b != excluded && !b.IsChildOf(excluded))).ToArray();
        var result = new bool[weights.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            var weight = weights[i];
            float total = Weight(weight.boneIndex0, weight.weight0) + Weight(weight.boneIndex1, weight.weight1) +
                Weight(weight.boneIndex2, weight.weight2) + Weight(weight.boneIndex3, weight.weight3);
            result[i] = total >= .5f;
        }
        return result;

        float Weight(int bone, float value) => bone >= 0 && bone < allowed.Length && allowed[bone] ? value : 0;
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
            if (!anchor) continue;
            float distance = (anchor.position - point).sqrMagnitude;
            if (distance >= best) continue;
            best = distance;
            result = anchor;
            chosen = bone;
        }
        if (!result) throw new InvalidOperationException("Receiver has no usable contact anchor.");
        return result;
    }
}
