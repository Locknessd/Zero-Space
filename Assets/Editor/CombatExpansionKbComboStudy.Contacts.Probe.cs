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
            // Explicit target bones avoid treating a torso root and all limbs as one region.
            public SkinRegionProbe(CharacterCombat source, HumanBodyBones sourceRoot,
                CharacterCombat target, HumanBodyBones targetAnchor, HumanBodyBones[] exactTargetBones)
            {
                this.source = source;
                this.target = target;
                this.sourceRoot = sourceRoot;
                targetRoot = targetAnchor;
                var summary = new List<string>();
                try
                {
                    SelectRegion(source, sourceRoot, sourceSkins, summary);
                    MappedBone(target, targetAnchor);
                    var region = new HashSet<Transform>();
                    foreach (var bone in exactTargetBones)
                    {
                        var node = target.Animator.GetBoneTransform(bone);
                        if (!node)
                            continue;
                        region.Add(MappedBone(target, bone));
                        summary.Add("Exact target torso bone: " + target.name + "/" + bone);
                    }
                    if (region.Count == 0)
                        throw new InvalidOperationException("No mapped torso bones.");
                    SelectExactTorso(target, region, targetSkins, summary);
                    summary.Insert(0, "Source hand and descendants; target exact axial bones only, no descendants. " +
                        "All triangle vertices require >=0.5 summed region influence; visible BodySkins only. " +
                        "Exact current skinned triangle distance; unsigned zero may be penetration. " +
                        "Torso local anchors are expressed relative to Hips, not nearest anatomical bone.");
                    SelectionSummary = string.Join("\n", summary);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            static void SelectExactTorso(CharacterCombat fighter, HashSet<Transform> region,
                List<RegionSkin> selected, List<string> summary)
            {
                foreach (var skin in BodySkins(fighter))
                {
                    var mesh = skin.sharedMesh;
                    var vertices = RegionVertices(skin, region);
                    var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                    try
                    {
                        skin.BakeMesh(baked, false);
                        if (baked.vertexCount != mesh.vertexCount)
                            throw new InvalidOperationException("Torso bake vertex count mismatch: " + skin.name);
                        int[] triangles = baked.triangles;
                        if (triangles.Length % 3 != 0)
                            throw new InvalidOperationException("Invalid torso triangle count: " + skin.name);
                        var indices = new List<int>();
                        for (int face = 0; face < triangles.Length; face += 3)
                        {
                            bool include = true;
                            for (int corner = 0; corner < 3; corner++)
                            {
                                int index = triangles[face + corner];
                                if (index < 0 || index >= vertices.Length)
                                    throw new InvalidOperationException("Invalid torso triangle index.");
                                include &= vertices[index];
                            }
                            if (!include)
                                continue;
                            for (int corner = 0; corner < 3; corner++)
                                indices.Add(triangles[face + corner]);
                        }
                        summary.Add(fighter.name + "/Torso: renderer=" + skin.name + "; mesh=" +
                            CombatExpansionInventory.Identity(mesh) + "; selected/total triangles=" +
                            indices.Count / 3 + "/" + triangles.Length / 3);
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
                    throw new InvalidOperationException("No exact torso triangles for " + fighter.name);
            }
        }
    }
}
