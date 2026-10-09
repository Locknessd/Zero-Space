using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        public const string AssetsRoot = "Assets/CombatExpansion/SamuraiStudy";
        public const string Output = "GeneratedAssets/CombatExpansion/SamuraiStudy/Sources";
        static readonly string[] Guids = { "3e685dd57e9c78b49b20bef3e8358ae3", "b01b411d366a5a344bddf3428a7bc8f7" };
        static readonly string[] Roles = { "PlayerA", "PlayerB" };
        static readonly string[] Swords = { "BladeR", "Sword_Hold" };

        [Serializable]
        sealed class SourceRecord
        {
            public string role, path, guid, clip, avatar;
            public RootSettings rootSettings;
            public long localId;
            public float durationSeconds, framesPerSecond;
            [NonSerialized] public AnimationClip asset;
        }

        [Serializable]
        sealed class RootSettings
        {
            public bool loopTime, loopBlend, loopBlendOrientation, loopBlendPositionY, loopBlendPositionXZ;
            public bool lockRootRotation, lockRootHeightY, lockRootPositionXZ;
            public bool keepOriginalOrientation, keepOriginalPositionY, keepOriginalPositionXZ, heightFromFeet;
            public float startTime, stopTime, orientationOffsetY, level, cycleOffset;
        }

        [Serializable]
        sealed class DriverRecord
        {
            public string fighter, role, variant, path, sourceAvatar, fighterAvatar;
            public Vector3 scale;
            public string[] retainedRenderers;
        }

        [Serializable]
        sealed class CaptureRecord
        {
            public int execution, direction;
            public string playerA, playerB, playerAAvatar, playerBAvatar, trajectory, sheet;
            public float durationSeconds;
            public float[] sheetSeconds;
        }

        [Serializable]
        sealed class Report
        {
            public string unityVersion = Application.unityVersion;
            public string utc = DateTime.UtcNow.ToString("O");
            public string scope = "Paired native source motion study on actual BattleScene avatars. " +
                "PlayerA/PlayerB are source labels, not attacker/victim assignments. Both carry source swords. " +
                "No contacts, gameplay timing, damage, hit events, actions or character spacing are approved. " +
                "Source A origin faces +Z; B offset (0,0,1.7) faces yaw 180. " +
                "The entire pair rotates yaw +90/-90 for both lane directions; source offsets are unscaled. " +
                "Native calibrated driver Animators apply root motion once at 60Hz. Shorter clips hold their end. " +
                "No grounding, contact alignment or pair scale correction is applied. " +
                "Playback copies disable looping and events; imported source settings are reported unchanged. " +
                "Sheets show fixed side and oblique views; CSV positions and displacement are world metres.";
            public SourceRecord[] clips;
            public DriverRecord[] drivers;
            public CaptureRecord[] captures;
        }

        public static void PrepareDrivers()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            CombatExpansionHumanoidStudy.EnsureFolder(AssetsRoot + "/Drivers");
            Directory.CreateDirectory(Output);
            var sources = ResolveSources();
            File.WriteAllText(Output + "/Scope.txt", new Report().scope);
            using var session = new SourceSession();
            var records = new List<DriverRecord>();
            foreach (var fighter in session.Fighters)
            for (int role = 0; role < 2; role++)
            foreach (bool armed in new[] { true, false })
            {
                string path = DriverPath(fighter.name, role, armed);
                var clips = sources.Where(s => s.role == Roles[role]).Select(s => s.asset).ToArray();
                var driver = FrankRetargetBuilder.BuildHumanoidStudyDriver(fighter.Animator, fighter.name,
                    AssetDatabase.GUIDToAssetPath(Guids[role]), clips, path, armed, Swords);
                ValidateDriver(driver, role, armed, path);
                records.Add(new DriverRecord
                {
                    fighter = fighter.name, role = Roles[role], variant = armed ? "Armed" : "Unarmed", path = path,
                    sourceAvatar = CombatExpansionInventory.Identity(driver.pose.sourceHumanAvatar),
                    fighterAvatar = CombatExpansionInventory.Identity(fighter.Animator.avatar),
                    scale = driver.transform.localScale,
                    retainedRenderers = RendererIdentities(driver)
                });
            }
            File.WriteAllText(Output + "/Drivers.json", JsonUtility.ToJson(new Report
            {
                clips = sources, drivers = records.ToArray()
            }, true));
        }

        static SourceRecord[] ResolveSources()
        {
            var records = new List<SourceRecord>();
            for (int role = 0; role < 2; role++)
            {
                string path = AssetDatabase.GUIDToAssetPath(Guids[role]);
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var animator = model ? model.GetComponent<Animator>() : null;
                if (!animator || !animator.avatar || !animator.avatar.isValid || !animator.avatar.isHuman)
                    throw new InvalidOperationException("Missing valid native Animator Avatar: " + Roles[role]);
                var assets = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().ToArray();
                for (int execution = 1; execution <= 10; execution++)
                {
                    long id = 7400000 + 2 * execution;
                    string identity = Guids[role] + ":" + id;
                    var clip = assets.SingleOrDefault(c => CombatExpansionInventory.Identity(c) == identity);
                    string expected = "Execution" + execution + "_" + Roles[role];
                    if (!clip || clip.name != expected || !clip.humanMotion || clip.length <= 0 || clip.frameRate <= 0)
                        throw new InvalidOperationException("Missing or invalid imported clip " + expected +
                            " at " + identity + " (" + path + ")");
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    var importer = AssetImporter.GetAtPath(path) as ModelImporter;
                    var imported = importer ? importer.clipAnimations.SingleOrDefault(c => c.name == expected) : null;
                    if (imported == null)
                        throw new InvalidOperationException("Missing explicit importer settings for " + identity);
                    records.Add(new SourceRecord
                    {
                        role = Roles[role], path = path, guid = Guids[role], localId = id, clip = clip.name,
                        asset = clip, durationSeconds = clip.length, framesPerSecond = clip.frameRate,
                        avatar = CombatExpansionInventory.Identity(animator.avatar),
                        rootSettings = new RootSettings
                        {
                            loopTime = settings.loopTime, loopBlend = settings.loopBlend,
                            loopBlendOrientation = settings.loopBlendOrientation,
                            loopBlendPositionY = settings.loopBlendPositionY,
                            loopBlendPositionXZ = settings.loopBlendPositionXZ,
                            lockRootRotation = imported.lockRootRotation, lockRootHeightY = imported.lockRootHeightY,
                            lockRootPositionXZ = imported.lockRootPositionXZ,
                            keepOriginalOrientation = settings.keepOriginalOrientation,
                            keepOriginalPositionY = settings.keepOriginalPositionY,
                            keepOriginalPositionXZ = settings.keepOriginalPositionXZ,
                            heightFromFeet = settings.heightFromFeet, startTime = settings.startTime,
                            stopTime = settings.stopTime, orientationOffsetY = settings.orientationOffsetY,
                            level = settings.level, cycleOffset = settings.cycleOffset
                        }
                    });
                }
            }
            return records.ToArray();
        }

        static string DriverPath(string fighter, int role, bool armed)
        {
            return AssetsRoot + "/Drivers/" + fighter + "_" + Roles[role] + "_" +
                (armed ? "Armed" : "Unarmed") + ".prefab";
        }

        static void ValidateDriver(FrankTestDriver driver, int role, bool armed, string path)
        {
            if (!driver || !driver.pose || !driver.pose.driver || !driver.pose.sourceHumanAvatar ||
                !driver.pose.sourceHumanAvatar.isValid || !driver.pose.sourceHumanAvatar.isHuman ||
                driver.pose.driver.avatar != driver.pose.sourceHumanAvatar)
                throw new InvalidOperationException("Unretargetable Samurai driver Animator Avatar: " + path);
            string avatarPath = AssetDatabase.GetAssetPath(driver.pose.driver.avatar);
            if (AssetDatabase.AssetPathToGUID(avatarPath) != Guids[role])
                throw new InvalidOperationException("Samurai driver source Avatar identity mismatch: " + path);
            var transforms = driver.GetComponentsInChildren<Transform>(true);
            foreach (string socket in new[] { "RightHand", "BladeR", "Sword_Hold" })
                if (!transforms.Any(t => t.name == socket))
                    throw new InvalidOperationException("Missing Samurai socket " + socket + ": " + path);
            var renderers = driver.GetComponentsInChildren<Renderer>(true);
            if (!armed && renderers.Length != 0)
                throw new InvalidOperationException("Unarmed Samurai driver contains native renderers: " + path);
            if (!armed)
                return;
            foreach (string sword in Swords)
                if (!renderers.Any(r => Under(r.transform, driver.transform, sword)))
                    throw new InvalidOperationException("Missing retained Samurai sword " + sword + ": " + path);
            if (renderers.Any(r => !r.enabled || !Swords.Any(s => Under(r.transform, driver.transform, s))) ||
                renderers.Any(r => !driver.pose.weaponRenderers.Contains(r)))
                throw new InvalidOperationException("Unexpected native body or hidden sword renderer: " + path);
        }

        static bool Under(Transform node, Transform root, string name)
        {
            for (var current = node; current && current != root; current = current.parent)
                if (current.name == name)
                    return true;
            return false;
        }

        static string[] RendererIdentities(FrankTestDriver driver)
        {
            return driver.GetComponentsInChildren<Renderer>(true).Select(r =>
                AnimationUtility.CalculateTransformPath(r.transform, driver.transform) + " materials=" +
                string.Join(";", r.sharedMaterials.Select(m => m ? m.name + "=" +
                    CombatExpansionInventory.Identity(m) : "<missing>"))).ToArray();
        }
    }
}
