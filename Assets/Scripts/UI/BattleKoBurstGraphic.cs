using UnityEngine;
using UnityEngine.UI;

/// <summary>Canvas-native impact rays: no particle shader or imported texture required.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleKoBurstGraphic : MaskableGraphic
{
    [SerializeField, Range(0, 1)] float progress = 1;
    public float Progress
    {
        get => progress;
        set { progress = Mathf.Clamp01(value); SetVerticesDirty(); }
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        float alpha = Mathf.Pow(1 - progress, 1.4f) * .72f;
        if (alpha <= .001f) return;
        float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .58f;
        for (int i = 0; i < 24; i++)
        {
            float angle = (i * 15f + (i % 3 - 1) * 3) * Mathf.Deg2Rad;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 tangent = new Vector2(-direction.y, direction.x);
            float inner = 80 + progress * 110;
            float outer = (160 + progress * radius) * (1 + (i % 5) * .07f);
            float width = (1 - progress) * (12 + i % 4 * 5);
            int index = mesh.currentVertCount;
            Color tip = new Color(1, .12f, .02f, 0);
            Color core = new Color(1, .65f, .12f, alpha);
            mesh.AddVert(direction * inner + tangent * width, core, Vector2.zero);
            mesh.AddVert(direction * inner - tangent * width, core, Vector2.zero);
            mesh.AddVert(direction * outer, tip, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
        }
    }
}
