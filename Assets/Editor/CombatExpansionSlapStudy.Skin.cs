using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        // Same BakeMesh(false) convention as CombatExpansionPreviewSkin, with partial-construction cleanup.
        sealed class PreviewSkin : IDisposable
        {
            sealed class Snapshot
            {
                public SkinnedMeshRenderer source;
                public Mesh mesh;
                public MeshRenderer renderer;
                public bool enabled;
            }

            readonly List<Snapshot> snapshots = new List<Snapshot>();
            readonly List<Object> owned = new List<Object>();

            public PreviewSkin(GameObject root)
            {
                try
                {
                    foreach (var skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        if (!skin.enabled || !skin.sharedMesh || skin.bones.Length == 0)
                            continue;
                        var proxy = new GameObject("Slap CPU preview " + skin.name);
                        owned.Add(proxy);
                        SceneManager.MoveGameObjectToScene(proxy, root.scene);
                        var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, name = proxy.name };
                        owned.Add(mesh);
                        proxy.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var renderer = proxy.AddComponent<MeshRenderer>();
                        renderer.sharedMaterials = skin.sharedMaterials;
                        renderer.shadowCastingMode = skin.shadowCastingMode;
                        renderer.receiveShadows = skin.receiveShadows;
                        renderer.enabled = false;
                        var properties = new MaterialPropertyBlock();
                        skin.GetPropertyBlock(properties);
                        renderer.SetPropertyBlock(properties);
                        snapshots.Add(new Snapshot
                        {
                            source = skin,
                            mesh = mesh,
                            renderer = renderer,
                            enabled = skin.enabled
                        });
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Sample()
            {
                foreach (var snapshot in snapshots)
                {
                    snapshot.source.BakeMesh(snapshot.mesh, false);
                    snapshot.mesh.RecalculateBounds();
                    snapshot.renderer.transform.SetPositionAndRotation(
                        snapshot.source.transform.position, snapshot.source.transform.rotation);
                    snapshot.renderer.enabled = snapshot.source.gameObject.activeInHierarchy;
                    snapshot.source.enabled = false;
                }
            }

            public void Dispose()
            {
                foreach (var snapshot in snapshots)
                    if (snapshot.source)
                        snapshot.source.enabled = snapshot.enabled;
                foreach (var asset in owned)
                    if (asset)
                        Object.DestroyImmediate(asset);
                snapshots.Clear();
                owned.Clear();
            }
        }
    }
}
