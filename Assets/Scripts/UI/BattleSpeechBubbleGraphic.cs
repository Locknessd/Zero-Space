using UnityEngine;
using UnityEngine.UI;

/// <summary>Resolution-independent comic bubble: rounded body, border, tail and soft offset shadow.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleSpeechBubbleGraphic : MaskableGraphic
{
    public Color fill = new Color(1, .985f, .95f, 1);
    public Color border = new Color(.08f, .65f, .76f, 1);
    public Color shadow = new Color(.015f, .02f, .035f, .32f);
    public bool tailOnRight;
    public float radius = 22;
    public float borderWidth = 3;

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        if (rect.width <= 0 || rect.height <= 0) return;
        var shifted = new Rect(rect.position + new Vector2(0, -7), rect.size);
        Bubble(mesh, shifted, radius, shadow, 0);
        Bubble(mesh, rect, radius, border, 0);
        Rect inner = new Rect(rect.xMin + borderWidth, rect.yMin + borderWidth,
            rect.width - 2 * borderWidth, rect.height - 2 * borderWidth);
        Bubble(mesh, inner, Mathf.Max(0, radius - borderWidth), fill, borderWidth);
    }

    void Bubble(VertexHelper mesh, Rect rect, float corner, Color tint, float inset)
    {
        RoundedRect(mesh, rect, corner, tint);
        float x = tailOnRight ? rect.xMax - 38 : rect.xMin + 38;
        float direction = tailOnRight ? 1 : -1;
        // The top corner points outward and up, towards the fighter portrait beside the card.
        Triangle(mesh, new Vector2(x - 17, rect.yMax - 2), new Vector2(x + 17, rect.yMax - 2),
            new Vector2(x + direction * 46, rect.yMax + 22 - inset * 1.3f), tint);
    }

    static void RoundedRect(VertexHelper mesh, Rect rect, float radius, Color tint)
    {
        radius = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * .5f);
        int start = mesh.currentVertCount;
        mesh.AddVert(rect.center, tint, Vector2.zero);
        const int steps = 6;
        for (int corner = 0; corner < 4; corner++)
        {
            Vector2 center = new Vector2(corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius,
                corner < 2 ? rect.yMax - radius : rect.yMin + radius);
            for (int step = 0; step <= steps; step++)
            {
                float angle = (corner * 90 + step * 90f / steps) * Mathf.Deg2Rad;
                mesh.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
            }
        }
        int count = 4 * (steps + 1);
        for (int i = 0; i < count; i++) mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % count);
    }

    static void Triangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Color tint)
    {
        int start = mesh.currentVertCount;
        mesh.AddVert(a, tint, Vector2.zero); mesh.AddVert(b, tint, Vector2.zero); mesh.AddVert(c, tint, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
    }
}
