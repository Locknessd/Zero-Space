using UnityEngine;
using UnityEngine.UI;

/// <summary>Gold-to-red vertex gradient for standard Unity UI Text.</summary>
public sealed class BattleTextGradient : BaseMeshEffect
{
    public Color top = new Color(1, .9f, .45f, 1);
    public Color bottom = new Color(.9f, .09f, .03f, 1);

    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || mesh.currentVertCount == 0) return;
        float min = float.PositiveInfinity, max = float.NegativeInfinity;
        var vertex = new UIVertex();
        for (int i = 0; i < mesh.currentVertCount; i++)
        {
            mesh.PopulateUIVertex(ref vertex, i);
            min = Mathf.Min(min, vertex.position.y);
            max = Mathf.Max(max, vertex.position.y);
        }
        for (int i = 0; i < mesh.currentVertCount; i++)
        {
            mesh.PopulateUIVertex(ref vertex, i);
            vertex.color = (Color)vertex.color * Color.Lerp(bottom, top, Mathf.InverseLerp(min, max, vertex.position.y));
            mesh.SetUIVertex(vertex, i);
        }
    }
}
