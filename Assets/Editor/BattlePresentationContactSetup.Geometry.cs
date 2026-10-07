using System;
using System.Collections.Generic;
using UnityEngine;

public static partial class BattlePresentationContactSetup
{
    // Triangle BVH: queries use evaluated vertices, never imported renderer bounds.
    sealed class Surface
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<int> triangles = new List<int>();
        readonly List<Node> nodes = new List<Node>();
        int[] order;
        struct Node
        {
            public Bounds bounds;
            public int start, count, left, right;
        }
        public void Build()
        {
            if (triangles.Count == 0) throw new InvalidOperationException("No rendered contact triangles.");
            order = new int[triangles.Count / 3];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            nodes.Clear();
            Split(0, order.Length);
        }
        int Split(int start, int count)
        {
            int id = nodes.Count;
            var bounds = new Bounds(vertices[triangles[order[start] * 3]], Vector3.zero);
            for (int i = start; i < start + count; i++)
                for (int j = 0; j < 3; j++) bounds.Encapsulate(vertices[triangles[order[i] * 3 + j]]);
            var node = new Node { bounds = bounds, start = start, count = count, left = -1, right = -1 };
            nodes.Add(node);
            if (count > 12)
            {
                Vector3 size = bounds.size;
                int axis = size.x > size.y ? 0 : 1;
                if (size.z > size[axis]) axis = 2;
                Array.Sort(order, start, count,
                    Comparer<int>.Create((a, b) => Center(a)[axis].CompareTo(Center(b)[axis])));
                int half = count / 2;
                node.left = Split(start, half);
                node.right = Split(start + half, count - half);
                nodes[id] = node;
            }
            return id;
        }
        Vector3 Center(int face)
        {
            int index = face * 3;
            return (vertices[triangles[index]] + vertices[triangles[index + 1]] +
                vertices[triangles[index + 2]]) / 3;
        }
        public Vector3 Nearest(Vector3 point)
        {
            float best = float.PositiveInfinity;
            Vector3 nearest = point;
            Search(0, point, ref best, ref nearest);
            return nearest;
        }
        void Search(int index, Vector3 point, ref float best, ref Vector3 nearest)
        {
            var node = nodes[index];
            if (node.bounds.SqrDistance(point) > best) return;
            if (node.left >= 0)
            {
                int first = node.left;
                int second = node.right;
                if (nodes[first].bounds.SqrDistance(point) > nodes[second].bounds.SqrDistance(point))
                {
                    first = node.right;
                    second = node.left;
                }
                Search(first, point, ref best, ref nearest);
                Search(second, point, ref best, ref nearest);
                return;
            }
            for (int i = node.start; i < node.start + node.count; i++)
            {
                int face = order[i] * 3;
                Vector3 candidate = TrianglePoint(point, vertices[triangles[face]],
                    vertices[triangles[face + 1]], vertices[triangles[face + 2]]);
                float distance = (candidate - point).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = candidate;
            }
        }
        public float Distance(Vector3 point) => Vector3.Distance(point, Nearest(point));
        public Bounds Bounds => nodes[0].bounds;
        public IEnumerable<Vector3> Samples()
        {
            var used = new HashSet<int>();
            foreach (int index in triangles)
                if (used.Add(index)) yield return vertices[index];
            for (int i = 0; i < triangles.Count; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                yield return (a + b + c) / 3;
                yield return (a + b) / 2;
                yield return (a + c) / 2;
                yield return (b + c) / 2;
            }
        }
    }

    static Vector3 Contact(Surface source, Surface body)
    {
        float best = float.PositiveInfinity;
        Vector3 contact = Vector3.zero;
        // Both directions cover broad blade faces and small receiver features.
        foreach (Vector3 point in source.Samples()) Consider(point, body.Nearest(point));
        foreach (Vector3 point in body.Samples()) Consider(source.Nearest(point), point);
        // Alternating exact surface projections refine the best sampled pair.
        for (int i = 0; i < 32; i++)
        {
            Vector3 next = body.Nearest(source.Nearest(contact));
            float distance = source.Distance(next);
            if (distance > best + .000001f) break;
            float movement = (next - contact).sqrMagnitude;
            contact = next;
            best = distance;
            if (movement < 1e-12f) break;
        }
        return contact;

        void Consider(Vector3 a, Vector3 b)
        {
            float distance = Vector3.Distance(a, b);
            if (distance >= best) return;
            best = distance;
            contact = b;
        }
    }

    static Vector3 TrianglePoint(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 ab = b - a;
        Vector3 ac = c - a;
        if (Vector3.Cross(ab, ac).sqrMagnitude < 1e-16f)
        {
            Vector3 first = SegmentPoint(p, a, b);
            Vector3 second = SegmentPoint(p, b, c);
            Vector3 third = SegmentPoint(p, c, a);
            if ((second - p).sqrMagnitude < (first - p).sqrMagnitude) first = second;
            return (third - p).sqrMagnitude < (first - p).sqrMagnitude ? third : first;
        }
        Vector3 ap = p - a;
        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0) return a;
        Vector3 bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3) return b;
        float vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0) return a + ab * (d1 / (d1 - d3));
        Vector3 cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6) return c;
        float vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0) return a + ac * (d2 / (d2 - d6));
        float va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
            return b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6));
        return a + ab * (vb / (va + vb + vc)) + ac * (vc / (va + vb + vc));
    }
    static Vector3 SegmentPoint(Vector3 p, Vector3 a, Vector3 b)
    {
        Vector3 edge = b - a;
        if (edge.sqrMagnitude < 1e-16f) return a;
        return a + edge * Mathf.Clamp01(Vector3.Dot(p - a, edge) / edge.sqrMagnitude);
    }
}
