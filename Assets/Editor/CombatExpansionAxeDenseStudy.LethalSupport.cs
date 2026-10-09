using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeDenseStudy
    {
        // Shares the existing exact rendered-world skinning convention; no source mesh or import changes.
        public sealed class LethalSupportProbe : IDisposable
        {
            public static readonly HumanBodyBones[] Regions =
            {
                HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
                HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.Head
            };

            sealed class Entry
            {
                public SkinnedMeshRenderer skin;
                public Mesh source, baked;
                public int[][] selected;
            }

            readonly List<Entry> entries = new List<Entry>();
            public string SelectionSummary { get; private set; }
            public float MinimumY { get; private set; }

            public LethalSupportProbe(CharacterCombat fighter)
            {
                try
                {
                    var regionBones = Regions.Select(bone =>
                    {
                        var root = fighter.Animator.GetBoneTransform(bone);
                        if (!root)
                            throw new InvalidOperationException("Missing support bone: " + bone);
                        return new HashSet<Transform>(root.GetComponentsInChildren<Transform>(true));
                    }).ToArray();
                    var counts = new int[Regions.Length];
                    foreach (var skin in BodySkins(fighter))
                    {
                        var entry = new Entry
                        {
                            skin = skin,
                            source = skin.sharedMesh,
                            baked = new Mesh { hideFlags = HideFlags.HideAndDontSave },
                            selected = new int[Regions.Length][]
                        };
                        entries.Add(entry);
                        skin.BakeMesh(entry.baked, false);
                        var triangles = entry.baked.triangles;
                        var influences = entry.source.GetBonesPerVertex();
                        var weights = entry.source.GetAllBoneWeights();
                        if (influences.Length != entry.source.vertexCount)
                            throw new InvalidOperationException("Missing support skin weights.");
                        for (int region = 0; region < Regions.Length; region++)
                        {
                            var selected = new bool[influences.Length];
                            int cursor = 0;
                            for (int vertex = 0; vertex < selected.Length; vertex++)
                            {
                                float sum = 0;
                                for (int influence = 0; influence < influences[vertex]; influence++)
                                {
                                    var weight = weights[cursor++];
                                    if (!float.IsFinite(weight.weight) || weight.weight < 0 ||
                                        weight.boneIndex < 0 || weight.boneIndex >= skin.bones.Length)
                                        throw new InvalidOperationException("Invalid support skin weight.");
                                    if (regionBones[region].Contains(skin.bones[weight.boneIndex]))
                                        sum += weight.weight;
                                }
                                selected[vertex] = sum >= .5f;
                            }
                            var vertices = new HashSet<int>();
                            for (int face = 0; face < triangles.Length; face += 3)
                                if (selected[triangles[face]] && selected[triangles[face + 1]] &&
                                    selected[triangles[face + 2]])
                                    for (int corner = 0; corner < 3; corner++)
                                        vertices.Add(triangles[face + corner]);
                            entry.selected[region] = vertices.OrderBy(i => i).ToArray();
                            counts[region] += vertices.Count;
                        }
                    }
                    if (entries.Count == 0 || counts.Any(count => count == 0))
                        throw new InvalidOperationException("Missing rendered support region: " + fighter.name);
                    SelectionSummary = fighter.name + ": skin triangles with >=0.5 region influence on " +
                        "every corner; region root includes descendants; selected vertex counts=" +
                        string.Join(";", Regions.Select((r, i) => r + ":" + counts[i]));
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public Vector3[] Measure()
            {
                MinimumY = float.MaxValue;
                var points = Enumerable.Repeat(new Vector3(0, float.MaxValue, 0), Regions.Length).ToArray();
                var diagnostics = new List<string>();
                foreach (var entry in entries)
                {
                    if (!entry.skin || !entry.skin.gameObject.activeInHierarchy ||
                        entry.skin.sharedMesh != entry.source)
                        throw new InvalidOperationException("Support skin identity changed.");
                    entry.skin.BakeMesh(entry.baked, false);
                    var world = RenderedVertices(entry.skin, entry.baked, diagnostics);
                    foreach (var p in world)
                    {
                        if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z))
                            throw new InvalidOperationException("Nonfinite support geometry.");
                        MinimumY = Mathf.Min(MinimumY, p.y);
                    }
                    for (int region = 0; region < points.Length; region++)
                        foreach (int index in entry.selected[region])
                            if (world[index].y < points[region].y)
                                points[region] = world[index];
                }
                return points;
            }

            public void Dispose()
            {
                foreach (var entry in entries)
                    if (entry.baked)
                        Object.DestroyImmediate(entry.baked);
                entries.Clear();
            }
        }
    }
}
