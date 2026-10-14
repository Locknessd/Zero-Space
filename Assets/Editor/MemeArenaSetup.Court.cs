using UnityEngine;

public static partial class MemeArenaSetup
{
    static void BuildCourt(Transform root)
    {
        var stone = Surface("Warm pavement", new Color(.35f, .34f, .31f));
        var alternate = Surface("Warm pavement variation", new Color(.37f, .36f, .33f));
        var court = Surface("Arena lavender", new Color(.22f, .24f, .31f));
        var border = Surface("Mint boundary", new Color(.21f, .48f, .43f));
        var line = Surface("Cream markings", new Color(.72f, .67f, .52f));
        for (int x = -5; x < 5; x++)
            for (int z = -5; z < 5; z++)
                Box("Meme floor tile", root, new Vector3(x * 5 + 2.5f, -.08f, z * 5 + 2.5f),
                    new Vector3(5, .16f, 5), (x * 3 + z * 7) % 4 == 0 ? alternate : stone,
                    collider: true);
        Shape("Mint combat court", root, PrimitiveType.Cylinder, new Vector3(0, -.014f, 0),
            new Vector3(17.6f, .015f, 17.6f), border);
        Shape("Open lavender combat court", root, PrimitiveType.Cylinder, new Vector3(0, -.005f, 0),
            new Vector3(17, .015f, 17), court);
        for (int side = -1; side <= 1; side += 2)
        {
            Box("Court starting mark", root, new Vector3(side * 3.2f, .015f, 0),
                new Vector3(.07f, .015f, 1.1f), line);
            for (int i = 0; i < 6; i++)
                Box("Walkway mosaic", root, new Vector3(side * (9.6f + i * .75f), .006f, 2.8f),
                    new Vector3(.28f, .012f, .65f), i % 2 == 0 ? border : line, Quaternion.Euler(0, 30, 0));
        }
        Label("BOM", root, new Vector3(0, .035f, 5.6f), 10, new Color(.5f, .5f, .6f),
            Quaternion.Euler(90, 180, 0));
        Label("MEME PLAZA", root, new Vector3(0, .035f, 7.3f), 3.6f, new Color(.57f, .58f, .67f),
            Quaternion.Euler(90, 180, 0));
    }
}
