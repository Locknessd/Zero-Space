using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        const string CopiedAvatarGuid = "54d24dcf9716c0c40821dd95a9e62844";

        [Serializable]
        sealed class DiagnosticReport
        {
            public string utc = DateTime.UtcNow.ToString("O");
            public string unityVersion = Application.unityVersion;
            public string scope = "Read-only imported SlapFace rig diagnosis. No scenes, imports, Avatars or " +
                "driver assets are created or modified. Missing copied Avatar references require inspection " +
                "before choosing a valid native rig preparation method.";
            public string copiedAvatarGuid = CopiedAvatarGuid;
            public string copiedAvatarPath;
            public RigDiagnostic[] sources;
        }

        [Serializable]
        sealed class AvatarDiagnostic
        {
            public bool exists, valid, human;
            public string name, identity, assetPath;
        }

        [Serializable]
        sealed class RigDiagnostic
        {
            public string guid, path, avatarSetup, animationType, importerJson, humanDescriptionJson;
            public bool modelExists, rootAnimatorExists, importerExists, optimizeGameObjects;
            public bool expectedClipExists, expectedClipHumanMotion;
            public float expectedClipDuration;
            public AvatarDiagnostic rootAnimatorAvatar, importerSourceAvatar;
            public AvatarDiagnostic[] avatarSubassets;
            public string[] validationFailures, subassets, animatorPaths, transformPaths;
            public string[] missingMappedBones, ambiguousMappedBones;
            public int humanBoneCount, referenceSkeletonBoneCount;
        }

        public static void DiagnoseSources()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var report = new DiagnosticReport
            {
                copiedAvatarPath = AssetDatabase.GUIDToAssetPath(CopiedAvatarGuid),
                sources = Guids.Select(DiagnoseSource).ToArray()
            };
            Directory.CreateDirectory(Output);
            string path = Output + "/SourceDiagnostics.json";
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("SlapFace read-only rig diagnostics: " + Path.GetFullPath(path));
        }

        static RigDiagnostic DiagnoseSource(string guid)
        {
            return DiagnoseSource(guid, AssetDatabase.GUIDToAssetPath(guid));
        }

        static RigDiagnostic DiagnoseSource(string guid, string path)
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            var animator = model ? model.GetComponent<Animator>() : null;
            var assets = AssetDatabase.LoadAllAssetsAtPath(path);
            var transforms = model ? model.GetComponentsInChildren<Transform>(true) : Array.Empty<Transform>();
            var clip = assets.OfType<AnimationClip>().SingleOrDefault(c =>
                CombatExpansionInventory.Identity(c) == guid + ":" + ClipId);
            var human = importer ? importer.humanDescription.human : Array.Empty<HumanBone>();
            return new RigDiagnostic
            {
                guid = guid,
                path = path,
                modelExists = model,
                rootAnimatorExists = animator,
                importerExists = importer,
                avatarSetup = importer ? importer.avatarSetup.ToString() : "<missing>",
                animationType = importer ? importer.animationType.ToString() : "<missing>",
                optimizeGameObjects = importer && importer.optimizeGameObjects,
                rootAnimatorAvatar = DescribeAvatar(animator ? animator.avatar : null),
                importerSourceAvatar = DescribeAvatar(importer ? importer.sourceAvatar : null),
                avatarSubassets = assets.OfType<Avatar>().Select(DescribeAvatar).ToArray(),
                validationFailures = RigFailures(path, model, animator, importer),
                expectedClipExists = clip,
                expectedClipHumanMotion = clip && clip.humanMotion,
                expectedClipDuration = clip ? clip.length : 0,
                subassets = assets.Select(a => a.GetType().Name + " | " + a.name + " | " +
                    CombatExpansionInventory.Identity(a)).ToArray(),
                animatorPaths = model ? model.GetComponentsInChildren<Animator>(true).Select(a =>
                    AnimationUtility.CalculateTransformPath(a.transform, model.transform)).ToArray() :
                    Array.Empty<string>(),
                transformPaths = transforms.Select(t =>
                    AnimationUtility.CalculateTransformPath(t, model.transform)).ToArray(),
                humanBoneCount = human.Length,
                referenceSkeletonBoneCount = importer ? importer.humanDescription.skeleton.Length : 0,
                missingMappedBones = human.Where(h => !transforms.Any(t => t.name == h.boneName))
                    .Select(h => h.humanName + "=" + h.boneName).ToArray(),
                ambiguousMappedBones = human.Where(h => transforms.Count(t => t.name == h.boneName) > 1)
                    .Select(h => h.humanName + "=" + h.boneName).ToArray(),
                importerJson = importer ? EditorJsonUtility.ToJson(importer, true) : "",
                humanDescriptionJson = importer ? JsonUtility.ToJson(importer.humanDescription, true) : ""
            };
        }

        static AvatarDiagnostic DescribeAvatar(Avatar avatar)
        {
            return new AvatarDiagnostic
            {
                exists = avatar,
                valid = avatar && avatar.isValid,
                human = avatar && avatar.isHuman,
                name = avatar ? avatar.name : "",
                identity = avatar ? CombatExpansionInventory.Identity(avatar) : "",
                assetPath = avatar ? AssetDatabase.GetAssetPath(avatar) : ""
            };
        }

        static string[] RigFailures(string path, GameObject model, Animator animator, ModelImporter importer)
        {
            var errors = new List<string>();
            if (!path.StartsWith("Assets/Selected/SlapFace/", StringComparison.Ordinal) && !DesignatedNativePath(path))
                errors.Add("Source path is outside designated SlapFace sources");
            if (!model)
                errors.Add("Imported model is missing");
            if (!animator)
                errors.Add("Imported model root Animator is missing");
            else if (!animator.avatar)
                errors.Add("Imported model root Animator has no Avatar");
            else
            {
                if (!animator.avatar.isValid)
                    errors.Add("Root Animator Avatar is invalid");
                if (!animator.avatar.isHuman)
                    errors.Add("Root Animator Avatar is not Humanoid");
            }
            if (!importer)
                errors.Add("ModelImporter is missing");
            else
            {
                if (importer.animationType != ModelImporterAnimationType.Human)
                    errors.Add("Importer rig is not Humanoid");
                if (importer.optimizeGameObjects)
                    errors.Add("Importer optimizes away native rig transforms");
            }
            return errors.ToArray();
        }
    }
}
