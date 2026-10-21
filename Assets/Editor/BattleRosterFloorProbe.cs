using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

/// <summary>Evaluated visible skin vertices with cached immutable mesh data, in world space.</summary>
public sealed class BattleRosterFloorProbe
{
    sealed class Skin
    {
        public SkinnedMeshRenderer renderer;
        public Vector3[] vertices;
        public Matrix4x4[] bindposes;
        public Transform[] bones;
        public byte[] counts;
        public BoneWeight1[] weights;
        public Vector4[] rows;
        public int limit;
        public bool[] support;
    }

    readonly Animator animator;
    readonly List<Skin> skins = new List<Skin>();
    readonly bool hasBlendShapes;

    public BattleRosterFloorProbe(CharacterCombat fighter) : this(fighter.Animator) { }

    public BattleRosterFloorProbe(Animator animator, bool feetOnly = false)
    {
        this.animator = animator;
        foreach (var renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>())
        {
            if (!renderer.enabled || !renderer.sharedMesh || renderer.sharedMesh.vertexCount < 1000)
                continue;
            var mesh = renderer.sharedMesh;
            hasBlendShapes |= mesh.blendShapeCount > 0;
            Vector3[] vertices;
            using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var local = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp))
            {
                data[0].GetVertices(local);
                vertices = local.ToArray();
            }
            var skin = new Skin
            {
                renderer = renderer,
                vertices = vertices,
                bindposes = mesh.bindposes,
                bones = renderer.bones,
                counts = mesh.GetBonesPerVertex().ToArray(),
                weights = mesh.GetAllBoneWeights().ToArray(),
                rows = new Vector4[renderer.bones.Length],
                limit = renderer.quality == SkinQuality.Auto ? (int)QualitySettings.skinWeights : (int)renderer.quality
            };
            if (feetOnly)
            {
                var feet = new[] { animator.GetBoneTransform(HumanBodyBones.LeftFoot),
                    animator.GetBoneTransform(HumanBodyBones.RightFoot) }.Where(t => t).ToArray();
                var footBones = skin.bones.Select(b => feet.Any(f => b && (b == f || b.IsChildOf(f)))).ToArray();
                skin.support = new bool[vertices.Length];
                int cursor = 0;
                for (int vertex = 0; vertex < vertices.Length; vertex++)
                {
                    for (int influence = 0; influence < skin.counts[vertex]; influence++)
                    {
                        var weight = skin.weights[cursor + influence];
                        skin.support[vertex] |= footBones[weight.boneIndex] && weight.weight >= .1f;
                    }
                    cursor += skin.counts[vertex];
                }
            }
            if (skin.bones.Length != skin.bindposes.Length || skin.counts.Length != vertices.Length)
                throw new Exception("Invalid floor skin binding: " + renderer.name);
            skins.Add(skin);
        }
        if (skins.Count == 0)
            throw new Exception("No visible floor geometry: " + animator.name);
    }

    public float Minimum()
    {
        if (hasBlendShapes && skins.All(s => s.support == null))
            return BattlePresentationContactSetup.EvaluatedBounds(animator).min.y;
        float minimum = float.PositiveInfinity;
        foreach (var skin in skins)
        {
            for (int bone = 0; bone < skin.bones.Length; bone++)
            {
                if (!skin.bones[bone])
                    throw new Exception("Missing floor bone.");
                var matrix = skin.bones[bone].localToWorldMatrix * skin.bindposes[bone];
                skin.rows[bone] = matrix.GetRow(1);
            }
            int cursor = 0;
            for (int vertex = 0; vertex < skin.vertices.Length; vertex++)
            {
                if (skin.support != null && !skin.support[vertex])
                {
                    cursor += skin.counts[vertex];
                    continue;
                }
                var p = skin.vertices[vertex];
                int count = skin.limit > 0 ? Math.Min(skin.limit, skin.counts[vertex]) : skin.counts[vertex];
                float y = 0;
                float total = 0;
                for (int influence = 0; influence < count; influence++)
                {
                    var weight = skin.weights[cursor + influence];
                    var row = skin.rows[weight.boneIndex];
                    y += (row.x * p.x + row.y * p.y + row.z * p.z + row.w) * weight.weight;
                    total += weight.weight;
                }
                if (total <= 0)
                    throw new Exception("Unweighted floor vertex.");
                minimum = Mathf.Min(minimum, y / total);
                cursor += skin.counts[vertex];
            }
        }
        if (!float.IsFinite(minimum))
            throw new Exception("Nonfinite floor geometry.");
        return minimum;
    }
}
