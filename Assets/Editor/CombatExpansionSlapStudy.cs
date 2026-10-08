using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        public const string AssetsRoot = "Assets/CombatExpansion/SlapStudy";
        public const string Output = "GeneratedAssets/CombatExpansion/SlapStudy/Sources";
        const long ClipId = 8993902097722301239;
        static readonly string[] Guids =
        {
            "881fa13cf2c568b4e86a1623d57f01a9", "98722fdde98b7b245a9c0df7f1a1143f",
            "67e760d2924bb9246a4ac9fe9ca09e78", "e8b01f6f8c4cfec4181a03b568ed143e"
        };

        [Serializable]
        sealed class SourceRecord
        {
            public string label, path, guid, clip, avatar, modelIdentity, importerJson, importerMeta;
            public long localId;
            public float durationSeconds, framesPerSecond;
            public bool importedLoopTime, importedLoopBlend;
            public Vector3 originalModelPosition, originalModelScale;
            public Quaternion originalModelRotation;
            public string[] nativeRig;
            [NonSerialized] public AnimationClip asset;
        }

        [Serializable]
        sealed class BoneRecord
        {
            public string rig, bone, path;
            public Vector3 position, localPosition;
            public Quaternion rotation, localRotation;
        }

        [Serializable]
        sealed class CaptureRecord
        {
            public string fighter, label, guid, driverPath, sourceAvatar, fighterAvatar, trajectory;
            public long localId;
            public float durationSeconds;
            public int trajectorySamples;
            public Vector3 calibratedNativeScale;
            public BoneRecord[] initialBones;
            public float[] sheetSeconds;
            public string[] sheets;
        }

        [Serializable]
        sealed class Report
        {
            public string unityVersion = Application.unityVersion;
            public string utc = DateTime.UtcNow.ToString("O");
            public string scope = "Eight independent source captures: four SlapFace clips on Mankey and Pepe. " +
                "Sequence and A/B labels identify sources only; no attacker assignment or verified pair offset exists. " +
                "No paired choreography, contact timing, reaction timing or registration is approved. " +
                "Original model transforms, native rig paths, Avatar identities and unchanged imports are recorded. " +
                "Capture roots normalize to origin and identity rotation; native rig is calibrated to each fighter. " +
                "Initial bones record this calibrated playback pose, not a verified pair offset. " +
                "A manually evaluated native Humanoid Animator applies root motion once at 60Hz. " +
                "Fighter Animator is disabled; calibrated native hips transfer directly without extra root integration. " +
                "Playback copies alone disable looping and events. CSV positions are world metres. " +
                "Each timed tile shows side on left and oblique on right; sheets sample every seven 60Hz ticks " +
                "plus the exact final pose (at least eight poses per second).";
            public SourceRecord[] clips;
            public CaptureRecord[] captures;
        }

        public static void PrepareDrivers()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            CombatExpansionHumanoidStudy.EnsureFolder(AssetsRoot + "/Drivers");
            var sources = ResolveSources();
            using var session = new SourceSession();
            foreach (var fighter in session.Fighters)
            foreach (var source in sources)
            {
                string path = DriverPath(fighter.name, source);
                var driver = FrankRetargetBuilder.BuildHumanoidStudyDriver(fighter.Animator, fighter.name,
                    source.path, new[] { source.asset }, path, false);
                ValidateDriver(driver, source, path);
            }
        }

        static SourceRecord[] ResolveSources()
        {
            var records = new List<SourceRecord>();
            for (int index = 0; index < Guids.Length; index++)
            {
                string path = AssetDatabase.GUIDToAssetPath(Guids[index]);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var animator = model ? model.GetComponent<Animator>() : null;
                var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                string[] failures = RigFailures(path, model, animator, importer);
                if (failures.Length != 0)
                    throw new InvalidOperationException("Invalid exposed SlapFace native rig: " + path +
                        " | " + string.Join("; ", failures) + ". Run CombatExpansionSlapStudy.DiagnoseSources().");
                var clip = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().SingleOrDefault(c =>
                    CombatExpansionInventory.Identity(c) == Guids[index] + ":" + ClipId);
                float expectedDuration = index < 2 ? 10.2000008f : 7.36666727f;
                if (!clip || !clip.humanMotion || clip.frameRate <= 0 ||
                    Mathf.Abs(clip.length - expectedDuration) > .001f)
                    throw new InvalidOperationException("SlapFace source identity or duration mismatch: " + path);
                var settings = AnimationUtility.GetAnimationClipSettings(clip);
                records.Add(new SourceRecord
                {
                    label = "Sequence" + (index / 2 + 1) + "_" + (index % 2 == 0 ? "A" : "B"),
                    path = path,
                    guid = Guids[index],
                    localId = ClipId,
                    clip = clip.name,
                    asset = clip,
                    durationSeconds = clip.length,
                    framesPerSecond = clip.frameRate,
                    avatar = CombatExpansionInventory.Identity(animator.avatar),
                    modelIdentity = CombatExpansionInventory.Identity(model),
                    importerJson = EditorJsonUtility.ToJson(importer, true),
                    importerMeta = File.ReadAllText(path + ".meta"),
                    importedLoopTime = settings.loopTime,
                    importedLoopBlend = settings.loopBlend,
                    originalModelPosition = model.transform.localPosition,
                    originalModelRotation = model.transform.localRotation,
                    originalModelScale = model.transform.localScale,
                    nativeRig = model.GetComponentsInChildren<Transform>(true).Select(t =>
                        AnimationUtility.CalculateTransformPath(t, model.transform)).ToArray()
                });
            }
            return records.ToArray();
        }

        static string DriverPath(string fighter, SourceRecord source)
        {
            return AssetsRoot + "/Drivers/" + fighter + "_" + source.guid + ".prefab";
        }

        static void ValidateDriver(FrankTestDriver driver, SourceRecord source, string path)
        {
            if (!driver || !driver.pose || !driver.pose.driver || !driver.pose.sourceHumanAvatar ||
                !driver.pose.sourceHumanAvatar.isValid || !driver.pose.sourceHumanAvatar.isHuman ||
                driver.pose.driver.avatar != driver.pose.sourceHumanAvatar ||
                CombatExpansionInventory.Identity(driver.pose.sourceHumanAvatar) != source.avatar ||
                driver.GetComponentsInChildren<Renderer>(true).Length != 0 ||
                driver.pose.weaponRenderers.Length != 0 || driver.pose.sourceClips.Length != 1 ||
                driver.pose.sourceClips[0] != source.asset)
                throw new InvalidOperationException("Missing or stale unarmed SlapFace driver: " + path);
        }
    }
}
