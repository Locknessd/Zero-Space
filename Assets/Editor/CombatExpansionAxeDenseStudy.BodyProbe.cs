using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeDenseStudy
    {
        public readonly struct BodyContact
        {
            public readonly float gap;
            public readonly Vector3 sourcePoint;
            public readonly Vector3 bodyPoint;
            public readonly HumanBodyBones nearestBone;
            public readonly Vector3 boneOffset;

            public BodyContact(float gap, Vector3 sourcePoint, Vector3 bodyPoint,
                HumanBodyBones nearestBone, Vector3 boneOffset)
            {
                this.gap = gap;
                this.sourcePoint = sourcePoint;
                this.bodyPoint = bodyPoint;
                this.nearestBone = nearestBone;
                this.boneOffset = boneOffset;
            }
        }

        // Reuses the dense axe study's exact triangle queries for other authored weapons.
        // Current geometry and BVH partitioning are rebuilt on every update; buffers alone are reused.
        public sealed partial class MovingBodyProbe
        {
            readonly CharacterCombat target;
            readonly SkinnedMeshRenderer[] skins;
            Surface body;
            Surface spare;
            public float MinimumY => body != null ? body.Bounds.min.y : float.NaN;

            public MovingBodyProbe(CharacterCombat target)
            {
                this.target = target;
                skins = BodySkins(target);
                if (skins.Length == 0)
                    throw new InvalidOperationException("Contact probe needs visible victim skin.");
            }

            public void Update(List<string> diagnostics)
            {
                var next = MovingReceiver(target, diagnostics, skins, spare ?? new Surface());
                spare = body;
                body = next;
            }

            public BodyContact Measure(Renderer renderer, Vector3[] localVertices, int[] triangles)
            {
                if (body == null || !renderer || localVertices == null || triangles == null ||
                    triangles.Length == 0 || triangles.Length % 3 != 0)
                    throw new InvalidOperationException("Contact surfaces are incomplete.");
                var source = new Surface();
                var matrix = renderer.localToWorldMatrix;
                foreach (var point in localVertices)
                    source.vertices.Add(matrix.MultiplyPoint3x4(point));
                foreach (int index in triangles)
                {
                    if (index < 0 || index >= localVertices.Length)
                        throw new InvalidOperationException("Invalid authored contact triangle index.");
                    source.triangles.Add(index);
                }
                source.Build();
                var result = body.Closest(source);
                var anchor = NearestAnchor(target, result.body, out var bone);
                return new BodyContact(Mathf.Sqrt(result.squared), result.blade, result.body,
                    bone, anchor.InverseTransformPoint(result.body));
            }
        }
    }
}
