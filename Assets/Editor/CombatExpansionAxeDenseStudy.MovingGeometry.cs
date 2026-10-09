using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeDenseStudy
    {
        // Used only by MovingBodyProbe. No imported mesh, binding, or pose data is cached.
        static Surface MovingReceiver(CharacterCombat fighter, List<string> geometry,
            SkinnedMeshRenderer[] skins, Surface result)
        {
            result.vertices.Clear();
            result.triangles.Clear();
            foreach (var skin in skins)
                AddBodyMesh(result, skin, geometry);
            result.BuildMoving();
            var bones = Enum.GetValues(typeof(HumanBodyBones)).Cast<HumanBodyBones>()
                .Where(b => b < HumanBodyBones.LastBone).Select(fighter.Animator.GetBoneTransform)
                .Where(t => t).ToArray();
            var bounds = new Bounds(bones[0].position, Vector3.zero);
            foreach (var bone in bones)
                bounds.Encapsulate(bone.position);
            geometry.Add(fighter.name + " body=" + result.Bounds + "; skeleton=" + bounds);
            if (result.Bounds.size.magnitude > bounds.size.magnitude * 2 + .5f)
                throw new InvalidOperationException("Baked body exceeds evaluated skeleton bounds: " + fighter.name);
            return result;
        }

        sealed partial class Surface
        {
            Vector3[] movingCenters;
            IComparer<int>[] movingComparers;

            public void BuildMoving()
            {
                if (triangles.Count == 0)
                    throw new InvalidOperationException("No rendered contact triangles.");
                int count = triangles.Count / 3;
                if (order == null || order.Length != count)
                    order = new int[count];
                if (movingCenters == null || movingCenters.Length != count)
                    movingCenters = new Vector3[count];
                if (movingComparers == null)
                {
                    movingComparers = new IComparer<int>[3];
                    for (int axis = 0; axis < 3; axis++)
                    {
                        int coordinate = axis;
                        movingComparers[axis] = Comparer<int>.Create((a, b) =>
                            movingCenters[a][coordinate].CompareTo(movingCenters[b][coordinate]));
                    }
                }
                for (int i = 0; i < count; i++)
                {
                    order[i] = i;
                    movingCenters[i] = Center(i);
                }
                nodes.Clear();
                SplitMoving(0, count);
            }

            int SplitMoving(int start, int count)
            {
                // Same initial order, comparator values, sort implementation and bounds accumulation as Split.
                // Refitting an old partition would change tied contact representatives, so rebuild it instead.
                int id = nodes.Count;
                var bounds = new Bounds(vertices[triangles[order[start] * 3]], Vector3.zero);
                for (int i = start; i < start + count; i++)
                    for (int j = 0; j < 3; j++)
                        bounds.Encapsulate(vertices[triangles[order[i] * 3 + j]]);
                var node = new Node { bounds = bounds, start = start, count = count, left = -1, right = -1 };
                nodes.Add(node);
                if (count > 12)
                {
                    Vector3 size = bounds.size;
                    int axis = size.x > size.y ? 0 : 1;
                    if (size.z > size[axis])
                        axis = 2;
                    Array.Sort(order, start, count, movingComparers[axis]);
                    int half = count / 2;
                    node.left = SplitMoving(start, half);
                    node.right = SplitMoving(start + half, count - half);
                    nodes[id] = node;
                }
                return id;
            }
        }
    }
}
