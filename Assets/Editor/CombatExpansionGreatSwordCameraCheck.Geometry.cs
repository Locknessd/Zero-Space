using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordCameraCheck
    {
        sealed class BodyGeometry : IDisposable
        {
            readonly SkinnedMeshRenderer skin;
            readonly Vector3[] vertices;
            readonly Vector3[] shaped;
            readonly Transform[] bones;
            readonly Matrix4x4[] bindings;
            readonly Matrix4x4[] matrices;
            readonly byte[] counts;
            readonly BoneWeight1[] weights;
            readonly Mesh baked;
            readonly List<Vector3> bakedVertices = new List<Vector3>();
            public string Name => skin.name;
            public float BakeError { get; private set; }
            public float BoundsMiss { get; private set; }
            public Bounds WorldBounds { get; private set; }
            public Bounds RawBakeBounds { get; private set; }
            public Bounds VertexBakeBounds { get; private set; }
            public Bounds RecalculatedBakeBounds { get; private set; }
            public float RawBoundsDifference { get; private set; }
            public float RecalculatedBoundsError { get; private set; }
            public readonly Vector3[] rawFramingEnvelope = new Vector3[8];
            public readonly Vector3[] world;
            public readonly Vector3[] framingEnvelope = new Vector3[8];

            public BodyGeometry(SkinnedMeshRenderer renderer)
            {
                skin = renderer;
                var mesh = skin.sharedMesh;
                using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
                using (var local = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp))
                {
                    data[0].GetVertices(local);
                    vertices = local.ToArray();
                }
                bones = skin.bones;
                bindings = mesh.bindposes;
                counts = mesh.GetBonesPerVertex().ToArray();
                weights = mesh.GetAllBoneWeights().ToArray();
                if (vertices.Length == 0 || bones.Length == 0 || bindings.Length != bones.Length ||
                    counts.Length != vertices.Length)
                    throw new InvalidOperationException("Missing skin geometry: " + Name);
                matrices = new Matrix4x4[bones.Length];
                world = new Vector3[vertices.Length];
                shaped = new Vector3[vertices.Length];
                baked = new Mesh
                {
                    name = "GreatSword independent camera audit",
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            public void Sample()
            {
                Array.Copy(vertices, shaped, vertices.Length);
                ApplyShapes(skin, shaped);
                for (int i = 0; i < bones.Length; i++)
                    matrices[i] = bones[i] ? bones[i].localToWorldMatrix * bindings[i] : Matrix4x4.zero;
                skin.BakeMesh(baked, false);
                baked.GetVertices(bakedVertices);
                if (bakedVertices.Count != world.Length)
                    throw new InvalidOperationException("Bake vertex count mismatch: " + Name);
                RawBakeBounds = baked.bounds;
                var vertexBounds = new Bounds(bakedVertices[0], Vector3.zero);
                foreach (var vertex in bakedVertices)
                    vertexBounds.Encapsulate(vertex);
                VertexBakeBounds = vertexBounds;
                baked.RecalculateBounds();
                RecalculatedBakeBounds = baked.bounds;
                RawBoundsDifference = BoundsDifference(RawBakeBounds, VertexBakeBounds);
                RecalculatedBoundsError = BoundsDifference(RecalculatedBakeBounds, VertexBakeBounds);
                Matrix4x4 transform = Matrix4x4.TRS(skin.transform.position, skin.transform.rotation, Vector3.one);
                Matrix4x4 inverse = transform.inverse;
                int limit = skin.quality == SkinQuality.Auto ? (int)QualitySettings.skinWeights : (int)skin.quality;
                BakeError = 0;
                BoundsMiss = 0;
                int cursor = 0;
                for (int vertex = 0; vertex < world.Length; vertex++)
                {
                    Vector3 position = Vector3.zero;
                    float total = 0;
                    int used = limit > 0 ? Mathf.Min(counts[vertex], limit) : counts[vertex];
                    for (int influence = 0; influence < used; influence++)
                    {
                        var weight = weights[cursor + influence];
                        if (weight.weight <= 0)
                            continue;
                        if (weight.boneIndex >= bones.Length || !bones[weight.boneIndex])
                            throw new InvalidOperationException("Missing weighted bone: " + Name);
                        position += matrices[weight.boneIndex].MultiplyPoint3x4(shaped[vertex]) * weight.weight;
                        total += weight.weight;
                    }
                    if (total <= 0)
                        throw new InvalidOperationException("Unweighted vertex: " + Name);
                    world[vertex] = position / total;
                    if (!Finite(world[vertex]))
                        throw new InvalidOperationException("Nonfinite body vertex: " + Name);
                    BakeError = Mathf.Max(BakeError,
                        Vector3.Distance(world[vertex], transform.MultiplyPoint3x4(bakedVertices[vertex])));
                    Vector3 local = inverse.MultiplyPoint3x4(world[vertex]);
                    BoundsMiss = Mathf.Max(BoundsMiss, Vector3.Distance(local, RawBakeBounds.ClosestPoint(local)));
                    cursor += counts[vertex];
                }
                var bounds = new Bounds(world[0], Vector3.zero);
                var localBounds = new Bounds(inverse.MultiplyPoint3x4(world[0]), Vector3.zero);
                foreach (var vertex in world)
                {
                    bounds.Encapsulate(vertex);
                    localBounds.Encapsulate(inverse.MultiplyPoint3x4(vertex));
                }
                WorldBounds = bounds;
                // The runtime fits a renderer-local AABB rotated into world space.
                // Reconstruct that envelope from independent bone-weighted vertices:
                // tight world bounds alone understate its required dolly during rotations.
                for (int i = 0; i < framingEnvelope.Length; i++)
                {
                    Vector3 corner = localBounds.center + Vector3.Scale(localBounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    framingEnvelope[i] = transform.MultiplyPoint3x4(corner);
                    Vector3 rawCorner = RawBakeBounds.center + Vector3.Scale(RawBakeBounds.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    rawFramingEnvelope[i] = transform.MultiplyPoint3x4(rawCorner);
                }
            }

            public void Dispose() => Object.DestroyImmediate(baked);
        }

        sealed class StaticGeometry
        {
            readonly MeshRenderer renderer;
            readonly Vector3[] local;
            readonly Bounds meshBounds;
            public string Name => renderer.name;
            public readonly Vector3[] world;
            public readonly Vector3[] framingEnvelope = new Vector3[8];
            public float RuntimeBoundsError { get; private set; }
            public Bounds WorldBounds { get; private set; }

            public StaticGeometry(MeshRenderer source)
            {
                renderer = source;
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh)
                    throw new InvalidOperationException("Missing visible mesh: " + Name);
                var mesh = filter.sharedMesh;
                using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
                using (var vertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp))
                {
                    data[0].GetVertices(vertices);
                    local = vertices.ToArray();
                }
                if (local.Length == 0)
                    throw new InvalidOperationException("Empty visible mesh: " + Name);
                world = new Vector3[local.Length];
                meshBounds = mesh.bounds;
            }

            public void Sample()
            {
                Matrix4x4 matrix = renderer.localToWorldMatrix;
                for (int i = 0; i < world.Length; i++)
                {
                    world[i] = matrix.MultiplyPoint3x4(local[i]);
                    if (!Finite(world[i]))
                        throw new InvalidOperationException("Nonfinite static vertex: " + Name);
                }
                // MeshRenderer bounds are the world AABB of the transformed mesh-local AABB.
                // Reconstruct it independently rather than trusting the camera's cached renderer bounds.
                var corners = new List<Vector3>();
                AppendCorners(corners, meshBounds);
                var bounds = new Bounds(matrix.MultiplyPoint3x4(corners[0]), Vector3.zero);
                foreach (var corner in corners)
                    bounds.Encapsulate(matrix.MultiplyPoint3x4(corner));
                WorldBounds = bounds;
                corners.Clear();
                AppendCorners(corners, bounds);
                corners.CopyTo(framingEnvelope);
                RuntimeBoundsError = Mathf.Max(Vector3.Distance(bounds.min, renderer.bounds.min),
                    Vector3.Distance(bounds.max, renderer.bounds.max));
            }
        }

        static StaticGeometry[] StaticMeshes(params GameObject[] roots) => roots
            .SelectMany(root => root.GetComponentsInChildren<MeshRenderer>())
            .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).Distinct()
            .Select(renderer => new StaticGeometry(renderer)).ToArray();

        static float BoundsDifference(Bounds first, Bounds second) =>
            Mathf.Max(Vector3.Distance(first.min, second.min), Vector3.Distance(first.max, second.max));

        static bool Finite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        static BodyGeometry[] Bodies(CharacterCombat fighter) => fighter.Animator
            .GetComponentsInChildren<SkinnedMeshRenderer>()
            .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.sharedMesh && r.bones.Length > 0)
            .Select(r => new BodyGeometry(r)).ToArray();

        static void ApplyShapes(SkinnedMeshRenderer skin, Vector3[] positions)
        {
            var mesh = skin.sharedMesh;
            for (int shape = 0; shape < mesh.blendShapeCount; shape++)
            {
                float weight = skin.GetBlendShapeWeight(shape);
                if (Mathf.Abs(weight) < .000001f)
                    continue;
                int frames = mesh.GetBlendShapeFrameCount(shape);
                if (frames == 0)
                    continue;
                int high = 0;
                while (high < frames - 1 && mesh.GetBlendShapeFrameWeight(shape, high) < weight)
                    high++;
                int low = high - 1;
                float lower = low < 0 ? 0 : mesh.GetBlendShapeFrameWeight(shape, low);
                float upper = mesh.GetBlendShapeFrameWeight(shape, high);
                var first = new Vector3[positions.Length];
                var second = new Vector3[positions.Length];
                if (low >= 0)
                    mesh.GetBlendShapeFrameVertices(shape, low, first, null, null);
                mesh.GetBlendShapeFrameVertices(shape, high, second, null, null);
                float blend = Mathf.Abs(upper - lower) < .000001f ? 1 : (weight - lower) / (upper - lower);
                for (int vertex = 0; vertex < positions.Length; vertex++)
                    positions[vertex] += Vector3.LerpUnclamped(first[vertex], second[vertex], blend);
            }
        }
    }
}
