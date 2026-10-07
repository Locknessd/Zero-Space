using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

public static partial class BattlePresentationContactSetup
{
    static Vector3[] RenderedVertices(SkinnedMeshRenderer skin, Mesh baked, List<string> geometry)
    {
        Mesh mesh = skin.sharedMesh;
        Vector3[] positions;
        // This Editor API also reads imported meshes whose runtime Read/Write flag is disabled.
        using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
        using (var local = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp))
        {
            data[0].GetVertices(local);
            positions = local.ToArray();
        }
        ApplyBlendShapes(skin, positions);
        Transform[] bones = skin.bones;
        Matrix4x4[] bindposes = mesh.bindposes;
        if (bones.Length == 0 || bindposes.Length != bones.Length)
            throw new InvalidOperationException("Missing skin binding: " + skin.name);
        var matrices = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
            if (bones[i]) matrices[i] = bones[i].localToWorldMatrix * bindposes[i];
        var world = new Vector3[positions.Length];
        // These native views belong to the mesh; do not dispose its backing storage.
        var counts = mesh.GetBonesPerVertex();
        var weights = mesh.GetAllBoneWeights();
        {
            if (counts.Length != positions.Length)
                throw new InvalidOperationException("Missing vertex skin weights: " + skin.name);
            int cursor = 0;
            int limit = skin.quality == SkinQuality.Auto ? (int)QualitySettings.skinWeights : (int)skin.quality;
            for (int vertex = 0; vertex < positions.Length; vertex++)
            {
                float total = 0;
                int used = limit > 0 ? Mathf.Min(counts[vertex], limit) : counts[vertex];
                for (int influence = 0; influence < used; influence++)
                {
                    var weight = weights[cursor + influence];
                    if (weight.weight <= 0) continue;
                    if (weight.boneIndex >= bones.Length || !bones[weight.boneIndex])
                        throw new InvalidOperationException("Missing weighted bone: " + skin.name);
                    world[vertex] += matrices[weight.boneIndex].MultiplyPoint3x4(positions[vertex]) * weight.weight;
                    total += weight.weight;
                }
                if (total <= 0)
                    throw new InvalidOperationException("Unweighted rendered vertex: " + skin.name);
                world[vertex] /= total;
                cursor += counts[vertex];
            }
        }
        CompareBakeSpace(skin, baked.vertices, world, geometry);
        return world;
    }

    static void ApplyBlendShapes(SkinnedMeshRenderer skin, Vector3[] positions)
    {
        Mesh mesh = skin.sharedMesh;
        var first = new Vector3[positions.Length];
        var second = new Vector3[positions.Length];
        for (int shape = 0; shape < mesh.blendShapeCount; shape++)
        {
            float weight = skin.GetBlendShapeWeight(shape);
            if (Mathf.Abs(weight) < .000001f) continue;
            int frames = mesh.GetBlendShapeFrameCount(shape);
            if (frames == 0) continue;
            int high = 0;
            while (high < frames - 1 && mesh.GetBlendShapeFrameWeight(shape, high) < weight) high++;
            int low = high - 1;
            if (high == frames - 1 && weight > mesh.GetBlendShapeFrameWeight(shape, high) && frames > 1)
                low = high - 1;
            float lower = low < 0 ? 0 : mesh.GetBlendShapeFrameWeight(shape, low);
            float upper = mesh.GetBlendShapeFrameWeight(shape, high);
            Array.Clear(first, 0, first.Length);
            if (low >= 0) mesh.GetBlendShapeFrameVertices(shape, low, first, null, null);
            mesh.GetBlendShapeFrameVertices(shape, high, second, null, null);
            float blend = Mathf.Abs(upper - lower) < .000001f ? 1 : (weight - lower) / (upper - lower);
            for (int vertex = 0; vertex < positions.Length; vertex++)
                positions[vertex] += Vector3.LerpUnclamped(first[vertex], second[vertex], blend);
        }
    }

    static void CompareBakeSpace(SkinnedMeshRenderer skin, Vector3[] baked, Vector3[] world,
        List<string> geometry)
    {
        if (baked.Length != world.Length)
            throw new InvalidOperationException("Skin vertex count changed: " + skin.name);
        Matrix4x4 rotationOnly = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
        Matrix4x4 full = skin.localToWorldMatrix;
        float rotationError = 0;
        float fullError = 0;
        var bounds = new Bounds(world[0], Vector3.zero);
        for (int vertex = 0; vertex < world.Length; vertex++)
        {
            rotationError = Mathf.Max(rotationError, Vector3.Distance(world[vertex],
                rotationOnly.MultiplyPoint3x4(baked[vertex])));
            fullError = Mathf.Max(fullError, Vector3.Distance(world[vertex], full.MultiplyPoint3x4(baked[vertex])));
            bounds.Encapsulate(world[vertex]);
        }
        geometry.Add(FormattableString.Invariant($"{skin.name}: explicit bone/bindpose world bounds={bounds}; ") +
            FormattableString.Invariant($"max Bake(false)+TR error={rotationError:R}m; ") +
            FormattableString.Invariant($"max Bake(false)+TRS error={fullError:R}m; ") +
            $"rootBone={(skin.rootBone ? skin.rootBone.name : "none")}; quality={skin.quality}");
    }
}
