using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeDenseStudy
    {
        sealed class AxeRegion
        {
            public readonly string hand;
            public readonly Surface head = new Surface();
            public readonly Surface remainder = new Surface();
            public readonly Renderer renderer;
            public readonly Mesh mesh;
            readonly Vector3[] local;
            readonly int axis;
            readonly int widthAxis;
            readonly float boundary;
            readonly float center;
            readonly float radius;
            readonly int sign;

            public AxeRegion(FrankTestActor actor, bool left, List<string> scope)
            {
                hand = left ? "Left" : "Right";
                string socketName = left ? "L_axe_wp" : "R_axe_wp";
                var socket = actor.activeDriver.GetComponentsInChildren<Transform>(true)
                    .Single(t => t.name == socketName);
                renderer = actor.Pose.weaponRenderers.Single(r =>
                    Visible(r) && (r.transform == socket || r.transform.IsChildOf(socket)));
                var filter = renderer.GetComponent<MeshFilter>();
                mesh = filter ? filter.sharedMesh : null;
                if (!mesh)
                    throw new InvalidOperationException("Expected native static axe mesh on " + socketName);
                local = mesh.vertices;
                var bounds = new Bounds(local[0], Vector3.zero);
                foreach (var point in local)
                    bounds.Encapsulate(point);
                axis = bounds.size.x > bounds.size.y ? 0 : 1;
                if (bounds.size.z > bounds.size[axis])
                    axis = 2;
                widthAxis = (axis + 1) % 3;
                if (bounds.size[(axis + 2) % 3] > bounds.size[widthAxis])
                    widthAxis = (axis + 2) % 3;
                float lo = bounds.min[axis];
                float length = bounds.size[axis];
                float upperWidth = Width(local.Where(p => p[axis] > lo + length * .65f));
                float lowerWidth = Width(local.Where(p => p[axis] < lo + length * .35f));
                sign = upperWidth >= lowerWidth ? 1 : -1;
                float TipDistance(Vector3 p) => sign > 0 ? p[axis] - lo : bounds.max[axis] - p[axis];
                var shaft = local.Where(p => TipDistance(p) >= length * .2f &&
                    TipDistance(p) <= length * .5f).ToArray();
                if (shaft.Length < 3)
                    throw new InvalidOperationException("Insufficient shaft cross-section vertices: " + socketName);
                float shaftMin = shaft.Min(p => p[widthAxis]);
                float shaftMax = shaft.Max(p => p[widthAxis]);
                center = (shaftMin + shaftMax) * .5f;
                radius = Mathf.Max((shaftMax - shaftMin) * .5f * 1.35f, length * .025f);
                boundary = sign > 0 ? lo + length * .55f : bounds.max[axis] - length * .55f;
                var triangles = mesh.triangles;
                for (int face = 0; face < triangles.Length; face += 3)
                {
                    bool selected = true;
                    for (int corner = 0; corner < 3; corner++)
                    {
                        Vector3 p = local[triangles[face + corner]];
                        selected &= sign * (p[axis] - boundary) >= 0 &&
                            Mathf.Abs(p[widthAxis] - center) > radius;
                    }
                    var destination = selected ? head : remainder;
                    for (int corner = 0; corner < 3; corner++)
                        destination.triangles.Add(triangles[face + corner]);
                }
                if (head.triangles.Count < 3 || remainder.triangles.Count < 3)
                    throw new InvalidOperationException("Head/shaft region failed to separate: " + socketName);
                scope.Add(FormattableString.Invariant(
                    $"{actor.characterName}/{hand}: mesh={mesh.name}; localBounds={bounds}; axis={axis}; widthAxis={widthAxis}; headSign={sign}; boundary={boundary:R}; shaftCenter={center:R}; shaftExclusionRadius={radius:R}; headFaces={head.triangles.Count / 3}; remainingFaces={remainder.triangles.Count / 3}"));
                Update();
            }

            float Width(IEnumerable<Vector3> points)
            {
                var values = points.Select(p => p[widthAxis]).ToArray();
                return values.Length == 0 ? 0 : values.Max() - values.Min();
            }

            public void Update()
            {
                head.vertices.Clear();
                remainder.vertices.Clear();
                foreach (var p in local)
                {
                    var world = renderer.localToWorldMatrix.MultiplyPoint3x4(p);
                    head.vertices.Add(world);
                    remainder.vertices.Add(world);
                }
                head.Build();
                remainder.Build();
            }
        }
    }
}
