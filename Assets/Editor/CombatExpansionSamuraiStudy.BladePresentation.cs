using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string PresentationBladeIdentity = "3e685dd57e9c78b49b20bef3e8358ae3:4300002";

        sealed class BladePresentationGeometry
        {
            public Mesh blade;
            public Mesh[] sheaths;
            public Vector3 bladeBase;
            public Vector3 bladeTip;
            public Bounds bounds;
            public int triangles;
            public int vertices;

            public string Describe() =>
                "Blade=" + CombatExpansionInventory.Identity(blade) + "\nSheaths=" +
                string.Join(",", sheaths.Select(CombatExpansionInventory.Identity)) + "\n" +
                FormattableString.Invariant($"SelectedTriangles={triangles}; selectedVertices={vertices}\n") +
                FormattableString.Invariant($"Base=({bladeBase.x:R},{bladeBase.y:R},{bladeBase.z:R})\n") +
                FormattableString.Invariant($"Tip=({bladeTip.x:R},{bladeTip.y:R},{bladeTip.z:R})\n") +
                FormattableString.Invariant($"Length={Vector3.Distance(bladeBase, bladeTip):R}; bounds={bounds}\n");
        }

        /// <summary>Configures this component only; the caller owns persistence and gameplay validation.</summary>
        public static string ConfigureBladePresentation(BattleWeaponTrails trails)
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            if (!trails)
                throw new ArgumentNullException(nameof(trails));
            var geometry = DiscoverBladePresentationGeometry();
            var exclusions = new List<Mesh>(trails.excludedMeshes ?? Array.Empty<Mesh>());
            foreach (var sheath in geometry.sheaths)
                if (!exclusions.Contains(sheath))
                    exclusions.Add(sheath);
            if (exclusions.Contains(geometry.blade))
                throw new InvalidOperationException("Native BladeR is already excluded; resolve this conflict.");
            var blades = new List<BattleWeaponTrails.StaticBlade>(
                trails.staticBlades ?? Array.Empty<BattleWeaponTrails.StaticBlade>());
            bool replaced = false;
            for (int index = 0; index < blades.Count; index++)
            {
                if (blades[index] == null || blades[index].mesh != geometry.blade)
                    continue;
                blades[index] = CalibratedPresentationBlade(geometry);
                replaced = true;
            }
            if (!replaced)
                blades.Add(CalibratedPresentationBlade(geometry));
            trails.excludedMeshes = exclusions.ToArray();
            trails.staticBlades = blades.ToArray();
            EditorUtility.SetDirty(trails);
            return geometry.Describe();
        }

        static BattleWeaponTrails.StaticBlade CalibratedPresentationBlade(BladePresentationGeometry geometry)
        {
            return new BattleWeaponTrails.StaticBlade
            {
                mesh = geometry.blade,
                bladeBase = geometry.bladeBase,
                bladeTip = geometry.bladeTip
            };
        }

        static BladePresentationGeometry DiscoverBladePresentationGeometry()
        {
            var geometry = new BladePresentationGeometry { sheaths = new Mesh[Guids.Length] };
            for (int role = 0; role < Guids.Length; role++)
            {
                string path = AssetDatabase.GUIDToAssetPath(Guids[role]);
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!source)
                    throw new InvalidOperationException("Missing native Samurai source: " + Guids[role]);
                geometry.sheaths[role] = PresentationSourceMesh(source, "Sword_Hold", Guids[role] + ":4300004");
                if (role == 0)
                    geometry.blade = PresentationSourceMesh(source, "BladeR", PresentationBladeIdentity);
            }
            SelectPresentationEndpoints(geometry);
            return geometry;
        }

        static Mesh PresentationSourceMesh(GameObject source, string rendererName, string expectedIdentity)
        {
            var renderers = source.GetComponentsInChildren<Renderer>(true)
                .Where(renderer => renderer.name == rendererName).ToArray();
            if (renderers.Length != 1)
                throw new InvalidOperationException("Expected one native " + rendererName + " in " + source.name);
            var renderer = renderers[0];
            Mesh mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh :
                renderer.GetComponent<MeshFilter>()?.sharedMesh;
            if (!mesh || CombatExpansionInventory.Identity(mesh) != expectedIdentity)
                throw new InvalidOperationException(rendererName + " identity changed: " + expectedIdentity);
            return mesh;
        }

        static void SelectPresentationEndpoints(BladePresentationGeometry geometry)
        {
            if (!geometry.blade.isReadable)
                throw new InvalidOperationException("Native BladeR must be readable for editor calibration.");
            var vertices = geometry.blade.vertices;
            var triangles = geometry.blade.triangles;
            if (triangles.Length % 3 != 0)
                throw new InvalidOperationException("Native blade triangle indices are incomplete.");
            var selected = new HashSet<int>();
            var meshBounds = geometry.blade.bounds;
            if (!FinitePresentationPoint(meshBounds.center) || !FinitePresentationPoint(meshBounds.size))
                throw new InvalidOperationException("Native blade has nonfinite mesh bounds.");
            meshBounds.Expand(.0001f);
            float minimumZ = float.PositiveInfinity;
            float maximumZ = float.NegativeInfinity;
            for (int face = 0; face < triangles.Length; face += 3)
            {
                bool blade = true;
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = triangles[face + corner];
                    if (index < 0 || index >= vertices.Length || !FinitePresentationPoint(vertices[index]))
                        throw new InvalidOperationException("Native blade contains invalid indices or vertices.");
                    blade &= vertices[index].z <= -.33f;
                }
                if (!blade)
                    continue;
                geometry.triangles++;
                for (int corner = 0; corner < 3; corner++)
                {
                    int index = triangles[face + corner];
                    var point = vertices[index];
                    if (!meshBounds.Contains(point))
                        throw new InvalidOperationException("Selected blade vertex falls outside native mesh bounds.");
                    if (selected.Count == 0)
                        geometry.bounds = new Bounds(point, Vector3.zero);
                    selected.Add(index);
                    geometry.bounds.Encapsulate(point);
                    if (point.z < minimumZ)
                    {
                        minimumZ = point.z;
                        geometry.bladeTip = point;
                    }
                    if (point.z > maximumZ)
                    {
                        maximumZ = point.z;
                        geometry.bladeBase = point;
                    }
                }
            }
            geometry.vertices = selected.Count;
            float length = Vector3.Distance(geometry.bladeBase, geometry.bladeTip);
            if (geometry.triangles != 111 || selected.Count == 0 || !float.IsFinite(length) || length <= .001f)
                throw new InvalidOperationException("Native blade region changed or endpoints are degenerate.");
        }

        static bool FinitePresentationPoint(Vector3 point)
        {
            return float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);
        }
    }
}
