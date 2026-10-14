using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcasePresentation
    {
        void DrawMuzzleExplosion(Vector3 origin, Vector3 direction, float age)
        {
            var right = effectCamera.transform.right;
            var up = effectCamera.transform.up;
            var side = Vector3.Cross(direction, effectCamera.transform.forward).normalized;
            if (side.sqrMagnitude < .1f) side = right;
            float hot = Mathf.Clamp01(1 - age / .12f);
            float sparks = Mathf.Pow(Mathf.Clamp01(1 - age / .25f), 1.5f);
            if (hot > 0)
            {
                var muzzle = origin + direction * .018f;
                Disc(muzzle, .34f * Mathf.Sqrt(hot), new Color(3.2f, 1.4f, .15f, hot * .48f));
                Disc(muzzle, .14f * hot + .035f, new Color(3.5f, 3.1f, 2, hot));
                Quad(muzzle, right * .38f * hot, up * .04f * hot, new Color(3, 1.8f, .45f, hot));
                Quad(muzzle, up * .26f * hot, right * .035f * hot, new Color(3, 1.8f, .45f, hot));
                MuzzleJet(muzzle, direction, side, .75f * hot, .11f * hot,
                    new Color(3.1f, 1.5f, .16f, hot * .85f));
                MuzzleJet(muzzle, direction, side, .5f * hot, .04f * hot,
                    new Color(3.5f, 3, 1.8f, hot));
                MuzzlePressureRing(muzzle, direction, .08f + age * 3.2f,
                    new Color(2.4f, 1.25f, .25f, hot * .55f));
            }
            for (int i = 0; i < 12 && sparks > 0; i++)
            {
                float angle = i * Mathf.PI / 6 + Noise(i + 13) * .5f;
                var radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                var velocity = direction * (1.1f + Noise(i + 19) * 2) + radial * (1.2f + Noise(i + 7));
                var point = origin + velocity * age - Vector3.up * age * age * 1.8f;
                var cross = Vector3.Cross(velocity.normalized, effectCamera.transform.forward).normalized;
                Quad(point, velocity.normalized * (.055f + age * .16f) * sparks,
                    cross * .012f * sparks, new Color(3, 1.3f, .12f, sparks * .85f));
            }
            float smoke = Mathf.Clamp01(age / .06f) * Mathf.Clamp01(1 - age / .38f);
            for (int i = 0; i < 3 && smoke > 0; i++)
            {
                var point = origin + direction * age * .65f + up * age * (.3f + i * .15f);
                point += right * (i - 1) * age * .15f;
                Disc(point, .06f + age * (.3f + i * .07f), new Color(.35f, .38f, .42f, smoke * .18f));
            }
        }

        void MuzzleJet(Vector3 origin, Vector3 direction, Vector3 side, float length, float width, Color color)
        {
            int index = accentVertices.Count;
            var clear = color;
            clear.a = 0;
            Vertex(origin - side * width, clear);
            Vertex(origin, color);
            Vertex(origin + side * width, clear);
            Vertex(origin + direction * length, clear);
            Triangle(index, index + 1, index + 3);
            Triangle(index + 1, index + 2, index + 3);
        }

        void MuzzlePressureRing(Vector3 origin, Vector3 direction, float radius, Color color)
        {
            var rotation = Quaternion.FromToRotation(Vector3.forward, direction);
            var right = rotation * Vector3.right;
            var up = rotation * Vector3.up;
            for (int i = 0; i < 32; i++)
            {
                float a = i * Mathf.PI / 16;
                float b = (i + 1) * Mathf.PI / 16;
                var first = origin + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius;
                var last = origin + (right * Mathf.Cos(b) + up * Mathf.Sin(b)) * radius;
                Strip(first, last, (first - origin).normalized * .012f, color);
            }
        }
    }
}
