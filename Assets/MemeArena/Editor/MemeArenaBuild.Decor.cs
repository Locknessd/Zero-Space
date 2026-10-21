using UnityEngine;

namespace MemeArenaEditor
{
    public static partial class MemeArenaBuild
    {
        static void BuildDecor(Transform parent)
        {
            float rear = settings.RearSign * (settings.colosseumRadius + 5);
            var statue = Group(parent, "Emperor Doge — replaceable placeholder socket");
            statue.localPosition = new Vector3(0, settings.seatingTiers * .74f + 2.1f, rear);
            if (settings.statuePrefab)
            {
                var replacement = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(settings.statuePrefab);
                replacement.transform.SetParent(statue, false);
            }
            else
            {
                Box(statue, "Emperor plinth", new Vector3(0, -.4f, 0), new Vector3(4.5f, .8f, 2.9f), stone);
                Box(statue, "Gold plinth lip", Vector3.zero, new Vector3(4.7f, .14f, 3), gold);
                Shape(statue, "Doge golden bust", PrimitiveType.Sphere, new Vector3(0, 1.1f, 0),
                    new Vector3(3.2f, 2.1f, 1.6f), red);
                Shape(statue, "Doge head placeholder", PrimitiveType.Sphere, new Vector3(0, 2.65f, 0),
                    new Vector3(2.7f, 2.4f, 1.9f), gold);
                float face = -settings.RearSign;
                Shape(statue, "Carved muzzle", PrimitiveType.Sphere, new Vector3(0, 2.25f, face * .87f),
                    new Vector3(1.5f, .9f, .68f), cream);
                Shape(statue, "Doge nose", PrimitiveType.Sphere, new Vector3(0, 2.55f, face * 1.18f),
                    new Vector3(.38f, .27f, .23f), dark);
                for (int side = -1; side <= 1; side += 2)
                {
                    Shape(statue, "Doge eye", PrimitiveType.Sphere, new Vector3(side * .58f, 2.95f, face * .86f),
                        new Vector3(.22f, .16f, .12f), dark);
                    Box(statue, "Doge ear", new Vector3(side * .85f, 3.7f, 0), new Vector3(.57f, 1.1f, .55f),
                        gold, Quaternion.Euler(0, 0, side * -20));
                }
                Box(statue, "Imperial crown band", new Vector3(0, 3.87f, 0), new Vector3(1.5f, .2f, 1), cream);
                for (int spike = -1; spike <= 1; spike++)
                    Box(statue, "Crown ray", new Vector3(spike * .54f, 4.15f, 0),
                        new Vector3(.18f, .6f, .27f), gold, Quaternion.Euler(0, 0, spike * -20));
            }
            for (int i = -2; i <= 2; i++)
            {
                if (i == 0) continue;
                var socket = Group(parent, "Imperial banner socket");
                socket.localPosition = new Vector3(i * 5.4f, settings.seatingTiers * .74f + 4.5f,
                    rear - settings.RearSign * 1.8f);
                if (settings.bannerPrefab)
                {
                    var replacement = (GameObject)UnityEditor.PrefabUtility.InstantiatePrefab(settings.bannerPrefab);
                    replacement.transform.SetParent(socket, false);
                }
                else
                {
                    Box(socket, "Red banner", Vector3.zero, new Vector3(1.7f, 3.8f, .09f), red);
                    Box(socket, "Gold banner rail", new Vector3(0, 1.94f, 0), new Vector3(2, .14f, .16f), gold);
                    Box(socket, "Banner gold stripe", new Vector3(0, -1.4f, -settings.RearSign * .065f),
                        new Vector3(1.7f, .18f, .05f), gold);
                    Shape(socket, "Banner sun medallion", PrimitiveType.Sphere,
                        new Vector3(0, .2f, -settings.RearSign * .09f), new Vector3(.7f, .7f, .08f), gold);
                }
            }
        }
    }
}
