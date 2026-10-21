using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MemeArenaEditor
{
    public static partial class MemeArenaBuild
    {
        struct CrowdCard
        {
            public Vector3 center;
            public Rect rect;
            public float height;
            public float seed;
            public float aspect;
        }

        static Mesh CrowdMesh(IEnumerable<CrowdCard> cards)
        {
            var vertices = new List<Vector3>();
            var centers = new List<Vector3>();
            var uv = new List<Vector2>();
            var seeds = new List<Vector2>();
            var triangles = new List<int>();
            foreach (var card in cards)
            {
                int start = vertices.Count;
                for (int corner = 0; corner < 4; corner++)
                {
                    bool right = corner == 1 || corner == 2;
                    bool top = corner >= 2;
                    float aspect = card.aspect > 0 ? card.aspect : 1;
                    vertices.Add(card.center + new Vector3((right ? .5f : -.5f) * card.height * aspect,
                        top ? card.height : 0, 0));
                    centers.Add(card.center);
                    uv.Add(new Vector2(right ? card.rect.xMax : card.rect.xMin,
                        top ? card.rect.yMax : card.rect.yMin));
                    seeds.Add(new Vector2(card.seed, 0));
                }
                triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
            }
            var mesh = new Mesh { name = "Shared meme crowd cards", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, centers);
            mesh.SetUVs(2, seeds);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            var bounds = mesh.bounds;
            bounds.Expand(3);
            mesh.bounds = bounds;
            return mesh;
        }

        static void BuildDistantCrowd(Transform parent)
        {
            var material = new Material(Shader.Find("MemeArena/Crowd Card")) { name = "Distant crowd silhouettes" };
            material.SetTexture("_BaseMap", settings.distantCrowdTexture);
            material.SetColor("_BaseColor", new Color(.65f, .65f, .75f, 1));
            material.SetFloat("_IdleSway", .008f);
            AssetDatabase.CreateAsset(material, folder + "/Materials/Distant crowd.mat");
            var mesh = CrowdMesh(new[] { new CrowdCard
            {
                center = new Vector3(0, settings.seatingTiers * .74f + 1,
                    settings.RearSign * (settings.colosseumRadius + settings.seatingTiers * 1.18f + 2)),
                height = 4,
                rect = new Rect(0, 0, 1, 1)
            } });
            // Wider silhouette strip: retain its actual aspect instead of stretching spectators vertically.
            var vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i].x *= 9;
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, folder + "/Meshes/Distant crowd.asset");
            var item = Group(parent, "Far crowd silhouette layer");
            item.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = item.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
