using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcasePresentation
    {
        void AirSpan(Vector3 a, Vector3 b, Vector3 radialA, Vector3 radialB, float fade)
        {
            var halo = new Color(.06f, 1.2f, 2.4f, fade * .3f);
            var core = new Color(.65f, 2, 2.5f, fade * .78f);
            var invisible = new Color(.04f, .8f, 1.8f, 0);
            float width = .035f;
            AirBand(a - radialA * width, a, b - radialB * width, b, invisible, core);
            AirBand(a, a + radialA * width, b, b + radialB * width, core, invisible);
            AirBand(a - radialA * width * 2.5f, a, b - radialB * width * 2.5f, b, invisible, halo);
            AirBand(a, a + radialA * width * 2.5f, b, b + radialB * width * 2.5f, halo, invisible);
            // A faint neighboring contour gives the suspended cut some depth.
            var secondA = a + radialA * .1f;
            var secondB = b + radialB * .1f;
            AirBand(secondA - radialA * .012f, secondA + radialA * .012f,
                secondB - radialB * .012f, secondB + radialB * .012f,
                new Color(.12f, 1.2f, 2, fade * .15f), new Color(.12f, 1.2f, 2, 0));
        }

        void AirBand(Vector3 a, Vector3 outerA, Vector3 b, Vector3 outerB, Color inner, Color outer)
        {
            int index = accentVertices.Count;
            Vertex(a, inner);
            Vertex(outerA, outer);
            Vertex(b, inner);
            Vertex(outerB, outer);
            Triangle(index, index + 1, index + 2);
            Triangle(index + 1, index + 3, index + 2);
        }

        void DrawAirMotes(AirCut cut, float to, float age, float fade)
        {
            for (int i = 0; i < 5; i++)
            {
                float fraction = (i + .4f) / 5;
                float time = Mathf.Lerp(cut.start, cut.end, fraction);
                if (time > to) continue;
                AirPoint(cut, time, out var point, out var root, out var radial);
                point += Vector3.up * age * (.08f + Noise(i + cut.take * 11) * .12f);
                point += radial * Mathf.Sin(i * 5 + age * 4) * .025f;
                float pulse = .65f + .35f * Mathf.Sin(i * 3 + age * 15);
                float size = .018f * fade * pulse;
                var color = new Color(.4f, 1.6f, 2, fade * .65f);
                Quad(point, effectCamera.transform.right * size * 2, effectCamera.transform.up * size * .4f, color);
                Quad(point, effectCamera.transform.up * size * 2, effectCamera.transform.right * size * .4f, color);
            }
        }
    }
}
