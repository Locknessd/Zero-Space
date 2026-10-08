using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeDenseStudy
    {
        public sealed partial class SkinRegionProbe
        {
            sealed class RegionSkin
            {
                public SkinnedMeshRenderer skin;
                public Mesh mesh;
                public Mesh baked;
                public int vertexCount;
                public int[] triangles;
            }

            static void SelectRegion(CharacterCombat fighter, HumanBodyBones root,
                List<RegionSkin> selected, List<string> summary)
            {
                SelectRegion(fighter, root, Selection.BoneAndDescendants, selected, summary);
            }

            static void SelectRegion(CharacterCombat fighter, HumanBodyBones root, Selection policy,
                List<RegionSkin> selected, List<string> summary)
            {
                var region = RegionBones(fighter, root, policy, summary);
                foreach (var skin in BodySkins(fighter))
                {
                    Mesh mesh = skin.sharedMesh;
                    bool[] vertices = RegionVertices(skin, region);
                    Mesh baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    try
                    {
                        // BakeMesh exposes topology even when the imported mesh is not runtime-readable.
                        skin.BakeMesh(baked, false);
                        if (baked.vertexCount != mesh.vertexCount)
                            throw new InvalidOperationException("Region bake vertex count mismatch: " + skin.name);
                        int[] triangles = baked.triangles;
                        if (triangles.Length % 3 != 0)
                            throw new InvalidOperationException("Invalid region triangle count: " + skin.name);
                        var indices = new List<int>();
                        for (int face = 0; face < triangles.Length; face += 3)
                        {
                            bool include = true;
                            for (int corner = 0; corner < 3; corner++)
                            {
                                int index = triangles[face + corner];
                                if (index < 0 || index >= vertices.Length)
                                    throw new InvalidOperationException("Invalid region triangle index: " + skin.name);
                                include &= vertices[index];
                            }
                            if (!include)
                                continue;
                            for (int corner = 0; corner < 3; corner++)
                                indices.Add(triangles[face + corner]);
                        }
                        summary.Add(fighter.name + "/" + root + "/" + policy + ": renderer=" + skin.name +
                            "; mesh=" + CombatExpansionInventory.Identity(mesh) +
                            "; selected/total triangles=" + indices.Count / 3 + "/" + triangles.Length / 3);
                        if (indices.Count == 0)
                            continue;
                        selected.Add(new RegionSkin
                        {
                            skin = skin,
                            mesh = mesh,
                            baked = baked,
                            vertexCount = mesh.vertexCount,
                            triangles = indices.ToArray()
                        });
                        baked = null;
                    }
                    finally
                    {
                        if (baked)
                            Object.DestroyImmediate(baked);
                    }
                }
                if (selected.Count == 0)
                    throw new InvalidOperationException("No selected skin triangles for " + fighter.name + "/" + root);
            }

            static bool[] RegionVertices(SkinnedMeshRenderer skin, HashSet<Transform> region)
            {
                Mesh mesh = skin.sharedMesh;
                Transform[] bones = skin.bones;
                if (bones.Length == 0 || mesh.bindposes.Length != bones.Length)
                    throw new InvalidOperationException("Missing region skin binding: " + skin.name);
                // Both NativeArray views are owned by the mesh and must not be disposed here.
                var counts = mesh.GetBonesPerVertex();
                var weights = mesh.GetAllBoneWeights();
                if (counts.Length != mesh.vertexCount)
                    throw new InvalidOperationException("Missing region vertex weights: " + skin.name);
                var selected = new bool[counts.Length];
                int cursor = 0;
                for (int vertex = 0; vertex < counts.Length; vertex++)
                {
                    int count = counts[vertex];
                    if (count == 0 || count > weights.Length - cursor)
                        throw new InvalidOperationException("Invalid region influence count: " + skin.name);
                    float influence = 0;
                    float total = 0;
                    for (int index = 0; index < count; index++)
                    {
                        var weight = weights[cursor++];
                        if (!float.IsFinite(weight.weight) || weight.weight < 0 ||
                            weight.boneIndex < 0 || weight.boneIndex >= bones.Length)
                            throw new InvalidOperationException("Invalid region bone weight: " + skin.name);
                        if (weight.weight == 0)
                            continue;
                        Transform bone = bones[weight.boneIndex];
                        if (!bone)
                            throw new InvalidOperationException("Missing weighted region bone: " + skin.name);
                        total += weight.weight;
                        if (region.Contains(bone))
                            influence += weight.weight;
                    }
                    if (!float.IsFinite(total) || total <= 0 || !float.IsFinite(influence))
                        throw new InvalidOperationException("Invalid region weight sum: " + skin.name);
                    selected[vertex] = influence >= .5f;
                }
                if (cursor != weights.Length)
                    throw new InvalidOperationException("Region skin weight count mismatch: " + skin.name);
                return selected;
            }
        }
    }
}
