using UnityEngine;
using UnityEngine.UI;

/// <summary>Comic ink and graffiti accents matching the battle's gold impacts and fighter HUD.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleKoInkGraphic : MaskableGraphic
{
    [SerializeField, Range(0, 1)] float progress;

    public float Progress
    {
        get => progress;
        set
        {
            progress = Mathf.Clamp01(value);
            SetVerticesDirty();
        }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        if (progress <= .001f)
            return;
        float spread = 1 - Mathf.Pow(1 - progress, 3);
        var pink = new Color(.86f, .025f, .37f);
        var cyan = new Color(.02f, .77f, .9f);
        var gold = new Color(1, .68f, .09f);
        var violet = new Color(.23f, .018f, .37f);
        var dark = new Color(.045f, .015f, .075f);
        Stroke(mesh, new Vector2(-450, -115), new Vector2(405, 175), 135, dark, dark, 2, spread);
        Stroke(mesh, new Vector2(-430, 130), new Vector2(435, -80), 132, dark, dark, 5, spread);
        Stroke(mesh, new Vector2(-380, -155), new Vector2(320, 200), 105, dark, dark, 8, spread);
        Stroke(mesh, new Vector2(-415, 20), new Vector2(460, 105), 128, violet, dark, 3, spread);
        Stroke(mesh, new Vector2(-345, -95), new Vector2(390, -115), 120, dark, violet, 7, spread);
        Stroke(mesh, new Vector2(-300, -145), new Vector2(360, 150), 125,
            new Color(.16f, .02f, .27f), new Color(.25f, .025f, .43f), 11, spread);
        Stroke(mesh, new Vector2(-450, 156), new Vector2(-218, 204), 17, pink, gold, 13, spread);
        Stroke(mesh, new Vector2(175, -181), new Vector2(430, -125), 14, gold, pink, 17, spread);
        Stroke(mesh, new Vector2(-425, -135), new Vector2(-195, -171), 5, cyan, cyan, 19, spread);
        for (int i = 0; i < 12; i++)
        {
            float angle = (i * 137.5f + 19) * Mathf.Deg2Rad;
            var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var center = new Vector2(direction.x * (390 + i % 3 * 27), direction.y * (176 + i % 4 * 12));
            float scatter = Mathf.SmoothStep(0, 1, Mathf.Clamp01((progress - .12f) / .65f));
            Droplet(mesh, center * (.72f + .28f * scatter), (5 + i % 4 * 3) * scatter,
                i % 3 == 0 ? gold : i % 3 == 1 ? pink : violet);
        }
        for (int i = 0; i < 7; i++)
        {
            float y = -128 + i * 41;
            Stroke(mesh, new Vector2(-305 + i * 11, y), new Vector2(265 + i * 13, y + 35),
                2 + i % 3, new Color(.92f, .4f, .61f, .12f),
                new Color(1, .83f, .38f, .08f), 15 + i, spread);
        }
    }

    void Stroke(VertexHelper mesh, Vector2 start, Vector2 end, float width,
        Color leftColor, Color rightColor, int seed, float spread)
    {
        const int segments = 24;
        var direction = (end - start).normalized;
        var normal = new Vector2(-direction.y, direction.x);
        int first = mesh.currentVertCount;
        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;
            var center = Vector2.Lerp(start, end, t);
            center += normal * Mathf.Sin(t * Mathf.PI) * (seed % 3 - 1) * 28;
            center.x *= spread;
            center.y *= .72f + .28f * spread;
            float cap = Mathf.Min(1, t * 9, (1 - t) * 7);
            float top = width * cap * (1 + Noise(i, seed) * .22f);
            float bottom = width * cap * (1 + Noise(i + 31, seed) * .2f);
            Color tint = Color.Lerp(leftColor, rightColor, t);
            tint.a *= Mathf.Clamp01(progress * 5) * color.a;
            AddVertex(mesh, center + normal * top, tint);
            AddVertex(mesh, center - normal * bottom, tint);
            if (i == 0)
                continue;
            int index = first + i * 2;
            mesh.AddTriangle(index - 2, index - 1, index);
            mesh.AddTriangle(index, index - 1, index + 1);
        }
    }

    void Droplet(VertexHelper mesh, Vector2 center, float radius, Color tint)
    {
        if (radius <= .01f)
            return;
        const int segments = 12;
        tint.a *= color.a;
        int first = mesh.currentVertCount;
        AddVertex(mesh, center, tint);
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            var offset = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            AddVertex(mesh, center + offset, tint);
            if (i > 0)
                mesh.AddTriangle(first, first + i, first + i + 1);
        }
    }

    void AddVertex(VertexHelper mesh, Vector2 point, Color tint)
    {
        var size = rectTransform.rect.size;
        point = new Vector2(point.x * size.x / 1000, point.y * size.y / 560);
        mesh.AddVert(point, tint, Vector2.zero);
    }

    static float Noise(int index, int seed) =>
        Mathf.Sin(index * 2.71f + seed * 1.93f) * Mathf.Cos(index * 1.17f + seed);
}
