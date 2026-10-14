using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static partial class BattleRosterSetup
{
    [MenuItem("Tools/Battle/Characters/Convert four models to Humanoid")]
    public static void ConvertHumanoids()
    {
        Directory.CreateDirectory(Review);
        var backup = Review + "/BeforeHumanoid";
        Directory.CreateDirectory(backup);
        var report = new StringBuilder();
        foreach (string id in Models)
        {
            string path = ModelPath(id);
            string saved = backup + "/" + id + ".fbx.meta";
            if (!File.Exists(saved))
                File.Copy(path + ".meta", saved);
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            var map = HumanMap();
            importer.animationType = ModelImporterAnimationType.Generic;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.sourceAvatar = null;
            importer.optimizeGameObjects = false;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.bakeAxisConversion = true;
            importer.SaveAndReimport();
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var transforms = model.GetComponentsInChildren<Transform>(true);
            foreach (var bone in map.Values)
                if (transforms.Count(t => t.name == bone) != 1)
                    throw new InvalidOperationException(id + ": missing/ambiguous bone " + bone);
            importer.animationType = ModelImporterAnimationType.Human;
            var human = map.Select(p => new HumanBone
            {
                humanName = HumanTrait.BoneName.Single(n => n.Replace(" ", "") == p.Key),
                boneName = p.Value,
                limit = new HumanLimit { useDefaultValues = true }
            }).ToArray();
            var reference = UnityEngine.Object.Instantiate(model);
            EnforceReferencePose(reference, human);
            transforms = reference.GetComponentsInChildren<Transform>(true);
            importer.humanDescription = new HumanDescription
            {
                human = human,
                skeleton = transforms.Select(t => new SkeletonBone
                {
                    name = t.name,
                    position = t.localPosition,
                    rotation = t.localRotation,
                    scale = t.localScale
                }).ToArray(),
                upperArmTwist = .5f,
                lowerArmTwist = .5f,
                upperLegTwist = .5f,
                lowerLegTwist = .5f,
                armStretch = .05f,
                legStretch = .05f
            };
            UnityEngine.Object.DestroyImmediate(reference);
            importer.SaveAndReimport();
            var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().Single();
            if (!avatar.isValid || !avatar.isHuman)
                throw new InvalidOperationException(id + ": Unity rejected Humanoid avatar.");
            foreach (var entry in map)
                if (!avatar.humanDescription.human.Any(h => h.humanName.Replace(" ", "") == entry.Key &&
                    h.boneName == entry.Value))
                    throw new InvalidOperationException(id + ": mapping not retained: " + entry.Key);
            report.AppendLine(id + ": Humanoid, Create From This Model, valid=True, mapped=" + map.Count);
            foreach (var bone in avatar.humanDescription.human)
                report.AppendLine(" " + bone.humanName + " = " + bone.boneName);
            File.WriteAllText(Review + "/Humanoid.txt", report.ToString());
        }
    }

    static Dictionary<string, string> HumanMap()
    {
        var map = new Dictionary<string, string>
        {
            ["Hips"] = "Bip001 Pelvis",
            ["Spine"] = "spine_01",
            ["Chest"] = "spine_02",
            ["UpperChest"] = "spine_03",
            ["Neck"] = "neck_01",
            ["Head"] = "head"
        };
        foreach (string side in new[] { "Left", "Right" })
        {
            string suffix = side == "Left" ? "_l" : "_r";
            string[] humans = { "Shoulder", "UpperArm", "LowerArm", "Hand", "UpperLeg", "LowerLeg", "Foot", "Toes" };
            string[] bones = { "clavicle", "upperarm", "lowerarm", "hand", "thigh", "calf", "foot", "ball" };
            for (int i = 0; i < humans.Length; i++)
                map[side + humans[i]] = bones[i] + suffix;
            foreach (string digit in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
            {
                string bone = digit == "Little" ? "pinky" : digit.ToLowerInvariant();
                string[] segments = { "Proximal", "Intermediate", "Distal" };
                for (int i = 0; i < segments.Length; i++)
                    map[side + digit + segments[i]] = bone + "_0" + (i + 1) + suffix;
            }
        }
        return map;
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void ConvertBattleModelsToHumanoid() => BattleRosterSetup.ConvertHumanoids();
    }
}
