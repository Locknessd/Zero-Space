using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static AnimationClip OrientationVariantCopy(SourceRecord source, OrientationVariantAsset record)
        {
            record.originalPath = source.path;
            record.variantPath = OrientationVariantAssets + "/" + source.role +
                "_Execution02_OriginalOrientation" + Path.GetExtension(source.path);
            record.originalIdentity = CombatExpansionInventory.Identity(source.asset);
            record.originalSha256 = NativeReferenceHash(source.path);
            record.originalBytes = new FileInfo(source.path).Length;
            record.originalMetaSha256 = NativeReferenceHash(source.path + ".meta");
            var original = AssetImporter.GetAtPath(source.path) as ModelImporter;
            if (!original)
                throw new InvalidOperationException("Missing original model importer.");
            record.originalImporterSettings = EditorJsonUtility.ToJson(original, true);
            var sourceClip = original.clipAnimations.Single(c => c.name == source.clip);
            record.originalKeepOriginalOrientation = sourceClip.keepOriginalOrientation;
            if (record.originalKeepOriginalOrientation)
                throw new InvalidOperationException("Hypothesis precondition changed: source orientation is already true.");
            record.originalClipSettings = JsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(source.asset));
            string originalMeta = File.ReadAllText(source.path + ".meta");
            var guidPattern = new Regex(@"(?m)^guid: ([a-f0-9]{32})\r?$");
            if (guidPattern.Matches(originalMeta).Count != 1 ||
                guidPattern.Match(originalMeta).Groups[1].Value != source.guid)
                throw new InvalidOperationException("Unexpected original metadata GUID.");
            bool exists = File.Exists(record.variantPath);
            if (exists != File.Exists(record.variantPath + ".meta"))
                throw new InvalidOperationException("Incomplete variant destination; inspect before retrying.");
            string variantGuid = Guid.NewGuid().ToString("N");
            if (exists)
            {
                var existingGuid = guidPattern.Match(File.ReadAllText(record.variantPath + ".meta"));
                if (!existingGuid.Success)
                    throw new InvalidOperationException("Existing variant metadata has no GUID.");
                variantGuid = existingGuid.Groups[1].Value;
                if (variantGuid == source.guid || NativeReferenceHash(record.variantPath) != record.originalSha256)
                    throw new InvalidOperationException("Existing destination is not an owned byte-identical copy.");
                string existingPath = AssetDatabase.GUIDToAssetPath(variantGuid);
                if (!string.IsNullOrEmpty(existingPath) && existingPath != record.variantPath)
                    throw new InvalidOperationException("Variant GUID belongs to another path.");
            }
            string variantMeta = guidPattern.Replace(originalMeta, "guid: " + variantGuid);
            // Fail closed on unfamiliar metadata. Preserve every byte except GUID and the one clip flag.
            var clipBlockPattern = new Regex(@"(?m)^      name: " + Regex.Escape(source.clip) +
                @"\r?\n(?:(?!^    - |^  \S)[\s\S])*?^      keepOriginalOrientation: 0(?=\r?$)");
            var matches = clipBlockPattern.Matches(variantMeta);
            if (matches.Count != 1)
                throw new InvalidOperationException("Cannot uniquely locate the Execution02 orientation flag.");
            var block = matches[0];
            variantMeta = variantMeta.Substring(0, block.Index) +
                block.Value.Substring(0, block.Length - 1) + "1" +
                variantMeta.Substring(block.Index + block.Length);
            if (exists && File.ReadAllText(record.variantPath + ".meta") != variantMeta)
                throw new InvalidOperationException("Existing variant settings differ; refusing to replace unrelated edits.");
            if (!exists)
            {
                File.Copy(source.path, record.variantPath, false);
                File.WriteAllText(record.variantPath + ".meta", variantMeta);
            }
            AssetDatabase.ImportAsset(record.variantPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            record.variantSha256 = NativeReferenceHash(record.variantPath);
            record.variantBytes = new FileInfo(record.variantPath).Length;
            record.variantMetaSha256 = NativeReferenceHash(record.variantPath + ".meta");
            record.bytesMatch = record.originalBytes == record.variantBytes &&
                record.originalSha256 == record.variantSha256;
            record.metadataMatchesOnlyIntendedChanges = File.ReadAllText(record.variantPath + ".meta") == variantMeta;
            var importer = AssetImporter.GetAtPath(record.variantPath) as ModelImporter;
            if (!importer)
                throw new InvalidOperationException("Missing variant model importer.");
            record.variantImporterSettings = EditorJsonUtility.ToJson(importer, true);
            record.variantKeepOriginalOrientation = importer.clipAnimations.Single(c =>
                c.name == source.clip).keepOriginalOrientation;
            var clips = AssetDatabase.LoadAllAssetsAtPath(record.variantPath).OfType<AnimationClip>().ToArray();
            var clip = clips.Single(c => c.name == source.clip);
            record.variantIdentity = CombatExpansionInventory.Identity(clip);
            record.variantClipSettings = JsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(clip));
            if (!record.bytesMatch || !record.metadataMatchesOnlyIntendedChanges ||
                !record.variantKeepOriginalOrientation || record.variantIdentity != variantGuid + ":" + source.localId)
                throw new InvalidOperationException("Variant bytes, identity or importer settings guard failed.");
            var originals = AssetDatabase.LoadAllAssetsAtPath(source.path).OfType<AnimationClip>().ToArray();
            if (clips.Length != originals.Length)
                throw new InvalidOperationException("Variant clip count changed.");
            foreach (var before in originals)
            {
                var after = clips.Single(c => c.name == before.name);
                if (after.humanMotion != before.humanMotion || after.legacy != before.legacy ||
                    after.length != before.length || after.frameRate != before.frameRate)
                    throw new InvalidOperationException("Variant clip type or timing changed: " + before.name);
                var beforeSettings = AnimationUtility.GetAnimationClipSettings(before);
                var afterSettings = AnimationUtility.GetAnimationClipSettings(after);
                if (before.name == source.clip)
                {
                    if (!afterSettings.keepOriginalOrientation)
                        throw new InvalidOperationException("Variant clip does not retain original orientation.");
                    afterSettings.keepOriginalOrientation = beforeSettings.keepOriginalOrientation;
                }
                if (JsonUtility.ToJson(beforeSettings) != JsonUtility.ToJson(afterSettings))
                    throw new InvalidOperationException("Other imported clip settings changed: " + before.name);
            }
            record.allClipSettingsChecked = true;
            record.status = "PROVISIONAL prepared; source preservation checked at session exit";
            return clip;
        }
    }
}
