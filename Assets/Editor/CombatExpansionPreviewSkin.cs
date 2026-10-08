using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    /// <summary>Independent CPU skin snapshots for multiple manually sampled poses in one editor frame.</summary>
    public sealed class CombatExpansionPreviewSkin : IDisposable
    {
        sealed class Snapshot
        {
            public SkinnedMeshRenderer skin;
            public Mesh mesh;
            public MeshRenderer renderer;
            public bool enabled;
        }

        readonly List<Snapshot> snapshots = new List<Snapshot>();

        public CombatExpansionPreviewSkin(params GameObject[] roots)
        {
            foreach (var root in roots)
            foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (!skin.enabled || !skin.sharedMesh || skin.bones.Length == 0)
                    continue;
                var proxy = new GameObject("CPU preview " + skin.name);
                SceneManager.MoveGameObjectToScene(proxy, skin.gameObject.scene);
                var mesh = new Mesh { name = proxy.name, hideFlags = HideFlags.HideAndDontSave };
                proxy.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = proxy.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = skin.sharedMaterials;
                renderer.shadowCastingMode = skin.shadowCastingMode;
                renderer.receiveShadows = skin.receiveShadows;
                renderer.enabled = false;
                var properties = new MaterialPropertyBlock();
                skin.GetPropertyBlock(properties);
                renderer.SetPropertyBlock(properties);
                snapshots.Add(new Snapshot { skin = skin, mesh = mesh, renderer = renderer, enabled = skin.enabled });
            }
        }

        public void Sample()
        {
            foreach (var snapshot in snapshots)
            {
                snapshot.skin.BakeMesh(snapshot.mesh, false);
                snapshot.mesh.RecalculateBounds();
                // Bake(false) contains the calibrated renderer scale. Reapplying its
                // transform scale would enlarge Pepe by ten times in this scene.
                snapshot.renderer.transform.SetPositionAndRotation(
                    snapshot.skin.transform.position, snapshot.skin.transform.rotation);
                snapshot.renderer.enabled = snapshot.skin.gameObject.activeInHierarchy;
                snapshot.skin.enabled = false;
            }
        }

        public void Dispose()
        {
            foreach (var snapshot in snapshots)
            {
                if (snapshot.skin)
                    snapshot.skin.enabled = snapshot.enabled;
                if (snapshot.renderer)
                    Object.DestroyImmediate(snapshot.renderer.gameObject);
                Object.DestroyImmediate(snapshot.mesh);
            }
            snapshots.Clear();
        }
    }
}
