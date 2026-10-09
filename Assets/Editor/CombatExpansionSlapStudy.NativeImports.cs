using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        [Serializable]
        sealed class NativeImportRecord
        {
            public int version = 1;
            public string originalGuid, originalPath, originalFileHash, originalMetaHash, originalSettingsHash;
            public string adaptedGuid, adaptedPath, adaptedFileHash, adaptedMetaHash, adaptedSettingsHash;
            public string correction = NativeCorrection;
            public string status, error, avatar, originalClipName, adaptedClipName;
            public long originalLocalId = ClipId, adaptedLocalId = ClipId;
            public float durationSeconds, framesPerSecond;
            public bool humanMotion;
        }

        [Serializable]
        sealed class NativeImportReport
        {
            public string utc = DateTime.UtcNow.ToString("O");
            public string unityVersion = Application.unityVersion;
            public string invokeOrder = "PrepareNativeImports(); PrepareDrivers(); CaptureSources();";
            public string scope = "Native source preparation only. Original stable identities remain provenance keys. " +
                "No roles, pairing, offsets, choreography or registration are established.";
            public NativeImportRecord[] sources;
        }

        static string NativeRecordPath(string guid)
        {
            return Output + "/NativeImports/" + guid + ".json";
        }

        static NativeImportRecord SourceBaseline(int index)
        {
            string guid = Guids[index];
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/Selected/SlapFace/", StringComparison.Ordinal))
                throw new InvalidOperationException("Unexpected original source path: " + path);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            ValidateMapping(AssetDatabase.LoadAssetAtPath<GameObject>(path), importer);
            var clip = NativeClip(path, guid, index);
            return new NativeImportRecord
            {
                originalGuid = guid,
                originalPath = path,
                originalFileHash = FileHash(path),
                originalMetaHash = FileHash(path + ".meta"),
                originalSettingsHash = SettingsFingerprint(importer),
                adaptedGuid = HashBytes(Encoding.UTF8.GetBytes("SlapStudy.NativeSources.v1:" + guid)).Substring(0, 32),
                adaptedPath = NativePath(guid),
                originalClipName = clip.name,
                durationSeconds = clip.length,
                framesPerSecond = clip.frameRate,
                humanMotion = clip.humanMotion
            };
        }

        static void RequireOriginalUnchanged(NativeImportRecord source)
        {
            if (AssetDatabase.GUIDToAssetPath(source.originalGuid) != source.originalPath ||
                FileHash(source.originalPath) != source.originalFileHash ||
                FileHash(source.originalPath + ".meta") != source.originalMetaHash)
                throw new InvalidOperationException("Original SlapFace source changed: " + source.originalPath);
        }

        static void SaveNativeRecord(NativeImportRecord record)
        {
            Directory.CreateDirectory(Output + "/NativeImports");
            File.WriteAllText(NativeRecordPath(record.originalGuid), JsonUtility.ToJson(record, true));
        }

        // Invoke on the editor main thread: PrepareNativeImports, then PrepareDrivers, then CaptureSources.
        public static void PrepareNativeImports()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var baselines = Enumerable.Range(0, Guids.Length).Select(SourceBaseline).ToArray();
            var prepared = new List<NativeImportRecord>();
            try
            {
                for (int index = 0; index < baselines.Length; index++)
                    prepared.Add(PrepareNativeImport(baselines[index], index));
                Directory.CreateDirectory(Output);
                File.WriteAllText(Output + "/NativeImports.json", JsonUtility.ToJson(new NativeImportReport
                {
                    sources = prepared.ToArray()
                }, true));
            }
            finally
            {
                foreach (var source in baselines)
                    RequireOriginalUnchanged(source);
            }
        }

        static NativeImportRecord PrepareNativeImport(NativeImportRecord source, int index)
        {
            if (File.Exists(source.adaptedPath) || File.Exists(source.adaptedPath + ".meta") ||
                File.Exists(NativeRecordPath(source.originalGuid)))
                return RequirePreparedImport(source, index);
            var importer = (ModelImporter)AssetImporter.GetAtPath(source.originalPath);
            if (importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther || importer.sourceAvatar ||
                !string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(CopiedAvatarGuid)))
                throw new InvalidOperationException("Source no longer matches diagnosed missing Avatar dependency");
            if (!string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(source.adaptedGuid)))
                throw new InvalidOperationException("Prepared source GUID already belongs to another asset");
            string meta = CorrectedMeta(source.originalPath, source.adaptedGuid);
            CombatExpansionHumanoidStudy.EnsureFolder(Path.GetDirectoryName(source.adaptedPath).Replace('\\', '/'));
            source.status = "preparing";
            SaveNativeRecord(source);
            try
            {
                AssetDatabase.DisallowAutoRefresh();
                try
                {
                    File.Copy(source.originalPath, source.adaptedPath, false);
                    File.WriteAllText(source.adaptedPath + ".meta", meta, new UTF8Encoding(false));
                }
                finally
                {
                    AssetDatabase.AllowAutoRefresh();
                }
                AssetDatabase.ImportAsset(source.adaptedPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                ValidatePreparedRig(source, index);
                source.adaptedFileHash = FileHash(source.adaptedPath);
                source.adaptedMetaHash = FileHash(source.adaptedPath + ".meta");
                source.status = "validated";
                SaveNativeRecord(source);
                return source;
            }
            catch (Exception error)
            {
                source.status = "failed";
                source.error = error.ToString();
                SaveNativeRecord(source);
                DiagnoseNativeImports();
                throw new InvalidOperationException("Native SlapFace import failed; inspect NativeImportDiagnostics.json. " +
                    "Existing copies will not be overwritten. " + source.adaptedPath, error);
            }
        }
    }
}
