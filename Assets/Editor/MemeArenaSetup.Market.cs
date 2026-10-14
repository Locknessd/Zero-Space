using UnityEngine;

public static partial class MemeArenaSetup
{
    static void BuildMarket(Transform root)
    {
        var cream = Surface("Cream plaster", new Color(.52f, .46f, .35f));
        var lilac = Surface("Lilac plaster", new Color(.39f, .34f, .48f));
        var mint = Surface("Mint plaster", new Color(.22f, .4f, .37f));
        var coral = Surface("Coral plaster", new Color(.52f, .29f, .25f));
        var dark = Surface("Midnight trim", new Color(.055f, .075f, .105f));
        var glass = Surface("Teal windows", new Color(.08f, .22f, .24f));
        var colors = new[] { mint, cream, lilac, coral };
        string[] names = { "PEPE'S", "DOGE CAFE", "BANANA CLUB", "WEN MOON?" };
        for (int shop = 0; shop < 4; shop++)
        {
            float x = (shop - 1.5f) * 5.5f;
            Box("Meme market shop", root, new Vector3(x, 3.15f, -14), new Vector3(5.2f, 6.3f, 3), colors[shop]);
            Box("Shop cornice", root, new Vector3(x, 6.35f, -13.7f), new Vector3(5.45f, .22f, 3.5f), dark);
            Box("Poster frame", root, new Vector3(x, 4.25f, -12.42f), new Vector3(3.7f, 3.7f, .2f), dark);
            Shape("Meme graffiti mural", root, PrimitiveType.Quad, new Vector3(x, 4.25f, -12.28f),
                new Vector3(3.5f, 3.5f, 1), Poster(shop), Quaternion.Euler(0, 180, 0));
            Box("Cafe window", root, new Vector3(x, 1.05f, -12.42f), new Vector3(3.7f, 1.75f, .2f), glass);
            Box("Shop canopy", root, new Vector3(x, 2.05f, -11.85f), new Vector3(4.7f, .16f, 1.5f), cream,
                Quaternion.Euler(-8, 0, 0));
            Label(names[shop], root, new Vector3(x, 6.9f, -12.15f), 3.4f, new Color(.88f, .82f, .66f));
            for (int stripe = 0; stripe < 8; stripe++)
                Box("Candy stripe canopy", root, new Vector3(x - 2.05f + stripe * .58f, 2.1f, -11.85f),
                    new Vector3(.29f, .025f, 1.45f), colors[shop], Quaternion.Euler(-8, 0, 0));
        }
        Box("Plaza entrance header", root, new Vector3(0, 8.1f, -13.9f), new Vector3(12.8f, 1.35f, .5f), dark);
        Label("BOM / BATTLE OF MEME", root, new Vector3(0, 8.1f, -13.55f), 5.3f,
            new Color(.87f, .82f, .69f));
        for (int i = 0; i < 17; i++)
        {
            float x = -12 + i * 1.5f;
            float height = 7.7f - 1.15f * (1 - Mathf.Pow(x / 12, 2));
            var pennant = Shape("Market bunting", root, PrimitiveType.Cube, new Vector3(x, height, -10.8f),
                new Vector3(.48f, .58f, .035f), colors[i % colors.Length], Quaternion.Euler(0, 0, i % 2 == 0 ? 9 : -9));
        }
        for (int i = 0; i < 15; i++)
        {
            float height = 5 + (i * 7 % 9);
            Box("Meme city skyline", root, new Vector3(-28 + i * 4, height / 2, -29 - i % 3 * 3),
                new Vector3(3.5f, height, 4), i % 2 == 0 ? lilac : mint);
        }
    }
}
