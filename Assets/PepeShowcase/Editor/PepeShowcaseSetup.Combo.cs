using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseSetup
    {
        internal const string ComboScenePath = "Assets/Scenes/PepeAssassinCombo.unity";

        [MenuItem("Tools/Pepe Showcase/Create Assassin long combo")]
        public static void BuildAssassinCombo()
        {
            var original = AssetDatabase.LoadAssetAtPath<PepeShowcaseLibrary>(Root + "/ShowcaseLibrary.asset");
            if (!original) throw new InvalidOperationException("Create the individual Assassin showcase first.");
            if (SceneManager.GetSceneByPath(ComboScenePath).isLoaded)
                throw new InvalidOperationException("Close the generated combo scene before rebuilding it.");
            var order = new[] { "Combo04", "Combo03", "Combo04", "Combo03", "Skill05" };
            var selected = order.Select(id => original.takes.Single(take =>
                take.animatorName == "Frank_Assassin_" + id && take.nativeWeaponEffects)).ToArray();
            var report = new StringBuilder("ASSASSIN LONG COMBO\n");
            var track = MakeComboTrack(selected, report);
            string trackPath = Root + "/AssassinLongCombo.asset";
            var storedTrack = AssetDatabase.LoadAssetAtPath<FrankAttackTrack>(trackPath);
            if (storedTrack)
            {
                EditorUtility.CopySerialized(track, storedTrack);
                UnityEngine.Object.DestroyImmediate(track);
                track = storedTrack;
            }
            else AssetDatabase.CreateAsset(track, trackPath);
            EditorUtility.SetDirty(track);
            AssetDatabase.SaveAssetIfDirty(track);

            string libraryPath = Root + "/AssassinComboLibrary.asset";
            var library = AssetDatabase.LoadAssetAtPath<PepeShowcaseLibrary>(libraryPath);
            if (!library)
            {
                library = UnityEngine.Object.Instantiate(original);
                library.name = "AssassinComboLibrary";
                AssetDatabase.CreateAsset(library, libraryPath);
            }
            library.takes = new[] { MakeComboTake(selected, track) };
            library.introSeconds = .6f;
            library.leadSeconds = .18f;
            library.holdSeconds = .35f;
            library.outroSeconds = .6f;
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            SaveComboScene(library);
            report.AppendLine("Duration: " + track.Duration.ToString("F3") + " seconds; cues: " +
                library.takes[0].cues.Length + "; same driver throughout; no inter-attack lead/hold.");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/ComboSetup.txt", report.ToString());
        }

        static FrankAttackTrack MakeComboTrack(PepeShowcaseLibrary.Take[] takes, StringBuilder report)
        {
            var track = ScriptableObject.CreateInstance<FrankAttackTrack>();
            track.name = "AssassinLongCombo";
            track.steps = new FrankAttackTrack.Step[takes.Length];
            float next = 0;
            for (int i = 0; i < takes.Length; i++)
            {
                var take = takes[i];
                var swings = take.cues.Where(cue => cue.swing).ToArray();
                if (swings.Length == 0) throw new InvalidOperationException("No native cues: " + take.title);
                float first = swings.Min(cue => cue.seconds);
                float last = swings.Max(cue => cue.seconds);
                float start = i == 0 ? 0 : Mathf.Max(0, first - .16f);
                float end = i == takes.Length - 1 ? take.Duration : Mathf.Min(take.Duration, last + .32f);
                track.steps[i] = new FrankAttackTrack.Step
                {
                    stepId = "Assassin_" + (i + 1) + "_" + take.animatorName,
                    clip = take.animation,
                    seconds = next,
                    sourceStartSeconds = start,
                    sourceEndSeconds = end,
                    blendSeconds = i == 0 ? 0 : .12f
                };
                report.AppendLine((i + 1) + ". " + take.title + " at " + next.ToString("F3") +
                    "s; source " + start.ToString("F3") + ".." + end.ToString("F3") +
                    "; blend " + track.steps[i].blendSeconds.ToString("F3") + "s.");
                next += end - start - .14f;
            }
            if (!track.TryValidate(out string error))
                throw new InvalidOperationException(error);
            return track;
        }

        static PepeShowcaseLibrary.Take MakeComboTake(PepeShowcaseLibrary.Take[] takes, FrankAttackTrack track)
        {
            var cues = new List<PepeShowcaseLibrary.Cue>();
            for (int i = 0; i < takes.Length; i++)
            {
                var step = track.steps[i];
                foreach (var cue in takes[i].cues)
                    if (cue.seconds >= step.sourceStartSeconds && cue.seconds <= step.sourceEndSeconds)
                        cues.Add(new PepeShowcaseLibrary.Cue
                        {
                            seconds = step.seconds + cue.seconds - step.sourceStartSeconds,
                            group = cue.group,
                            swing = cue.swing,
                            gun = cue.gun,
                            sounds = cue.sounds,
                            effectStart = step.seconds + cue.effectStart - step.sourceStartSeconds,
                            effectEnd = step.seconds + cue.effectEnd - step.sourceStartSeconds
                        });
            }
            return new PepeShowcaseLibrary.Take
            {
                title = "ASSASSIN / LONG COMBO",
                animatorName = "AssassinLongCombo",
                animation = takes[0].animation,
                attackTrack = track,
                pepeDriver = takes[0].pepeDriver,
                mankeyDriver = takes[0].mankeyDriver,
                nativeWeaponEffects = true,
                cues = cues.OrderBy(cue => cue.seconds).ToArray(),
                yawStart = -12,
                yawEnd = -12,
                cameraDistance = 3.8f
            };
        }

        static void SaveComboScene(PepeShowcaseLibrary library)
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(ComboScenePath) &&
                !AssetDatabase.CopyAsset(ScenePath, ComboScenePath))
                throw new IOException("Could not create Assassin combo scene.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(ComboScenePath, OpenSceneMode.Additive);
            try
            {
                var showcase = scene.GetRootGameObjects()
                    .Select(root => root.GetComponent<PepeAttackShowcase>()).Single(value => value);
                showcase.library = library;
                showcase.autoPlay = true;
                showcase.loop = true;
                showcase.speed = 1;
                ExtendComboFloor(scene);
                EditorUtility.SetDirty(showcase);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save combo scene.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        static void ExtendComboFloor(Scene scene)
        {
            if (scene.GetRootGameObjects().Any(root => root.name == "Combo floor extension")) return;
            var floor = new GameObject("Combo floor extension").transform;
            SceneManager.MoveGameObjectToScene(floor.gameObject, scene);
            var pearl = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Floor pearl.mat");
            var graphite = AssetDatabase.LoadAssetAtPath<Material>(Root + "/Materials/Floor graphite.mat");
            for (int x = -5; x <= 5; x++)
                for (int z = 6; z <= 16; z++)
                    Box("Combo floor tile", floor, new Vector3(x * 2, -.055f, z * 2),
                        new Vector3(2, .1f, 2), (x + z) % 2 == 0 ? pearl : graphite);
        }
    }

    public static partial class FrankRetargetBuilder
    {
        public static void CreatePepeAssassinCombo() => PepeShowcaseSetup.BuildAssassinCombo();
        public static void ValidatePepeAssassinCombo() => PepeShowcaseCapture.ValidateAssassinCombo();
        public static void RecordPepeAssassinCombo() => PepeShowcaseCapture.BeginAssassinCombo();
    }
}
