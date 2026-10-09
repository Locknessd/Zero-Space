using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        static void RequireSourceFilesUnchanged(SourceRecord[] sources)
        {
            foreach (var source in sources)
                if (FileHash(source.originalPath) != source.originalFileHash ||
                    FileHash(source.originalPath + ".meta") != source.originalMetaHash)
                    throw new InvalidOperationException("Original SlapFace source changed: " + source.originalPath);
        }

        static NativeImportRecord RequirePreparedImport(NativeImportRecord baseline, int index)
        {
            string recordPath = NativeRecordPath(baseline.originalGuid);
            if (!File.Exists(recordPath) || !File.Exists(baseline.adaptedPath) ||
                !File.Exists(baseline.adaptedPath + ".meta"))
                throw new InvalidOperationException("Missing prepared import/provenance; run PrepareNativeImports(): " +
                    baseline.adaptedPath + ". Existing partial or manual assets are never overwritten.");
            var record = JsonUtility.FromJson<NativeImportRecord>(File.ReadAllText(recordPath));
            if (record == null || record.version != 1 || record.status != "validated" ||
                record.correction != NativeCorrection || record.originalGuid != baseline.originalGuid ||
                record.originalPath != baseline.originalPath || record.originalLocalId != ClipId ||
                record.adaptedLocalId != ClipId || record.adaptedGuid != baseline.adaptedGuid ||
                record.adaptedPath != baseline.adaptedPath || record.originalFileHash != baseline.originalFileHash ||
                record.originalMetaHash != baseline.originalMetaHash ||
                record.originalSettingsHash != baseline.originalSettingsHash ||
                record.adaptedFileHash != FileHash(baseline.adaptedPath) ||
                record.adaptedMetaHash != FileHash(baseline.adaptedPath + ".meta"))
                throw new InvalidOperationException("Prepared source provenance/settings changed or preparation failed: " +
                    baseline.adaptedPath + ". Refusing to overwrite; inspect provenance and DiagnoseNativeImports().");
            string expectedSettings = record.adaptedSettingsHash;
            string expectedAvatar = record.avatar;
            ValidatePreparedRig(record, index);
            if (record.adaptedSettingsHash != expectedSettings || record.avatar != expectedAvatar)
                throw new InvalidOperationException("Prepared imported state changed: " + record.adaptedPath);
            RequireOriginalUnchanged(record);
            return record;
        }

        static void ValidatePreparedRig(NativeImportRecord source, int index)
        {
            string path = source.adaptedPath;
            if (!DesignatedNativePath(path) || AssetDatabase.AssetPathToGUID(path) != source.adaptedGuid ||
                FileHash(path) != source.originalFileHash)
                throw new InvalidOperationException("Prepared import bytes/path/GUID mismatch: " + path);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var animator = model ? model.GetComponent<Animator>() : null;
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            var failures = RigFailures(path, model, animator, importer);
            if (failures.Length != 0)
                throw new InvalidOperationException("Prepared rig invalid: " + string.Join("; ", failures));
            if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel || importer.sourceAvatar ||
                File.ReadAllText(path + ".meta").Contains(CopiedAvatarGuid) ||
                AssetDatabase.GetAssetPath(animator.avatar) != path)
                throw new InvalidOperationException("Prepared Avatar must be created by its own model: " + path);
            ValidateMapping(model, importer);
            foreach (var bone in importer.humanDescription.human)
            {
                int humanIndex = Array.IndexOf(HumanTrait.BoneName, bone.humanName);
                var transform = animator.GetBoneTransform((HumanBodyBones)humanIndex);
                if (!transform || transform.name != bone.boneName)
                    throw new InvalidOperationException("Prepared Avatar bone binding mismatch: " + bone.humanName);
            }
            source.adaptedSettingsHash = SettingsFingerprint(importer);
            if (source.adaptedSettingsHash != source.originalSettingsHash)
                throw new InvalidOperationException("Import configuration changed beyond the Avatar dependency: " + path);
            var original = AssetDatabase.LoadAssetAtPath<GameObject>(source.originalPath);
            var originalPaths = original.GetComponentsInChildren<Transform>(true).Select(t =>
                AnimationUtility.CalculateTransformPath(t, original.transform)).OrderBy(p => p).ToArray();
            var nativePaths = model.GetComponentsInChildren<Transform>(true).Select(t =>
                AnimationUtility.CalculateTransformPath(t, model.transform)).OrderBy(p => p).ToArray();
            if (!originalPaths.SequenceEqual(nativePaths))
                throw new InvalidOperationException("Prepared native transform hierarchy changed: " + path);
            var clip = NativeClip(path, source.adaptedGuid, index);
            var originalClip = NativeClip(source.originalPath, source.originalGuid, index);
            if (clip.name != originalClip.name || Mathf.Abs(clip.length - originalClip.length) > .00001f ||
                clip.frameRate != originalClip.frameRate || clip.humanMotion != originalClip.humanMotion)
                throw new InvalidOperationException("Prepared clip differs from original identity/settings: " + path);
            source.adaptedClipName = clip.name;
            source.avatar = CombatExpansionInventory.Identity(animator.avatar);
            RequireOriginalUnchanged(source);
        }

        public static void DiagnoseNativeImports()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/NativeImportDiagnostics.json", JsonUtility.ToJson(new DiagnosticReport
            {
                copiedAvatarPath = AssetDatabase.GUIDToAssetPath(CopiedAvatarGuid),
                scope = "Read-only project-owned native import diagnosis. Paths are keyed by original source GUID. " +
                    "No import configuration, scenes or driver assets are modified.",
                sources = Guids.Select(g => DiagnoseSource(AssetDatabase.AssetPathToGUID(NativePath(g)),
                    NativePath(g))).ToArray()
            }, true));
        }
    }
}
