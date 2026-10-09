using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeDenseStudy
    {
        // Native-only adapter. Surface, AddBodyMesh and RenderedVertices remain unchanged.
        public sealed class NativePairProbe
        {
            readonly SkinnedMeshRenderer[] skins;
            readonly MeshRenderer blade;
            readonly Vector3[] vertices;
            readonly int[] triangles;
            public readonly string[] MeshIdentities;
            public readonly string BladeMeshIdentity;
            public Bounds BodyBounds { get; private set; }

            public NativePairProbe(Animator source, Animator receiver)
            {
                bool Weapon(Transform node)
                {
                    for (var current = node; current && current != receiver.transform; current = current.parent)
                        if (current.name == "BladeR" || current.name == "Sword_Hold")
                            return true;
                    return false;
                }
                skins = receiver.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(s => Visible(s) && !Weapon(s.transform)).ToArray();
                if (skins.Length == 0 || skins.Any(s => !s.sharedMesh || s.sharedMesh.vertexCount == 0))
                    throw new InvalidOperationException("Native receiver has no complete visible body skin.");
                if (receiver.GetComponentsInChildren<Renderer>(true).Any(r => Weapon(r.transform) && r.enabled))
                    throw new InvalidOperationException("Native receiver weapons must be disabled.");
                var socket = source.GetComponentsInChildren<Transform>(true).Single(t => t.name == "BladeR");
                blade = socket.GetComponentsInChildren<MeshRenderer>(true).Single(r => Visible(r));
                var filter = blade.GetComponent<MeshFilter>();
                var mesh = filter ? filter.sharedMesh : null;
                BladeMeshIdentity = CombatExpansionInventory.Identity(mesh);
                if (!mesh || BladeMeshIdentity != "3e685dd57e9c78b49b20bef3e8358ae3:4300002")
                    throw new InvalidOperationException("Native inspected BladeR mesh identity mismatch.");
                vertices = mesh.vertices;
                var faces = mesh.triangles;
                ValidateTriangles(vertices.Length, faces);
                var selected = new List<int>();
                for (int face = 0; face < faces.Length; face += 3)
                    if (vertices[faces[face]].z <= -.33f && vertices[faces[face + 1]].z <= -.33f &&
                        vertices[faces[face + 2]].z <= -.33f)
                        selected.AddRange(new[] { faces[face], faces[face + 1], faces[face + 2] });
                triangles = selected.ToArray();
                if (triangles.Length != 333)
                    throw new InvalidOperationException("Native blade no longer has the inspected 111 faces.");
                MeshIdentities = skins.Select(s => s.name + "=" +
                    CombatExpansionInventory.Identity(s.sharedMesh)).ToArray();
            }

            public BodyContact Measure(List<string> diagnostics)
            {
                var body = new Surface();
                foreach (var skin in skins)
                {
                    if (!skin || !skin.sharedMesh || !skin.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("Native body binding disappeared.");
                    AddBodyMesh(body, skin, diagnostics);
                }
                ValidateTriangles(body.vertices.Count, body.triangles.ToArray());
                body.Build();
                BodyBounds = body.Bounds;
                var source = new Surface();
                foreach (var point in vertices)
                {
                    var world = blade.localToWorldMatrix.MultiplyPoint3x4(point);
                    RequireFinite(world);
                    source.vertices.Add(world);
                }
                source.triangles.AddRange(triangles);
                source.Build();
                var closest = body.Closest(source);
                if (!float.IsFinite(closest.squared) || closest.squared < 0)
                    throw new InvalidOperationException("Nonfinite native blade/body gap.");
                RequireFinite(closest.blade);
                RequireFinite(closest.body);
                return new BodyContact(Mathf.Sqrt(closest.squared), closest.blade, closest.body,
                    HumanBodyBones.LastBone, Vector3.zero);
            }

            static void ValidateTriangles(int count, int[] faces)
            {
                if (count == 0 || faces.Length == 0 || faces.Length % 3 != 0 ||
                    faces.Any(i => i < 0 || i >= count))
                    throw new InvalidOperationException("Invalid native surface triangles.");
            }

            static void RequireFinite(Vector3 point)
            {
                if (!float.IsFinite(point.x) || !float.IsFinite(point.y) || !float.IsFinite(point.z))
                    throw new InvalidOperationException("Nonfinite native surface vertex.");
            }
        }
    }
}
