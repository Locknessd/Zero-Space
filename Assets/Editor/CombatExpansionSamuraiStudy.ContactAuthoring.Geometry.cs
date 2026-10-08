using UnityEngine;
namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static class CAGeometry
        {
            public struct Gap
            {
                public float squared;
                public Vector3 blade;
                public Vector3 body;
            }
        public static float BoundsGap(Bounds a, Bounds b)
        {
            Vector3 gap = Vector3.Max(Vector3.zero, Vector3.Max(a.min - b.max, b.min - a.max));
            return gap.sqrMagnitude;
        }

        public static void CompareTriangles(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 e, Vector3 f,
            ref Gap best)
        {
            Compare(a, TrianglePoint(a, d, e, f), ref best);
            Compare(b, TrianglePoint(b, d, e, f), ref best);
            Compare(c, TrianglePoint(c, d, e, f), ref best);
            Compare(TrianglePoint(d, a, b, c), d, ref best);
            Compare(TrianglePoint(e, a, b, c), e, ref best);
            Compare(TrianglePoint(f, a, b, c), f, ref best);
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = i == 0 ? a : i == 1 ? b : c;
                Vector3 q = i == 0 ? b : i == 1 ? c : a;
                if (Pierces(p, q, d, e, f, out var hit))
                {
                    best = new Gap { squared = 0, blade = hit, body = hit };
                    return;
                }
                Vector3 r = i == 0 ? d : i == 1 ? e : f;
                Vector3 s = i == 0 ? e : i == 1 ? f : d;
                if (Pierces(r, s, a, b, c, out hit))
                {
                    best = new Gap { squared = 0, blade = hit, body = hit };
                    return;
                }
                Segments(p, q, d, e, ref best);
                Segments(p, q, e, f, ref best);
                Segments(p, q, f, d, ref best);
            }
        }

        static void Compare(Vector3 a, Vector3 b, ref Gap best)
        {
            float distance = (a - b).sqrMagnitude;
            if (distance >= best.squared)
                return;
            best = new Gap { squared = distance, blade = a, body = b };
        }

        static bool Pierces(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c, out Vector3 hit)
        {
            hit = default;
            Vector3 direction = q - p;
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 h = Vector3.Cross(direction, ac);
            float det = Vector3.Dot(ab, h);
            if (Mathf.Abs(det) < 1e-10f)
                return false;
            float inverse = 1 / det;
            Vector3 offset = p - a;
            float u = inverse * Vector3.Dot(offset, h);
            Vector3 cross = Vector3.Cross(offset, ab);
            float v = inverse * Vector3.Dot(direction, cross);
            float t = inverse * Vector3.Dot(ac, cross);
            if (u < 0 || v < 0 || u + v > 1 || t < 0 || t > 1)
                return false;
            hit = p + direction * t;
            return true;
        }

        static void Segments(Vector3 p, Vector3 q, Vector3 r, Vector3 s, ref Gap best)
        {
            Vector3 d1 = q - p;
            Vector3 d2 = s - r;
            Vector3 offset = p - r;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, offset);
            float u;
            float v;
            if (a <= 1e-16f)
            {
                u = 0;
                v = e <= 1e-16f ? 0 : Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, offset);
                if (e <= 1e-16f)
                {
                    v = 0;
                    u = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denominator = a * e - b * b;
                    u = denominator > 1e-16f ? Mathf.Clamp01((b * f - c * e) / denominator) : 0;
                    v = (b * u + f) / e;
                    if (v < 0)
                    {
                        v = 0;
                        u = Mathf.Clamp01(-c / a);
                    }
                    else if (v > 1)
                    {
                        v = 1;
                        u = Mathf.Clamp01((b - c) / a);
                    }
                }
            }
            Compare(p + d1 * u, r + d2 * v, ref best);
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
                if ((second - p).sqrMagnitude < (first - p).sqrMagnitude)
                    first = second;
                return (third - p).sqrMagnitude < (first - p).sqrMagnitude ? third : first;
            }
            Vector3 ap = p - a;
            float d1 = Vector3.Dot(ab, ap);
            float d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0)
                return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp);
            float d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3)
                return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0)
                return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp);
            float d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6)
                return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0)
                return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
                return b + (c - b) * ((d4 - d3) / (d4 - d3 + d5 - d6));
            return a + ab * (vb / (va + vb + vc)) + ac * (vc / (va + vb + vc));
        }
        static Vector3 SegmentPoint(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 edge = b - a;
            if (edge.sqrMagnitude < 1e-16f)
                return a;
            return a + edge * Mathf.Clamp01(Vector3.Dot(p - a, edge) / edge.sqrMagnitude);
        }
        }
    }
}
