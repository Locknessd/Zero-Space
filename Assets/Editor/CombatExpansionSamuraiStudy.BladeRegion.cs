using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        sealed class SwordRegion : IDisposable
        {
            public readonly Renderer renderer;
            public readonly Vector3[] vertices;
            public readonly int[] triangles;
            readonly Vector3 tip;
            GameObject overlay;
            Mesh overlayMesh;
            Material overlayMaterial;
            public Vector3 Tip => renderer.transform.TransformPoint(tip);

            public SwordRegion(FrankBattlePairPlayback pair)
            {
                renderer = SwordRenderer(pair);
                var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                if (CombatExpansionInventory.Identity(mesh) != "3e685dd57e9c78b49b20bef3e8358ae3:4300002")
                    throw new InvalidOperationException("Samurai blade classification requires the inspected native mesh.");
                vertices = mesh.vertices;
                var faces = mesh.triangles;
                var selected = new List<int>();
                for (int face = 0; face < faces.Length; face += 3)
                {
                    bool blade = true;
                    for (int corner = 0; corner < 3; corner++)
                        blade &= vertices[faces[face + corner]].z <= -.33f;
                    if (blade)
                        for (int corner = 0; corner < 3; corner++)
                            selected.Add(faces[face + corner]);
                }
                triangles = selected.ToArray();
                if (triangles.Length != 111 * 3)
                    throw new InvalidOperationException("The inspected Samurai blade topology changed.");
                tip = triangles.Distinct().Select(index => vertices[index]).OrderBy(point => point.z).First();
            }

            public void ShowOverlay()
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (!shader)
                    throw new InvalidOperationException("Blade visualization requires URP Unlit.");
                overlay = new GameObject("Measured blade region only");
                overlay.transform.SetParent(renderer.transform, false);
                overlayMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave };
                var normals = renderer.GetComponent<MeshFilter>().sharedMesh.normals;
                var displayed = new Vector3[vertices.Length];
                for (int index = 0; index < vertices.Length; index++)
                    displayed[index] = vertices[index] + normals[index] * .003f;
                overlayMesh.vertices = displayed;
                overlayMesh.triangles = triangles;
                overlayMesh.RecalculateNormals();
                overlayMesh.RecalculateBounds();
                overlay.AddComponent<MeshFilter>().sharedMesh = overlayMesh;
                overlayMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                overlayMaterial.SetColor("_BaseColor", new Color(.1f, .95f, 1));
                overlay.AddComponent<MeshRenderer>().sharedMaterial = overlayMaterial;
            }

            public void Dispose()
            {
                if (overlay)
                    Object.DestroyImmediate(overlay);
                if (overlayMesh)
                    Object.DestroyImmediate(overlayMesh);
                if (overlayMaterial)
                    Object.DestroyImmediate(overlayMaterial);
            }
        }
    }
}
