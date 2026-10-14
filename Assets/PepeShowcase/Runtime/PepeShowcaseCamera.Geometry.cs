using System.Collections.Generic;
using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcaseCamera
    {
        Mesh baked;
        readonly List<Vector3> vertices = new List<Vector3>();

        bool WeaponBounds(Renderer renderer, out Bounds bounds)
        {
            bounds = renderer.bounds;
            if (!(renderer is SkinnedMeshRenderer skin)) return bounds.size.sqrMagnitude > .0001f;
            if (!skin.sharedMesh) return false;
            if (!baked) baked = new Mesh { name = "Showcase posed weapon bounds" };
            skin.BakeMesh(baked, false);
            baked.GetVertices(vertices);
            var matrix = skin.localToWorldMatrix;
            for (int i = 0; i < vertices.Count; i++)
                vertices[i] = matrix.MultiplyPoint3x4(vertices[i]);
            var indices = baked.triangles;
            bool found = false;
            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = vertices[indices[i]];
                Vector3 b = vertices[indices[i + 1]];
                Vector3 c = vertices[indices[i + 2]];
                // Animated zero-scale weapon parts have no visible triangles.
                if (Vector3.Cross(b - a, c - a).sqrMagnitude < 1e-12f) continue;
                if (!found)
                {
                    bounds = new Bounds(a, Vector3.zero);
                    found = true;
                }
                bounds.Encapsulate(a);
                bounds.Encapsulate(b);
                bounds.Encapsulate(c);
            }
            return found;
        }

        void OnDestroy()
        {
            if (!baked) return;
            if (Application.isPlaying) Destroy(baked);
            else DestroyImmediate(baked);
        }
    }
}
