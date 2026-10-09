using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        [Serializable]
        sealed class LethalClipRecord
        {
            public string identity, path, name, byteHash, metaHash, serializedHash;
            public float durationSeconds, frameRate;
        }

        static LethalClipRecord LethalClipSnapshot(AnimationClip clip)
        {
            string path = AssetDatabase.GetAssetPath(clip);
            return new LethalClipRecord
            {
                identity = CombatExpansionInventory.Identity(clip),
                path = path,
                name = clip.name,
                byteHash = FileHash(path),
                metaHash = FileHash(path + ".meta"),
                serializedHash = LethalObjectHash(clip),
                durationSeconds = clip.length,
                frameRate = clip.frameRate
            };
        }

        static string LethalObjectHash(Object value)
        {
            return HashBytes(Encoding.UTF8.GetBytes(EditorJsonUtility.ToJson(value)));
        }

        sealed class LethalAssetGuard
        {
            readonly string path, bytes, metadata;
            readonly List<(Object value, string hash, bool dirty)> states =
                new List<(Object, string, bool)>();

            public LethalAssetGuard(string path)
            {
                this.path = path;
                if (!File.Exists(path) || !File.Exists(path + ".meta"))
                    throw new InvalidOperationException("Missing raw evidence asset bytes/metadata: " + path);
                bytes = FileHash(path);
                metadata = FileHash(path + ".meta");
                var objects = new HashSet<Object>(AssetDatabase.LoadAllAssetsAtPath(path));
                var importer = AssetImporter.GetAtPath(path);
                if (importer)
                    objects.Add(importer);
                foreach (var root in objects.OfType<GameObject>().ToArray())
                foreach (var component in root.GetComponentsInChildren<Component>(true))
                {
                    if (!component)
                        continue;
                    objects.Add(component);
                    objects.Add(component.gameObject);
                }
                foreach (var value in objects.Where(value => value))
                    states.Add((value, LethalObjectHash(value), EditorUtility.IsDirty(value)));
            }

            public void AssertUnchanged()
            {
                if (FileHash(path) != bytes || FileHash(path + ".meta") != metadata)
                    throw new InvalidOperationException("Raw evidence bytes/metadata changed: " + path);
                foreach (var state in states)
                    if (!state.value || LethalObjectHash(state.value) != state.hash ||
                        EditorUtility.IsDirty(state.value) != state.dirty)
                        throw new InvalidOperationException("Raw evidence persistent object changed: " + path);
            }
        }

        static LethalAssetGuard[] LethalGuards(SourceRecord[] sources, AnimationClip[] clips)
        {
            var paths = clips.Select(AssetDatabase.GetAssetPath)
                .Concat(sources.Select(s => s.originalPath))
                .Concat(sources.SelectMany(s => new[] { DriverPath("Mankey", s), DriverPath("Pepe", s) }))
                .Append(CombatExpansionInventory.Battle).Distinct();
            return paths.Select(path => new LethalAssetGuard(path)).ToArray();
        }
    }
}
