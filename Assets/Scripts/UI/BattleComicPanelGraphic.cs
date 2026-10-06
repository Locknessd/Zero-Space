using UnityEngine;
using UnityEngine.UI;

[ExecuteAlways]
[RequireComponent(typeof(CanvasRenderer))]
public sealed class BattleComicPanelGraphic : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        var r = rectTransform.rect; float skew = Mathf.Min(36, r.width * .04f);
        Quad(new Vector2(r.xMin + skew, r.yMin), new Vector2(r.xMin, r.yMax),
            new Vector2(r.xMax - skew, r.yMax), new Vector2(r.xMax, r.yMin), new Color(.04f, .025f, .06f, 1));
        Quad(new Vector2(r.xMin + skew + 7, r.yMin + 7), new Vector2(r.xMin + 7, r.yMax - 7),
            new Vector2(r.xMax - skew - 7, r.yMax - 7), new Vector2(r.xMax - 7, r.yMin + 7), color);
        for (int i = 0; i < 9; i++)
        {
            float x = Mathf.Lerp(r.xMin + r.width * .26f, r.xMax - 45, i / 8f);
            Quad(new Vector2(x, r.yMin + 8), new Vector2(x + 65, r.yMax - 8),
                new Vector2(x + 82, r.yMax - 8), new Vector2(x + 17, r.yMin + 8), new Color(1, 1, .8f, .16f));
        }
        void Quad(Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            int n = mesh.currentVertCount;
            mesh.AddVert(a, tint, Vector2.zero); mesh.AddVert(b, tint, Vector2.zero);
            mesh.AddVert(c, tint, Vector2.zero); mesh.AddVert(d, tint, Vector2.zero);
            mesh.AddTriangle(n, n + 1, n + 2); mesh.AddTriangle(n, n + 2, n + 3);
        }
    }
}
