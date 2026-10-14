using System;
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
        internal const string CompleteScenePath = "Assets/Scenes/PepeCompleteCombo.unity";

        [MenuItem("Tools/Pepe Showcase/Create complete combo video scene")]
        public static void BuildCompleteCombo()
        {
            var original = AssetDatabase.LoadAssetAtPath<PepeShowcaseLibrary>(Root + "/ShowcaseLibrary.asset");
            if (!original) throw new InvalidOperationException("The original attack showcase is missing.");
            if (SceneManager.GetSceneByPath(CompleteScenePath).isLoaded)
                throw new InvalidOperationException("Close the generated complete combo scene before rebuilding it.");
            var selected = new[]
            {
                original.takes.Single(take => take.title == "CRITICAL DUAL"),
                original.takes.Single(take => take.title == "COMBO 03"),
                original.takes.Single(take => take.animatorName == "Frank_Assassin_Combo04"),
                original.takes.Single(take => take.animatorName == "Frank_Assassin_Combo03"),
                original.takes.Single(take => take.animatorName == "Frank_Assassin_Skill05")
            };
            if (selected.Select(take => take.animation).Distinct().Count() != selected.Length)
                throw new InvalidOperationException("Complete sequence must not repeat any attack clip.");
            string path = Root + "/CompleteComboLibrary.asset";
            var library = AssetDatabase.LoadAssetAtPath<PepeShowcaseLibrary>(path);
            if (!library)
            {
                library = UnityEngine.Object.Instantiate(original);
                library.name = "CompleteComboLibrary";
                AssetDatabase.CreateAsset(library, path);
            }
            var report = new StringBuilder("COMPLETE COMBO - FIVE UNIQUE ATTACKS\n");
            library.takes = selected.Select((take, index) => CompilationTake(take, index, report)).ToArray();
            library.continuous = true;
            library.entryBlendSeconds = .12f;
            library.motionAccentMaterial = CompleteMotionMaterial();
            library.introSeconds = .8f;
            library.leadSeconds = 0;
            library.holdSeconds = 0;
            library.outroSeconds = .8f;
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            SaveCompleteScene(library);
            report.AppendLine("Timeline duration including titles: " + library.Duration.ToString("F3") + "s.");
            report.AppendLine("30 slash cues, 3 gun flashes; native weapon rigs retained; no repeated clip or loop.");
            report.AppendLine("Skill05: fixed shot centered on the jump; smooth lead-in during the preceding take.");
            report.AppendLine("Extra VFX: blade glints, motion wakes, takeoff and landing rings.");
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/CompleteSetup.txt", report.ToString());
        }

        static PepeShowcaseLibrary.Take CompilationTake(PepeShowcaseLibrary.Take source, int index,
            StringBuilder report)
        {
            var take = JsonUtility.FromJson<PepeShowcaseLibrary.Take>(JsonUtility.ToJson(source));
            float first = source.cues.Min(cue => cue.effectEnd > cue.effectStart ?
                Mathf.Min(cue.seconds, cue.effectStart) : cue.seconds);
            float last = source.cues.Max(cue => Mathf.Max(cue.seconds, cue.effectEnd));
            take.sourceStartSeconds = index == 0 ? 0 : Mathf.Max(0, first - .2f);
            take.sourceEndSeconds = index == 4 ? source.animation.length :
                Mathf.Min(source.animation.length, last + .25f);
            take.attackTrack = null;
            take.cameraDistance = 9;
            take.lockCamera = index == 4;
            take.yawStart = -12;
            take.yawEnd = -12;
            foreach (var cue in take.cues)
            {
                cue.seconds -= take.sourceStartSeconds;
                if (cue.effectEnd > cue.effectStart)
                {
                    cue.effectStart -= take.sourceStartSeconds;
                    cue.effectEnd -= take.sourceStartSeconds;
                }
            }
            report.AppendLine((index + 1) + ". " + take.title + "; source " +
                take.sourceStartSeconds.ToString("F3") + ".." + take.sourceEndSeconds.ToString("F3") +
                "s; " + take.cues.Count(cue => cue.swing) + " slash cues; " +
                take.cues.Count(cue => cue.gun) + " gun flashes; minimum camera distance 9m.");
            return take;
        }

        static void SaveCompleteScene(PepeShowcaseLibrary library)
        {
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(CompleteScenePath) &&
                !AssetDatabase.CopyAsset(ScenePath, CompleteScenePath))
                throw new IOException("Could not copy the complete combo scene.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(CompleteScenePath, OpenSceneMode.Additive);
            try
            {
                var showcase = scene.GetRootGameObjects().Select(root => root.GetComponent<PepeAttackShowcase>())
                    .Single(value => value);
                showcase.library = library;
                showcase.autoPlay = true;
                showcase.loop = false;
                showcase.speed = 1;
                ExtendComboFloor(scene);
                EditorUtility.SetDirty(showcase);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save complete combo scene.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }
    }

    public static partial class FrankRetargetBuilder
    {
        public static void CreatePepeCompleteCombo() => PepeShowcaseSetup.BuildCompleteCombo();
        public static void ValidatePepeCompleteCombo() => PepeShowcaseCapture.ValidateCompleteCombo();
        public static void RecordPepeCompleteCombo() => PepeShowcaseCapture.BeginCompleteCombo();
    }
}
