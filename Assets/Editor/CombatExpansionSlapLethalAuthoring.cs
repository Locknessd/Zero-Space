using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapLethalAuthoring
    {
        public const string Output = "GeneratedAssets/CombatExpansion/SlapStudy/LethalAuthoring";
        public const string AssetRoot = "Assets/CombatExpansion/SlapStudy/LethalCandidates";
        const float BlendDuration = .22f;
        const float FallStart = .45f;
        const float FallDuration = 1.81f;
        static readonly string[] SourceGuids =
        {
            "ab20a2bd36bd2b59358ce4e03e934069", "8ae91d8a5acd3a1f64feaad62ff62a6d",
            "b01b411d366a5a344bddf3428a7bc8f7"
        };
        static readonly long[] SourceIds = { 8993902097722301239, 8993902097722301239, 7400004 };
        static readonly float[] Contacts = { 1.4f, .933333333f };

        sealed class Source
        {
            public AnimationClip clip;
            public string path, guid, byteHash, metaHash, clipHash, importerHash;
            public long localId;
            public readonly Dictionary<string, EditorCurveBinding> bindings = new();
            public readonly Dictionary<string, AnimationCurve> curves = new();
        }

        [MenuItem("Tools/Combat Expansion/Slap/Inspect Lethal Authoring Sources")]
        public static void InspectLethalAuthoringSources()
        {
            var sources = LoadSources();
            WriteInspection(sources);
            AssertSourcesUnchanged(sources);
            Debug.Log("Slap lethal source inspection: " + Output);
        }

        [MenuItem("Tools/Combat Expansion/Slap/Bake Lethal Candidates")]
        public static void BakeLethalCandidates()
        {
            var sources = LoadSources();
            var report = new StringBuilder();
            var candidates = new List<AnimationClip>();
            Directory.CreateDirectory(Output);
            Exception failure = null;
            try
            {
                WriteInspection(sources);
                WriteIntent(report);
                // Publish intent before schema rejection so an unsupported import remains diagnosable.
                File.WriteAllText(Output + "/BakeProvenance.txt", report.ToString());
                foreach (var source in sources)
                    ValidateSource(source);
                for (int i = 0; i < 2; i++)
                {
                    candidates.Add(BuildEmpirical(sources[i], sources[2], i, report));
                }
                AssertSourcesUnchanged(sources);
                PreflightTargets(candidates);
                EnsureFolder(AssetRoot);
                foreach (var candidate in candidates)
                    SaveCandidate(candidate, report);
                AssertSourcesUnchanged(sources);
                report.AppendLine("SUCCESS: both candidates saved; source bytes, metadata and editor state unchanged.");
                Debug.Log("Provisional Slap lethal candidates baked: " + AssetRoot);
            }
            catch (Exception exception)
            {
                failure = exception;
                report.AppendLine("FAILED: " + exception);
                throw;
            }
            finally
            {
                foreach (var candidate in candidates)
                    if (candidate && !EditorUtility.IsPersistent(candidate))
                        Object.DestroyImmediate(candidate);
                FinishGuard(sources, report, failure);
            }
        }

        static Source[] LoadSources()
        {
            return SourceGuids.Select((guid, index) =>
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !File.Exists(path) || !File.Exists(path + ".meta"))
                    throw new InvalidOperationException("Missing source bytes/metadata for " + guid);
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().SingleOrDefault(c =>
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(c, out string g, out long id) &&
                    g == guid && id == SourceIds[index]);
                if (!clip)
                    throw new InvalidOperationException("Missing exact source clip: " + guid + ":" + SourceIds[index]);
                var source = new Source { clip = clip, guid = guid, path = path, localId = SourceIds[index] };
                source.byteHash = FileHash(path);
                source.metaHash = FileHash(path + ".meta");
                WarmSource(source);
                AssertFileHashes(source);
                source.clipHash = StableClipHash(source);
                source.importerHash = TextHash(EditorJsonUtility.ToJson(AssetImporter.GetAtPath(path)));
                return source;
            }).ToArray();
        }

        static string FileHash(string path)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }

        static string TextHash(string value)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", "");
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static string CandidatePath(AnimationClip clip)
        {
            return AssetRoot + "/" + clip.name + ".anim";
        }

        static void PreflightTargets(IEnumerable<AnimationClip> candidates)
        {
            foreach (var candidate in candidates)
            {
                string path = CandidatePath(candidate);
                var existing = AssetDatabase.LoadMainAssetAtPath(path);
                if ((File.Exists(path) && !existing) || (existing && !(existing is AnimationClip)))
                    throw new InvalidOperationException("Unsafe candidate target: " + path);
                if (existing && EditorUtility.IsDirty(existing))
                    throw new InvalidOperationException("Candidate has unsaved edits: " + path);
            }
        }

        static void SaveCandidate(AnimationClip candidate, StringBuilder report)
        {
            string path = CandidatePath(candidate);
            var target = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            string previousGuid = AssetDatabase.AssetPathToGUID(path);
            if (target)
            {
                EditorUtility.CopySerialized(candidate, target);
                EditorUtility.SetDirty(target);
            }
            else
            {
                AssetDatabase.CreateAsset(candidate, path);
                target = candidate;
            }
            AssetDatabase.SaveAssetIfDirty(target);
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (!string.IsNullOrEmpty(previousGuid) && previousGuid != guid)
                throw new InvalidOperationException("Candidate GUID changed unexpectedly: " + path);
            report.AppendLine("ASSET " + path + " guid=" + guid + " sha256=" + FileHash(path));
        }
    }
}
