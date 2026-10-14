using System.Collections.Generic;
using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcasePresentation
    {
        readonly List<Vector3> accentVertices = new List<Vector3>();
        readonly List<Color> accentColors = new List<Color>();
        readonly List<int> accentIndices = new List<int>();

        void DrawMotion(float seconds)
        {
            if (!motionMesh || motionFrames == null) return;
            accentVertices.Clear();
            accentColors.Clear();
            accentIndices.Clear();
            for (int index = 0; index < accents.Count; index++)
            {
                var accent = accents[index];
                float age = seconds - accent.seconds;
                if (age < 0 || age > (accent.ground ? .7f : .26f)) continue;
                if (accent.ground) DrawGroundAccent(accent, age, index);
                else DrawBladeAccent(accent, age, index);
            }
            DrawDashWake(seconds);
            DrawPowerSlashes(seconds);
            DrawAirCuts(seconds);
            DrawMuzzleBursts(seconds);
            motionMesh.Clear();
            motionMesh.SetVertices(accentVertices);
            motionMesh.SetColors(accentColors);
            motionMesh.SetTriangles(accentIndices, 0);
            motionMesh.RecalculateBounds();
            ActiveAccentTriangles = accentIndices.Count / 3;
        }

        void DrawGroundAccent(Accent accent, float age, int seed)
        {
            float fade = Mathf.Pow(1 - age / .7f, 2);
            var color = accent.color;
            color.a *= fade;
            float radius = .12f + age * 3.5f * accent.strength;
            Ring(accent.position, radius, .045f + age * .045f, color);
            if (accent.landing && age > .065f)
            {
                color.a *= .6f;
                Ring(accent.position + Vector3.up * .004f, (age - .065f) * 4.5f, .035f, color);
            }
            for (int i = 0; i < 10; i++)
            {
                float angle = i * Mathf.PI * .2f + Noise(seed + i) * .35f;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                var position = accent.position + direction * age * (1.8f + Noise(i + seed) * 1.2f);
                position.y += .06f + age * .25f;
                var dust = new Color(.48f, .8f, .86f, fade * .095f * accent.strength);
                Disc(position, .1f + age * .22f, dust);
                var glint = new Color(.65f, 1, 1, fade * .45f);
                Vector3 tip = accent.position + direction * (.15f + age * 3.4f * accent.strength);
                Quad(tip, direction * .12f * fade, Vector3.Cross(direction, Vector3.up) * .012f, glint);
            }
        }

        void DrawBladeAccent(Accent accent, float age, int seed)
        {
            float fade = Mathf.Pow(1 - age / .26f, 1.6f);
            var right = effectCamera.transform.right;
            var up = effectCamera.transform.up;
            for (int i = 0; i < 9; i++)
            {
                float angle = Noise(seed * 13 + i * 7) * Mathf.PI * 2;
                float speed = .9f + Noise(seed * 17 + i * 3) * 2;
                var direction = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                var position = accent.position + direction * age * speed;
                position.y -= age * age * 1.5f;
                var color = accent.color;
                color.a *= fade;
                var side = right * -Mathf.Sin(angle) + up * Mathf.Cos(angle);
                Quad(position, direction * (.045f + speed * .022f) * fade * accent.strength,
                    side * .012f * fade, color);
            }
            if (age < .07f)
            {
                float core = (1 - age / .07f) * .11f * accent.strength;
                Quad(accent.position, right * core, up * core * .15f, Color.white);
                Quad(accent.position, up * core, right * core * .15f, Color.white);
            }
        }

        void DrawDashWake(float seconds)
        {
            int end = Mathf.Clamp(Mathf.FloorToInt(seconds / motionStep), 1, motionFrames.Length - 1);
            int start = Mathf.Max(1, end - Mathf.CeilToInt(.13f / motionStep));
            var side = effectCamera.transform.right;
            for (int i = start; i < end; i++)
            {
                var a = motionFrames[i - 1].hips;
                var b = motionFrames[i].hips;
                float speed = (b - a).magnitude / motionStep;
                if (speed < 3.2f) continue;
                float fade = (i - start + 1f) / (end - start + 1f);
                float alpha = Mathf.Clamp01((speed - 3.2f) / 7) * fade * .28f;
                for (int streak = -1; streak <= 1; streak++)
                {
                    var offset = side * streak * .22f - Vector3.up * Mathf.Abs(streak) * .2f;
                    var color = new Color(.35f, .9f, 1, alpha);
                    Strip(a + offset, b + offset, side * .035f * fade, color);
                }
            }
        }

        void Ring(Vector3 center, float radius, float width, Color color)
        {
            const int segments = 64;
            var transparent = color;
            transparent.a = 0;
            int start = accentVertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                var direction = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
                Vertex(center + direction * Mathf.Max(0, radius - width), transparent);
                Vertex(center + direction * radius, color);
                Vertex(center + direction * (radius + width), transparent);
                if (i == 0) continue;
                int current = start + i * 3;
                for (int band = 0; band < 2; band++)
                {
                    Triangle(current + band - 3, current + band, current + band + 1);
                    Triangle(current + band - 3, current + band + 1, current + band - 2);
                }
            }
        }

        void Disc(Vector3 center, float radius, Color color)
        {
            int start = accentVertices.Count;
            Vertex(center, color);
            color.a = 0;
            for (int i = 0; i <= 12; i++)
            {
                float angle = i * Mathf.PI / 6;
                Vertex(center + radius * (effectCamera.transform.right * Mathf.Cos(angle) +
                    effectCamera.transform.up * Mathf.Sin(angle)), color);
                if (i > 0) Triangle(start, start + i, start + i + 1);
            }
        }

        void Quad(Vector3 center, Vector3 along, Vector3 side, Color color)
        {
            int start = accentVertices.Count;
            var transparent = color;
            transparent.a = 0;
            Vertex(center - along, transparent);
            Vertex(center - side, color);
            Vertex(center + along, transparent);
            Vertex(center + side, color);
            Triangle(start, start + 1, start + 2);
            Triangle(start, start + 2, start + 3);
        }

        void Strip(Vector3 a, Vector3 b, Vector3 side, Color color)
        {
            int start = accentVertices.Count;
            Vertex(a - side, color);
            Vertex(a + side, color);
            Vertex(b - side, color);
            Vertex(b + side, color);
            Triangle(start, start + 1, start + 2);
            Triangle(start + 1, start + 3, start + 2);
        }

        void Vertex(Vector3 position, Color color)
        {
            accentVertices.Add(position);
            accentColors.Add(color);
        }

        void Triangle(int a, int b, int c)
        {
            accentIndices.Add(a);
            accentIndices.Add(b);
            accentIndices.Add(c);
        }

        static float Noise(int seed)
        {
            float value = Mathf.Sin(seed * 127.1f + 311.7f) * 43758.5453f;
            return value - Mathf.Floor(value);
        }
    }
}
