using UnityEngine;

public static partial class MemeArenaSetup
{
    static void BuildDecor(Transform root)
    {
        var pot = Surface("Terracotta planters", new Color(.44f, .26f, .21f));
        var leaves = Surface("Plant leaves", new Color(.12f, .3f, .2f));
        var frog = Surface("Frog sculpture", new Color(.13f, .36f, .18f));
        var white = Surface("Sculpture eye white", new Color(.8f, .77f, .61f));
        var dark = Surface("Midnight trim", new Color(.055f, .075f, .105f));
        var gold = Surface("Meme gold", new Color(.7f, .47f, .12f));
        var wood = Surface("Park bench wood", new Color(.31f, .21f, .15f));
        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < 3; i++)
            {
                float z = -8 + i * 6;
                Box("Plaza planter", root, new Vector3(side * 11.4f, .38f, z), new Vector3(1.1f, .76f, 2), pot);
                for (int bush = 0; bush < 3; bush++)
                    Shape("Rounded plaza shrub", root, PrimitiveType.Sphere,
                        new Vector3(side * 11.4f, .95f, z - .6f + bush * .6f), new Vector3(1.15f, 1.2f, 1.1f), leaves);
            }
            Box("Park bench", root, new Vector3(side * 10.9f, .65f, -3.5f), new Vector3(.8f, .16f, 2.7f), wood);
            Box("Bench back", root, new Vector3(side * 11.3f, 1, -3.5f), new Vector3(.12f, .65f, 2.7f), wood);
            Shape("Coin monument plinth", root, PrimitiveType.Cylinder, new Vector3(side * 10.8f, .25f, -9),
                new Vector3(2.3f, .25f, 2.3f), pot);
            for (int coin = 0; coin < 4; coin++)
                Shape("Stack of meme coins", root, PrimitiveType.Cylinder,
                    new Vector3(side * 10.8f + coin % 2 * .1f, .57f + coin * .2f, -9),
                    new Vector3(1.55f, .075f, 1.55f), gold);
            Shape("Golden meme coin", root, PrimitiveType.Cylinder, new Vector3(side * 10.8f, 2.25f, -9),
                new Vector3(2, .1f, 2), gold, Quaternion.Euler(90, 0, 0));
        }
        Box("Frog statue pedestal", root, new Vector3(-12.6f, .55f, -6.7f), new Vector3(2.4f, 1.1f, 2), pot);
        Shape("Pepe head sculpture", root, PrimitiveType.Sphere, new Vector3(-12.6f, 2.1f, -6.7f),
            new Vector3(2.1f, 1.6f, 1.3f), frog);
        for (int side = -1; side <= 1; side += 2)
        {
            Shape("Frog statue eye", root, PrimitiveType.Sphere, new Vector3(-12.6f + side * .48f, 2.65f, -6.15f),
                new Vector3(.72f, .58f, .3f), white);
            Shape("Frog statue pupil", root, PrimitiveType.Sphere, new Vector3(-12.6f + side * .48f, 2.65f, -5.98f),
                new Vector3(.24f, .3f, .08f), dark);
        }
        Box("Frog statue smile", root, new Vector3(-12.6f, 1.93f, -5.98f), new Vector3(1.3f, .13f, .1f),
            Surface("Frog smile", new Color(.45f, .2f, .13f)));
        for (int side = -1; side <= 1; side += 2)
        {
            Box("Plaza lantern pole", root, new Vector3(side * 12.3f, 2.6f, 3.8f), new Vector3(.1f, 5.2f, .1f), dark);
            Shape("Warm plaza lantern", root, PrimitiveType.Sphere, new Vector3(side * 12.3f, 5.1f, 3.8f),
                Vector3.one * .65f, white);
        }
    }
}
