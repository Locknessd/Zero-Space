using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeDenseStudy
    {
        // Selection is fixed; evaluated vertices and both triangle surfaces are rebuilt per measurement.
        public sealed partial class SkinRegionProbe : IDisposable
        {
            readonly CharacterCombat source;
            readonly CharacterCombat target;
            readonly HumanBodyBones sourceRoot;
            readonly HumanBodyBones targetRoot;
            readonly List<RegionSkin> sourceSkins = new List<RegionSkin>();
            readonly List<RegionSkin> targetSkins = new List<RegionSkin>();
            bool disposed;

            public string SelectionSummary { get; }

            public SkinRegionProbe(CharacterCombat source, HumanBodyBones sourceRoot,
                CharacterCombat target, HumanBodyBones targetRoot)
            {
                this.source = source;
                this.target = target;
                this.sourceRoot = sourceRoot;
                this.targetRoot = targetRoot;
                var summary = new List<string>
                {
                    "Region selection: actual Animator root bone and descendants; every triangle vertex " +
                    "must have >=0.5 summed influence across all native skin weights on region bones; " +
                    "visible BodySkins only. Geometry: explicit current bone * bindpose world skinning; " +
                    "exact triangle distance; unsigned zero may mean touch or penetration."
                };
                try
                {
                    SelectRegion(source, sourceRoot, sourceSkins, summary);
                    SelectRegion(target, targetRoot, targetSkins, summary);
                    SelectionSummary = string.Join("\n", summary);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public BodyContact Measure(List<string> diagnostics)
            {
                if (disposed)
                    throw new ObjectDisposedException(nameof(SkinRegionProbe));
                if (diagnostics == null)
                    throw new ArgumentNullException(nameof(diagnostics));
                MappedBone(source, sourceRoot);
                var anchor = MappedBone(target, targetRoot);
                Surface hand = EvaluateRegion(sourceSkins, diagnostics);
                Surface body = EvaluateRegion(targetSkins, diagnostics);
                var result = body.Closest(hand);
                if (!float.IsFinite(result.squared) || result.squared < 0 ||
                    !Finite(result.blade) || !Finite(result.body))
                    throw new InvalidOperationException("Nonfinite skin region contact geometry or gap.");
                float gap = Mathf.Sqrt(result.squared);
                Vector3 offset = anchor.InverseTransformPoint(result.body);
                if (!float.IsFinite(gap) || !Finite(offset))
                    throw new InvalidOperationException("Nonfinite skin region gap or target bone offset.");
                // This bone records the selected region, rather than a nearest anatomical estimate.
                return new BodyContact(gap, result.blade, result.body, targetRoot, offset);
            }

            static Surface EvaluateRegion(List<RegionSkin> skins, List<string> diagnostics)
            {
                var surface = new Surface();
                foreach (var selected in skins)
                {
                    var skin = selected.skin;
                    // CPU preview rendering hides the original renderer while sampling this same skin.
                    // Visibility was checked at selection; retain mesh and active-object identity checks here.
                    if (!skin || !skin.gameObject.activeInHierarchy || !selected.mesh || skin.sharedMesh != selected.mesh ||
                        selected.mesh.vertexCount != selected.vertexCount)
                        throw new InvalidOperationException("Selected skin changed after region selection.");
                    skin.BakeMesh(selected.baked, false);
                    Vector3[] world = RenderedVertices(skin, selected.baked, diagnostics);
                    if (world.Length != selected.vertexCount)
                        throw new InvalidOperationException("Selected skin vertex count changed: " + skin.name);
                    int offset = surface.vertices.Count;
                    foreach (var point in world)
                    {
                        if (!Finite(point))
                            throw new InvalidOperationException("Nonfinite region skin vertex: " + skin.name);
                        surface.vertices.Add(point);
                    }
                    foreach (int index in selected.triangles)
                        surface.triangles.Add(offset + index);
                }
                surface.Build();
                return surface;
            }

            static Transform MappedBone(CharacterCombat fighter, HumanBodyBones root)
            {
                if (!fighter || !fighter.Animator || !fighter.Animator.isHuman ||
                    root < HumanBodyBones.Hips || root >= HumanBodyBones.LastBone)
                    throw new InvalidOperationException("Region probe requires a mapped humanoid fighter bone.");
                var bone = fighter.Animator.GetBoneTransform(root);
                if (!bone || !bone.IsChildOf(fighter.Animator.transform))
                    throw new InvalidOperationException("Missing mapped region bone: " + fighter.name + "/" + root);
                return bone;
            }

            static bool Finite(Vector3 point) =>
                float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z);

            public void Dispose()
            {
                if (disposed)
                    return;
                disposed = true;
                DestroyBakes(sourceSkins);
                DestroyBakes(targetSkins);
            }

            static void DestroyBakes(List<RegionSkin> skins)
            {
                foreach (var selected in skins)
                    if (selected.baked)
                        Object.DestroyImmediate(selected.baked);
                skins.Clear();
            }
        }
    }
}
