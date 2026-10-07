using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionHumanoidStudy
    {
        public const string AssetsRoot = "Assets/CombatExpansion/HumanoidStudy";
        public const string ReportsRoot = "GeneratedAssets/CombatExpansion/HumanoidStudy";
        const string KbRoot = "Assets/Selected/FightingAnimsetPro/Animations/KB_";
        const string AxeRoot = "Assets/Selected/Brutal_DoubleAxe_Anim/Animation/Humanoid/Combo_";

        [Serializable]
        public sealed class StudyRecord
        {
            public string fighter, sourcePath, guid, clip, variant, driverPath, sourceAvatar, fighterAvatar;
            public string trajectory, sheet, role, rootPolicy, status;
            public long localId;
            public float durationSeconds, sourceFrameRate;
            public float[] sheetSeconds;
            public Vector3 rootStart, rootEnd, hipsStart, hipsEnd;
            public int trajectorySamples;
        }

        [Serializable]
        sealed class StudyReport
        {
            public string unityVersion = Application.unityVersion;
            public string utc = DateTime.UtcNow.ToString("O");
            public string verification = "Unpaired source motion study; contacts and gameplay remain unverified";
            public StudyRecord[] clips;
        }

        public static void PrepareDrivers()
        {
            RequireEditor();
            EnsureFolder(AssetsRoot + "/Drivers");
            EnsureFolder(AssetsRoot + "/Materials");
            Directory.CreateDirectory(ReportsRoot);
            var records = Inventory();
            var scene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            var log = new List<string> { "fighter\tsourcePath\tdriver\tscale\tsourceAvatar\tweapons" };
            try
            {
                foreach (var fighter in Fighters(scene.GetRootGameObjects()))
                foreach (var group in records.GroupBy(c => c.path))
                {
                    var clips = group.Select(ResolveClip).ToArray();
                    string path = DriverPath(fighter.name, group.Key);
                    var driver = FrankRetargetBuilder.BuildHumanoidStudyDriver(fighter.Animator, fighter.name,
                        group.Key, clips, path, group.Key.StartsWith(AxeRoot, StringComparison.Ordinal));
                    log.Add(string.Join("\t", fighter.name, group.Key, path, driver.transform.localScale,
                        CombatExpansionInventory.Identity(driver.pose.sourceHumanAvatar),
                        driver.pose.weaponRenderers.Length));
                }
                File.WriteAllLines(ReportsRoot + "/Drivers.tsv", log);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public static void StudyPunches() { Study("Punches", c => c.path == KbRoot + "Punches.fbx"); }
        public static void StudyKicks() { Study("Kicks", c => c.path == KbRoot + "Kicks.fbx"); }
        public static void StudyReactions() { Study("Reactions", c => c.path == KbRoot + "Hits.fbx"); }
        public static void StudyAxeCombos() { Study("AxeCombos", c => c.path.StartsWith(AxeRoot)); }

        static void Study(string category, Func<CombatExpansionInventory.ClipRecord, bool> filter)
        {
            RequireEditor();
            string output = ReportsRoot + "/" + category;
            Directory.CreateDirectory(output);
            var records = Inventory().Where(filter).ToArray();
            if (records.Length == 0)
                throw new InvalidOperationException("No source records for " + category);
            var results = new List<StudyRecord>();
            var sourceScene = EditorSceneManager.OpenPreviewScene(CombatExpansionInventory.Battle);
            try
            {
                foreach (var fighter in Fighters(sourceScene.GetRootGameObjects()))
                foreach (var record in records)
                {
                    string driverPath = DriverPath(fighter.name, record.path);
                    var driver = AssetDatabase.LoadAssetAtPath<FrankTestDriver>(driverPath);
                    if (!driver)
                        throw new InvalidOperationException("Run PrepareDrivers first: " + driverPath);
                    using (var preview = new MotionPreview(fighter.Animator, driver, ResolveClip(record)))
                    {
                        string stem = fighter.name + "_" + record.guid + "_" + record.localId;
                        var result = new StudyRecord
                        {
                            fighter = fighter.name,
                            sourcePath = record.path,
                            guid = record.guid,
                            localId = record.localId,
                            clip = record.name,
                            variant = Path.GetFileNameWithoutExtension(record.path),
                            durationSeconds = preview.Duration,
                            sourceFrameRate = record.frameRate,
                            driverPath = driverPath,
                            sourceAvatar = CombatExpansionInventory.Identity(driver.pose.sourceHumanAvatar),
                            fighterAvatar = CombatExpansionInventory.Identity(fighter.Animator.avatar),
                            trajectory = stem + ".csv",
                            sheet = stem + ".png",
                            role = category == "Reactions" ? "independent reaction" : "independent source action",
                            rootPolicy = "Humanoid playable advances at 60Hz; Animator applies root motion once; " +
                                "calibrated source pelvis transfers directly; no additional root integration",
                            status = "Sampled; visual suitability and contacts require review"
                        };
                        preview.WriteTrajectories(output + "/" + result.trajectory, result);
                        preview.WriteSheet(output + "/" + result.sheet, result);
                        results.Add(result);
                        File.WriteAllText(output + "/Progress.json", JsonUtility.ToJson(new StudyReport
                        {
                            clips = results.ToArray()
                        }, true));
                    }
                }
                File.WriteAllText(output + "/Study.json", JsonUtility.ToJson(new StudyReport
                {
                    clips = results.ToArray()
                }, true));
                WriteIndex(output, results);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(sourceScene);
            }
        }

        static CharacterCombat[] Fighters(GameObject[] roots)
        {
            var all = roots.SelectMany(r => r.GetComponentsInChildren<CharacterCombat>(true)).ToArray();
            var fighters = new[] { "Mankey", "Pepe" }.Select(name => all.Single(f => f.name == name)).ToArray();
            foreach (var fighter in fighters)
                if (!fighter.Animator || !fighter.Animator.avatar || !fighter.Animator.avatar.isValid ||
                    !fighter.Animator.avatar.isHuman)
                    throw new InvalidOperationException("Invalid actual fighter Avatar: " + fighter.name);
            return fighters;
        }

        static CombatExpansionInventory.ClipRecord[] Inventory()
        {
            var report = JsonUtility.FromJson<CombatExpansionInventory.Report>(
                File.ReadAllText(CombatExpansionInventory.Output + "/SourceInventory.json"));
            var records = report.clips.Where(c => c.path == KbRoot + "Punches.fbx" ||
                c.path == KbRoot + "Kicks.fbx" || c.path == KbRoot + "Hits.fbx" ||
                c.path.StartsWith(AxeRoot, StringComparison.Ordinal)).OrderBy(c => c.path, StringComparer.Ordinal)
                .ThenBy(c => c.localId).ToArray();
            if (records.Select(c => c.guid + ":" + c.localId).Distinct().Count() != records.Length)
                throw new InvalidOperationException("Duplicate stable source identities in inventory");
            return records;
        }

        static AnimationClip ResolveClip(CombatExpansionInventory.ClipRecord record)
        {
            var clip = AssetDatabase.LoadAllAssetsAtPath(record.path).OfType<AnimationClip>().SingleOrDefault(c =>
                CombatExpansionInventory.Identity(c) == record.guid + ":" + record.localId);
            if (!clip || !clip.humanMotion || !record.humanoid ||
                Mathf.Abs(clip.length - record.durationSeconds) > .001f)
                throw new InvalidOperationException("Inventory mismatch or nonhumanoid clip: " + record.path);
            return clip;
        }

        static string DriverPath(string fighter, string path)
        {
            return AssetsRoot + "/Drivers/" + fighter + "_" + AssetDatabase.AssetPathToGUID(path) + ".prefab";
        }

        internal static void RequireEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before isolated humanoid studies");
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void WriteIndex(string output, List<StudyRecord> records)
        {
            var html = new System.Text.StringBuilder("<!doctype html><meta charset='utf-8'>" +
                "<title>Humanoid source motion study</title><style>body{background:#18202b;color:#eee;" +
                "font:16px sans-serif}img{max-width:100%}section{margin:2em 0}</style>" +
                "<h1>Independent source motion study</h1><p>Chronological left to right, top to bottom. " +
                "CSV coordinates are world metres. No paired contact timings are inferred.</p>");
            foreach (var item in records)
            {
                string label = System.Net.WebUtility.HtmlEncode(
                    item.fighter + " / " + item.clip + " / " + item.variant);
                html.Append("<section><h2>").Append(label).Append("</h2><p>").Append(item.guid).Append(":")
                    .Append(item.localId).Append("</p><img src='").Append(item.sheet).Append("'><p>Seconds: ")
                    .Append(string.Join(", ", item.sheetSeconds.Select(t => t.ToString("R",
                        System.Globalization.CultureInfo.InvariantCulture))))
                    .Append("</p><a href='").Append(item.trajectory).Append("'>60Hz trajectories</a></section>");
            }
            File.WriteAllText(output + "/index.html", html.ToString());
        }
    }
}
