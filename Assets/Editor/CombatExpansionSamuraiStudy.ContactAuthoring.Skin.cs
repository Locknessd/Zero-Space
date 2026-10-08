using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        sealed class CASkin
        {
            public SkinnedMeshRenderer renderer;
            public string path;
            public string identity;
            public int[] triangles;
            public Vector3[] world;
            public float[][] humans;
            public bool[] torso;
            public bool[] support;
            readonly Vector3[] local;
            readonly Matrix4x4[] bindposes;
            readonly Transform[] bones;
            readonly byte[] counts;
            readonly BoneWeight1[] weights;

            public CASkin(SkinnedMeshRenderer skin, Animator animator)
            {
                renderer = skin;
                var mesh = skin.sharedMesh;
                path = AnimationUtility.CalculateTransformPath(skin.transform, animator.transform);
                identity = CombatExpansionInventory.Identity(mesh);
                using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
                using (var vertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp))
                {
                    data[0].GetVertices(vertices);
                    local = vertices.ToArray();
                }
                var baked = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                try
                {
                    skin.BakeMesh(baked, false);
                    triangles = baked.triangles;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(baked);
                }
                bones = skin.bones;
                bindposes = mesh.bindposes;
                counts = mesh.GetBonesPerVertex().ToArray();
                weights = mesh.GetAllBoneWeights().ToArray();
                if (bones.Length != bindposes.Length || counts.Length != local.Length)
                    throw new InvalidOperationException("Invalid native binding: " + path);
                var map = new Dictionary<Transform, int>();
                for (int human = 0; human < (int)HumanBodyBones.LastBone; human++)
                {
                    var node = animator.GetBoneTransform((HumanBodyBones)human);
                    if (node && !map.ContainsKey(node))
                        map.Add(node, human);
                }
                var labels = new int[bones.Length];
                for (int index = 0; index < bones.Length; index++)
                {
                    labels[index] = (int)HumanBodyBones.LastBone;
                    for (var node = bones[index]; node; node = node.parent)
                        if (map.TryGetValue(node, out int human))
                        {
                            labels[index] = human;
                            break;
                        }
                }
                humans = new float[local.Length][];
                torso = new bool[local.Length];
                support = new bool[local.Length];
                world = new Vector3[local.Length];
                int cursor = 0;
                for (int vertex = 0; vertex < local.Length; vertex++)
                {
                    humans[vertex] = new float[(int)HumanBodyBones.LastBone + 1];
                    float sum = 0;
                    float axial = 0;
                    for (int influence = 0; influence < counts[vertex]; influence++)
                    {
                        if (cursor >= weights.Length)
                            throw new InvalidOperationException("Invalid native weight count: " + path);
                        var w = weights[cursor++];
                        if (!float.IsFinite(w.weight) || w.weight < 0 || w.boneIndex < 0 ||
                            w.boneIndex >= bones.Length || (w.weight > 0 && !bones[w.boneIndex]))
                            throw new InvalidOperationException("Invalid native influence: " + path);
                        sum += w.weight;
                        humans[vertex][labels[w.boneIndex]] += w.weight;
                        if (bones[w.boneIndex] && map.TryGetValue(bones[w.boneIndex], out int exact) &&
                            CAGroup(exact) == 1)
                            axial += w.weight;
                    }
                    if (!float.IsFinite(sum) || sum <= 0)
                        throw new InvalidOperationException("Unweighted vertex: " + path);
                    for (int human = 0; human < humans[vertex].Length; human++)
                        humans[vertex][human] /= sum;
                    torso[vertex] = axial >= .5f;
                    var groups = CAGrouped(humans[vertex]);
                    support[vertex] = groups[0] + groups[1] + groups[2] + groups[3] >= .5f;
                }
                if (cursor != weights.Length || triangles.Length % 3 != 0)
                    throw new InvalidOperationException("Native topology/weights mismatch: " + path);
            }

            public void Update()
            {
                var positions = (Vector3[])local.Clone();
                CABlendShapes(renderer, positions);
                var matrices = new Matrix4x4[bones.Length];
                for (int bone = 0; bone < bones.Length; bone++)
                    if (bones[bone])
                        matrices[bone] = bones[bone].localToWorldMatrix * bindposes[bone];
                int cursor = 0;
                for (int vertex = 0; vertex < positions.Length; vertex++)
                {
                    var point = Vector3.zero;
                    float sum = 0;
                    for (int influence = 0; influence < counts[vertex]; influence++)
                    {
                        var w = weights[cursor++];
                        point += matrices[w.boneIndex].MultiplyPoint3x4(positions[vertex]) * w.weight;
                        sum += w.weight;
                    }
                    world[vertex] = point / sum;
                    if (!float.IsFinite(world[vertex].x) || !float.IsFinite(world[vertex].y) ||
                        !float.IsFinite(world[vertex].z))
                        throw new InvalidOperationException("Nonfinite native skin geometry: " + path);
                }
            }
        }

        static CASkin[] CASkins(CharacterCombat fighter)
        {
            var result = fighter.Animator.GetComponentsInChildren<SkinnedMeshRenderer>()
                .Where(s => s.enabled && s.gameObject.activeInHierarchy && s.sharedMesh &&
                    s.sharedMesh.vertexCount >= 1000)
                .Select(s => new CASkin(s, fighter.Animator)).OrderBy(s => s.path).ToArray();
            if (result.Length == 0)
                throw new InvalidOperationException("Missing visible body skins: " + fighter.name);
            return result;
        }

        static int CAGroup(int human)
        {
            string name = ((HumanBodyBones)human).ToString();
            if (name == "Head" || name == "Neck" || name == "Jaw" || name.EndsWith("Eye"))
                return 0;
            if (name == "Hips" || name == "Spine" || name == "Chest" || name == "UpperChest")
                return 1;
            if (name.StartsWith("Left") || name.StartsWith("Right"))
            {
                bool leg = name.Contains("Leg") || name.Contains("Foot") || name.Contains("Toes");
                return (leg ? 4 : 2) + (name.StartsWith("Right") ? 1 : 0);
            }
            return 6;
        }
    }
}
