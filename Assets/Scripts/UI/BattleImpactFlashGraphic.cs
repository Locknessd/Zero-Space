using UnityEngine;
using UnityEngine.UI;

/// <summary>Small bright impact star, generated in the HUD without full-screen white flashes.</summary>
[ExecuteAlways]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleImpactFlashGraphic : MaskableGraphic
{
    float progress = 1;
    public float Progress
    {
        get => progress;
        set
        {
            float next = Mathf.Clamp01(value);
            if (progress == next) return;
            progress = next; SetVerticesDirty();
        }
    }
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (progress >= 1) return;
        float radius = rectTransform.rect.width * .5f * Mathf.Lerp(.65f, 1, progress);
        var core = color; core.a *= Mathf.Pow(1 - progress, 2);
        var edge = core; edge.a = 0;
        for (int i = 0; i < 12; i++)
        {
            float angle = i * Mathf.PI / 6;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 side = new Vector2(-direction.y, direction.x);
            int n = vh.currentVertCount;
            float length = radius * (i % 3 == 0 ? 1 : .62f);
            vh.AddVert(direction * radius * .07f + side * radius * .07f, core, Vector2.zero);
            vh.AddVert(direction * radius * .07f - side * radius * .07f, core, Vector2.zero);
            vh.AddVert(direction * length, edge, Vector2.zero);
            vh.AddTriangle(n, n + 1, n + 2);
        }
        int index = vh.currentVertCount;
        vh.AddVert(Vector2.zero, core, Vector2.zero);
        for (int i = 0; i <= 16; i++)
        {
            float a = i * Mathf.PI / 8;
            vh.AddVert(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius * .12f, core, Vector2.zero);
            if (i > 0) vh.AddTriangle(index, index + i, index + i + 1);
        }
    }
}
