using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        const string NativeRoot = AssetsRoot + "/NativeSources";
        const string NativeCorrection = "CopyFromOther -> CreateFromThisModel; clear missing source Avatar; " +
            "preserve original FBX bytes and complete human description and animation import settings.";

        static string NativePath(string guid)
        {
            return NativeRoot + "/" + guid + "/" + Path.GetFileName(AssetDatabase.GUIDToAssetPath(guid));
        }

        static bool DesignatedNativePath(string path)
        {
            return Guids.Any(g => path == NativePath(g));
        }

        static string HashBytes(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        static string FileHash(string path)
        {
            return HashBytes(File.ReadAllBytes(path));
        }

        static string SettingsFingerprint(ModelImporter importer)
        {
            var json = JObject.Parse(EditorJsonUtility.ToJson(importer));
            // Imported identities and diagnostics are outputs, not import configuration.
            foreach (string key in new[] { "m_ImportedTakeInfos", "m_ImportedRoots", "m_AvatarSetup",
                "m_LastHumanDescriptionAvatarSource" })
                json.Remove(key);
            var model = json["ModelImporter"] as JObject;
            model?.Remove("m_UsedFileIDs");
            var animations = json["animations"] as JObject;
            foreach (string key in new[] { "m_AnimationImportErrors", "m_AnimationImportWarnings",
                "m_AnimationRetargetingWarnings" })
                animations?.Remove(key);
            return HashBytes(Encoding.UTF8.GetBytes(json.ToString(Newtonsoft.Json.Formatting.None)));
        }

        static string ReplaceMetaLine(string meta, string pattern, string replacement)
        {
            var regex = new Regex(pattern, RegexOptions.Multiline);
            if (regex.Matches(meta).Count != 1)
                throw new InvalidOperationException("Unexpected source importer metadata: " + pattern);
            return regex.Replace(meta, replacement);
        }

        static string CorrectedMeta(string originalPath, string guid)
        {
            string meta = File.ReadAllText(originalPath + ".meta");
            meta = ReplaceMetaLine(meta, @"^guid: [0-9a-f]{32}(?=\r?$)", "guid: " + guid);
            meta = ReplaceMetaLine(meta, @"^  avatarSetup: 2(?=\r?$)",
                "  avatarSetup: " + (int)ModelImporterAvatarSetup.CreateFromThisModel);
            meta = ReplaceMetaLine(meta, "^  lastHumanDescriptionAvatarSource: \\{fileID: 9000000, guid: " +
                CopiedAvatarGuid + @", type: 3\}(?=\r?$)", "  lastHumanDescriptionAvatarSource: {fileID: 0}");
            if (meta.Contains(CopiedAvatarGuid))
                throw new InvalidOperationException("Missing Avatar dependency remains in corrected metadata");
            return meta;
        }

        static void ValidateMapping(GameObject model, ModelImporter importer)
        {
            if (!model || !importer || importer.animationType != ModelImporterAnimationType.Human ||
                importer.optimizeGameObjects)
                throw new InvalidOperationException("Expected an exposed native Humanoid model");
            var description = importer.humanDescription;
            var human = description.human;
            var skeleton = description.skeleton;
            if (human == null || skeleton == null || human.Length == 0 || skeleton.Length == 0)
                throw new InvalidOperationException("Native human description is incomplete");
            var transforms = model.GetComponentsInChildren<Transform>(true);
            foreach (var bone in human)
            {
                if (!HumanTrait.BoneName.Contains(bone.humanName) || string.IsNullOrEmpty(bone.boneName) ||
                    human.Count(h => h.humanName == bone.humanName) != 1 ||
                    human.Count(h => h.boneName == bone.boneName) != 1 ||
                    transforms.Count(t => t.name == bone.boneName) != 1 ||
                    skeleton.Count(s => s.name == bone.boneName) != 1)
                    throw new InvalidOperationException("Invalid or ambiguous native mapping: " + bone.humanName);
            }
            for (int i = 0; i < HumanTrait.BoneCount; i++)
                if (HumanTrait.RequiredBone(i) && !human.Any(h => h.humanName == HumanTrait.BoneName[i]))
                    throw new InvalidOperationException("Required human mapping is missing: " + HumanTrait.BoneName[i]);
            foreach (var bone in skeleton)
            {
                float magnitude = Quaternion.Dot(bone.rotation, bone.rotation);
                if (!Finite(bone.position) || !Finite(bone.scale) || !float.IsFinite(magnitude) ||
                    magnitude < .000001f || bone.scale.x == 0 || bone.scale.y == 0 || bone.scale.z == 0)
                    throw new InvalidOperationException("Invalid native reference skeleton: " + bone.name);
            }
        }

        static bool Finite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        static AnimationClip NativeClip(string path, string guid, int index)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().SingleOrDefault(c =>
                CombatExpansionInventory.Identity(c) == guid + ":" + ClipId);
            float duration = index < 2 ? 10.2000008f : 7.36666727f;
            if (!clip || !clip.humanMotion || Mathf.Abs(clip.frameRate - 30f) > .0001f ||
                Mathf.Abs(clip.length - duration) > .001f)
                throw new InvalidOperationException("SlapFace clip identity/duration/rate/humanMotion mismatch: " + path);
            return clip;
        }
    }
}
