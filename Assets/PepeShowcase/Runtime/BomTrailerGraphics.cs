using UnityEngine;
using UnityEngine.UI;

namespace ZeroSpace.Showcase
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class BomTrailerGraphics : MaskableGraphic
    {
        public bool nameCard;

        protected override void OnPopulateMesh(VertexHelper helper)
        {
            helper.Clear();
            if (nameCard)
            {
                Stroke(helper, new Vector2(-510, -90), new Vector2(650, 160), 78,
                    new Color(.025f, .04f, .1f, .94f));
                Stroke(helper, new Vector2(-610, -20), new Vector2(600, 140), 36,
                    new Color(.05f, .9f, 1, .98f));
                Stroke(helper, new Vector2(-560, -155), new Vector2(700, 36), 22,
                    new Color(1, .08f, .44f, .96f));
                Stroke(helper, new Vector2(-650, 70), new Vector2(480, 215), 6,
                    new Color(.4f, 1, 1, .85f));
                for (int i = 0; i < 30; i++)
                {
                    float x = -560 + i * 43;
                    float y = (i % 5 - 2) * 57 + x * .16f;
                    Dot(helper, new Vector2(x, y), 2 + i % 5, new Color(.2f, .9f, 1, .5f));
                }
            }
            else
            {
                for (int i = -5; i <= 5; i++)
                    Stroke(helper, new Vector2(-1100, i * 100 - 200), new Vector2(1100, i * 100 + 200),
                        1, new Color(.15f, .6f, .8f, .08f));
                Stroke(helper, new Vector2(-1100, -180), new Vector2(-270, -65), 13,
                    new Color(.02f, .85f, 1, .8f));
                Stroke(helper, new Vector2(300, 90), new Vector2(1100, 230), 12,
                    new Color(1, .06f, .4f, .8f));
                Stroke(helper, new Vector2(-1050, 250), new Vector2(-400, 340), 2,
                    new Color(1, .2f, .5f, .5f));
                for (int row = 0; row < 7; row++)
                    for (int col = 0; col < 12; col++)
                        Dot(helper, new Vector2(620 + col * 26, -380 + row * 26), 2,
                            new Color(.2f, .85f, 1, .22f));
            }
        }

        static void Stroke(VertexHelper helper, Vector2 start, Vector2 end, float width, Color color)
        {
            var side = Vector2.Perpendicular((end - start).normalized) * width;
            int index = helper.currentVertCount;
            helper.AddVert(start - side * .7f, color, Vector2.zero);
            helper.AddVert(start + side, color, Vector2.up);
            helper.AddVert(end + side * .7f, color, Vector2.one);
            helper.AddVert(end - side, color, Vector2.right);
            helper.AddTriangle(index, index + 1, index + 2);
            helper.AddTriangle(index, index + 2, index + 3);
        }

        static void Dot(VertexHelper helper, Vector2 center, float radius, Color color)
        {
            int start = helper.currentVertCount;
            helper.AddVert(center, color, Vector2.zero);
            for (int i = 0; i <= 8; i++)
            {
                float angle = i * Mathf.PI / 4;
                helper.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius,
                    color, Vector2.zero);
                if (i > 0) helper.AddTriangle(start, start + i, start + i + 1);
            }
        }
    }
}
