using UnityEditor;
using UnityEngine;

namespace MemeArenaEditor
{
    public static partial class MemeArenaBuild
    {
        static void BuildFloor(Transform parent)
        {
            if (!settings.includeVisualFloor) return;
            float diameter = 2 * (settings.colosseumRadius + settings.seatingTiers * 1.18f + 2);
            Shape(parent, "Continuous colosseum foundation", PrimitiveType.Cylinder,
                new Vector3(0, -.25f, settings.RearSign), new Vector3(diameter, .22f, diameter), stone);
            Box(parent, "Fighting floor — top Y0, no collider", new Vector3(0, -.22f, 0),
                new Vector3(settings.arenaWidth, .44f, settings.arenaDepth), sand);
            float halfX = settings.arenaWidth * .5f;
            float halfZ = settings.arenaDepth * .5f;
            for (int side = -1; side <= 1; side += 2)
            {
                Box(parent, "Gold floor inlay", new Vector3(0, -.004f, side * (halfZ - .5f)),
                    new Vector3(settings.arenaWidth - 1, .012f, .09f), gold);
                Box(parent, "Gold floor inlay", new Vector3(side * (halfX - .5f), -.004f, 0),
                    new Vector3(.09f, .012f, settings.arenaDepth - 1), gold);
            }
        }

        static void BuildSeating(Transform parent)
        {
            for (int tier = 0; tier < settings.seatingTiers; tier++)
            {
                float radius = settings.colosseumRadius + tier * 1.18f;
                float height = .35f + tier * .74f;
                var mesh = RingMesh(radius, radius + 1.3f, height - .38f, height + .3f);
                AssetDatabase.CreateAsset(mesh, folder + "/Meshes/Tier_" + tier + ".asset");
                var item = new GameObject("Curved spectator tier " + (tier + 1));
                item.transform.SetParent(parent, false);
                item.AddComponent<MeshFilter>().sharedMesh = mesh;
                item.AddComponent<MeshRenderer>().sharedMaterial = tier % 2 == 0 ? stone : cream;
            }
        }

        static Mesh RingMesh(float inner, float outer, float low, float high)
        {
            var vertices = new System.Collections.Generic.List<Vector3>();
            var triangles = new System.Collections.Generic.List<int>();
            var uv = new System.Collections.Generic.List<Vector2>();
            for (int segment = 0; segment < settings.arcSegments; segment++)
            {
                float a = Mathf.Lerp(-settings.arcDegrees * .5f, settings.arcDegrees * .5f,
                    segment / (float)settings.arcSegments) * Mathf.Deg2Rad;
                float b = Mathf.Lerp(-settings.arcDegrees * .5f, settings.arcDegrees * .5f,
                    (segment + 1f) / settings.arcSegments) * Mathf.Deg2Rad;
                var p = new[] { Arc(inner, low, a), Arc(inner, low, b), Arc(outer, low, b), Arc(outer, low, a),
                    Arc(inner, high, a), Arc(inner, high, b), Arc(outer, high, b), Arc(outer, high, a) };
                foreach (var face in new[] { new[] { 4, 5, 6, 7 }, new[] { 0, 3, 2, 1 },
                    new[] { 0, 1, 5, 4 }, new[] { 3, 7, 6, 2 }, new[] { 0, 4, 7, 3 }, new[] { 1, 2, 6, 5 } })
                {
                    int start = vertices.Count;
                    for (int i = 0; i < 4; i++)
                    {
                        vertices.Add(p[face[i]]);
                        uv.Add(new Vector2(i == 1 || i == 2 ? 1 : 0, i >= 2 ? 1 : 0));
                    }
                    triangles.AddRange(settings.RearSign > 0 ?
                        new[] { start, start + 2, start + 1, start, start + 3, start + 2 } :
                        new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
                }
            }
            var mesh = new Mesh { name = "Curved limestone tier" };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static Vector3 Arc(float radius, float height, float radians) =>
            new Vector3(radius * Mathf.Sin(radians), height, settings.RearSign * (1 + radius * Mathf.Cos(radians)));

        static void BuildArcades(Transform parent)
        {
            float radius = settings.colosseumRadius + settings.seatingTiers * 1.18f + .8f;
            float baseY = settings.seatingTiers * .74f;
            float arcadeSpan = settings.arcDegrees - 10;
            int bays = Mathf.Clamp(Mathf.FloorToInt(radius * arcadeSpan * Mathf.Deg2Rad / 3.35f) + 1, 19, 60);
            for (int level = 0; level < 2; level++)
                for (int bay = 0; bay < bays; bay++)
                {
                    float angle = Mathf.Lerp(-arcadeSpan * .5f, arcadeSpan * .5f, bay / (float)(bays - 1));
                    var socket = Group(parent, "Roman arcade " + level + " / " + bay);
                    socket.localPosition = Arc(radius, baseY + level * 3.25f, angle * Mathf.Deg2Rad);
                    socket.localRotation = Quaternion.Euler(0, settings.RearSign * angle, 0);
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Shape(socket, "Fluted pillar", PrimitiveType.Cylinder, new Vector3(side * 1.45f, 1.1f, 0),
                            new Vector3(.42f, 1.15f, .42f), stone);
                        Box(socket, "Column capital", new Vector3(side * 1.45f, 2.26f, 0),
                            new Vector3(.65f, .22f, .75f), cream);
                        Box(socket, "Column foot", new Vector3(side * 1.45f, .05f, 0),
                            new Vector3(.65f, .24f, .75f), cream);
                    }
                    for (int piece = 0; piece < 11; piece++)
                    {
                        float a = Mathf.PI * (piece + .5f) / 11;
                        Box(socket, "Voussoir arch stone", new Vector3(1.45f * Mathf.Cos(a),
                            2.1f + 1.1f * Mathf.Sin(a), 0), new Vector3(.43f, .38f, .65f), stone,
                            Quaternion.Euler(0, 0, a * Mathf.Rad2Deg - 90));
                    }
                    Box(socket, "Gold cornice", new Vector3(0, 3.4f, 0), new Vector3(3.3f, .14f, .8f), gold);
                    Box(socket, "Rear decorative wall", new Vector3(0, 1.35f, settings.RearSign * .5f),
                        new Vector3(3.2f, 2.8f, .25f), dark);
                }
            for (int i = 0; i < 40; i++)
                Box(parent, "Safety railing post", Arc(settings.colosseumRadius - .2f, .72f,
                    Mathf.Lerp(-settings.arcDegrees * .5f + 2, settings.arcDegrees * .5f - 2, i / 39f) *
                    Mathf.Deg2Rad), new Vector3(.08f, 1.3f, .08f), gold);
            var railMesh = RingMesh(settings.colosseumRadius - .28f, settings.colosseumRadius - .12f, 1.33f, 1.42f);
            AssetDatabase.CreateAsset(railMesh, folder + "/Meshes/Continuous railing.asset");
            var rail = Group(parent, "Continuous curved golden railing");
            rail.gameObject.AddComponent<MeshFilter>().sharedMesh = railMesh;
            rail.gameObject.AddComponent<MeshRenderer>().sharedMaterial = gold;
        }
    }
}
