using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MemeArenaEditor
{
    public static partial class MemeArenaBuild
    {
        static Mesh FractureMesh()
        {
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var triangles = new List<int>();
            var random = new System.Random(191026);
            var darkCrack = new Color(.11f, .065f, .035f, .92f);
            var edge = new Color(.78f, .54f, .3f, .75f);
            for (int ray = 0; ray < 12; ray++)
            {
                float angle = ray * Mathf.PI * 2 / 12 + (float)random.NextDouble() * .13f;
                var previous = Vector3.zero;
                for (int step = 1; step <= 4; step++)
                {
                    float distance = step * Mathf.Lerp(.18f, .26f, (float)random.NextDouble());
                    float bend = angle + ((float)random.NextDouble() - .5f) * .3f;
                    var point = new Vector3(Mathf.Cos(bend), 0, Mathf.Sin(bend)) * distance;
                    float width = Mathf.Lerp(.034f, .007f, step / 4f);
                    CrackSegment(previous, point, width, darkCrack, vertices, colors, triangles);
                    CrackSegment(previous + Vector3.right * .018f, point + Vector3.right * .018f,
                        width * .32f, edge, vertices, colors, triangles);
                    if (step == 2 || step == 3)
                    {
                        var branch = point + Quaternion.Euler(0, ray % 2 == 0 ? 47 : -47, 0) *
                            (point - previous).normalized * .19f;
                        CrackSegment(point, branch, width * .55f, darkCrack, vertices, colors, triangles);
                    }
                    previous = point;
                }
                float nextAngle = (ray + 1) * Mathf.PI * 2 / 12;
                var a = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * .3f;
                var b = new Vector3(Mathf.Cos(nextAngle), 0, Mathf.Sin(nextAngle)) * .3f;
                CrackSegment(a, b, .018f, darkCrack, vertices, colors, triangles);
            }
            var mesh = new Mesh { name = "Branching radial floor cracks" };
            mesh.SetVertices(vertices);
            mesh.SetColors(colors);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        static void CrackSegment(Vector3 a, Vector3 b, float width, Color color,
            List<Vector3> vertices, List<Color> colors, List<int> triangles)
        {
            var side = Vector3.Cross(b - a, Vector3.up).normalized * width;
            int start = vertices.Count;
            vertices.Add(a - side);
            vertices.Add(a + side);
            vertices.Add(b + side * .65f);
            vertices.Add(b - side * .65f);
            for (int i = 0; i < 4; i++) colors.Add(color);
            triangles.AddRange(new[] { start, start + 1, start + 2, start, start + 2, start + 3 });
        }

        static Mesh ShardMesh()
        {
            var points = new[] { new Vector3(-.8f, 0, -.5f), new Vector3(.7f, 0, -.45f),
                new Vector3(.35f, 0, .7f), new Vector3(-.6f, 0, .5f), new Vector3(0, .65f, 0) };
            var indices = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 4, 1, 2, 4, 2, 3, 4, 3, 0, 4 };
            var vertices = new Vector3[indices.Length];
            var triangles = new int[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                vertices[i] = points[indices[i]];
                triangles[i] = i;
            }
            for (int i = 0; i < triangles.Length; i += 3)
            {
                triangles[i + 1] = i + 2;
                triangles[i + 2] = i + 1;
            }
            var mesh = new Mesh { name = "Low polygon arena stone fragment", vertices = vertices, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static void RepairFloorShardMesh()
        {
            string prefabPath = System.IO.File.ReadAllText(Root + "/Documentation/LatestGenerated.txt").Trim();
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var stage = prefab.GetComponent<MemeArena.MemeArenaStage>();
            var target = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(stage.generatedAssetFolder +
                "/Meshes/Broken stone shard.asset");
            var corrected = ShardMesh();
            UnityEditor.EditorUtility.CopySerialized(corrected, target);
            Object.DestroyImmediate(corrected);
            UnityEditor.EditorUtility.SetDirty(target);
            UnityEditor.AssetDatabase.SaveAssets();
            var center = target.vertices.Aggregate(Vector3.zero, (sum, vertex) => sum + vertex) / target.vertexCount;
            for (int i = 0; i < target.vertexCount; i += 3)
                if (Vector3.Dot(target.normals[i], target.vertices[i] - center) <= 0)
                    throw new System.Exception("A stone shard face points inward.");
            Debug.Log("PASS all six stone fragment faces point outward; shared mesh references preserved.");
        }
    }
}
