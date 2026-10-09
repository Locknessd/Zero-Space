using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        sealed class E10Guard
        {
            readonly Scene active = SceneManager.GetActiveScene();
            readonly Object[] selection = Selection.objects;
            readonly Object selected = Selection.activeObject;
            readonly string sourceSnapshot;
            readonly string liveSnapshot;
            public readonly NativeReferenceFile[] Files;

            public E10Guard()
            {
                liveSnapshot = Scenes();
                var sources = ResolveSources();
                sourceSnapshot = NativeReferenceSourceSnapshot(sources);
                var paths = sources.Select(s => s.path).Concat(new[]
                {
                    CombatExpansionInventory.Battle,
                    GroundingAssetPath(10),
                    E10RecoveryPath,
                    AssetDatabase.GUIDToAssetPath(E10GetUp.Split(':')[0])
                }).ToList();
                foreach (string fighter in new[] { "Mankey", "Pepe" })
                {
                    paths.Add(DriverPath(fighter, 0, true));
                    paths.Add(DriverPath(fighter, 1, false));
                }
                foreach (string path in paths)
                    if (string.IsNullOrEmpty(path) || !File.Exists(path) || !File.Exists(path + ".meta"))
                        throw new InvalidOperationException("Missing preservation guard asset/meta: " + path);
                Files = paths.SelectMany(p => AssetDatabase.GetDependencies(p, true)).Concat(paths)
                    .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).Distinct()
                    .SelectMany(p => new[] { p, p + ".meta" }).Where(File.Exists).Distinct().OrderBy(p => p)
                    .Select(p => new NativeReferenceFile { path = p, sha256 = NativeReferenceHash(p) }).ToArray();
            }

            public void Verify(E10Report report)
            {
                report.fileGuardsPassed = sourceSnapshot == NativeReferenceSourceSnapshot(ResolveSources()) &&
                    Files.All(f => File.Exists(f.path) && NativeReferenceHash(f.path) == f.sha256);
                report.sceneGuardsPassed = liveSnapshot == Scenes() && SceneManager.GetActiveScene() == active;
                if (!report.fileGuardsPassed || !report.sceneGuardsPassed)
                    throw new InvalidOperationException("Source, grounding, driver, saved/live scene guard changed.");
            }

            public void RestoreSelection()
            {
                if (active.IsValid() && active.isLoaded)
                    SceneManager.SetActiveScene(active);
                Selection.objects = selection;
                Selection.activeObject = selected;
            }

            static string Scenes()
            {
                var values = new List<string>();
                for (int index = 0; index < SceneManager.sceneCount; index++)
                {
                    var scene = SceneManager.GetSceneAt(index);
                    values.Add(scene.handle + "|" + scene.path + "|" + scene.isLoaded + "|" + scene.isDirty);
                    if (!scene.isLoaded)
                        continue;
                    foreach (var root in scene.GetRootGameObjects())
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                    {
                        values.Add(transform.gameObject.GetEntityId() + "|" +
                            EditorJsonUtility.ToJson(transform.gameObject));
                        foreach (var component in transform.GetComponents<Component>())
                            if (component)
                                values.Add(component.GetEntityId() + "|" + EditorJsonUtility.ToJson(component));
                    }
                }
                return string.Join("\n", values);
            }
        }
    }
}
