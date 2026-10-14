using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseSetup
    {
        internal const string BomScenePath = "Assets/Scenes/BOMPepeTrailer.unity";

        [MenuItem("Tools/Pepe Showcase/Create BOM Pepe trailer")]
        public static void BuildBomTrailer()
        {
            if (SceneManager.GetSceneByPath(BomScenePath).isLoaded)
                throw new InvalidOperationException("Close the generated BOM trailer scene before rebuilding it.");
            var original = AssetDatabase.LoadAssetAtPath<PepeShowcaseLibrary>(Root + "/CompleteComboLibrary.asset");
            if (!original) throw new InvalidOperationException("Create the complete combo scene first.");
            string path = Root + "/BomPepeTrailerLibrary.asset";
            var library = AssetDatabase.LoadAssetAtPath<PepeShowcaseLibrary>(path);
            if (!library)
            {
                library = UnityEngine.Object.Instantiate(original);
                library.name = "BomPepeTrailerLibrary";
                AssetDatabase.CreateAsset(library, path);
            }
            library.takes = original.takes.Select(take =>
                JsonUtility.FromJson<PepeShowcaseLibrary.Take>(JsonUtility.ToJson(take))).ToArray();
            library.bomTrailer = true;
            library.continuous = true;
            library.introSeconds = 17;
            library.outroSeconds = 2.2f;
            library.slashPower = 1.7f;
            library.slashAfterglow = .32f;
            library.airCutLifetime = .72f;
            library.startupAnimation = AssetDatabase.LoadAllAssetsAtPath("Assets/pepe/source/pepFBX@Taunt.fbx")
                .OfType<AnimationClip>().Single(clip => !clip.name.StartsWith("__preview__"));
            if (!library.startupAnimation.isHumanMotion)
                throw new InvalidOperationException("Pepe startup must be a Humanoid animation.");
            library.startupPoseSeconds = library.startupAnimation.length * .6f;
            library.motionAccentMaterial = CompleteMotionMaterial();
            foreach (var take in library.takes)
            {
                take.slashScale *= 1.65f;
                take.trailLifetime = .17f;
                take.trailColor = new Color(.3f, .9f, 1, .9f);
                take.cameraDistance = 8.2f;
            }
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssetIfDirty(library);
            if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(BomScenePath) &&
                !AssetDatabase.CopyAsset(CompleteScenePath, BomScenePath))
                throw new IOException("Could not copy the BOM trailer scene.");
            var previous = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.OpenScene(BomScenePath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var showcase = scene.GetRootGameObjects().Select(root => root.GetComponent<PepeAttackShowcase>())
                    .Single(value => value);
                showcase.library = library;
                showcase.autoPlay = true;
                showcase.loop = false;
                showcase.director.margin = .025f;
                showcase.director.cinematicAttackReference = true;
                var intro = showcase.GetComponent<BomTrailerIntro>();
                if (!intro) intro = showcase.gameObject.AddComponent<BomTrailerIntro>();
                intro.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/UI/Fonts/Nunito/Nunito-Black.ttf");
                StyleBomStage(scene, showcase);
                if (AssetDatabase.LoadAssetAtPath<Texture2D>(MemeArenaSetup.Root + "/Textures/MemeGraffitiAtlas.png"))
                    MemeArenaSetup.Build(scene);
                MemeBladeAudioSetup.ApplyToShowcase(library);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save BOM trailer.");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
            File.WriteAllText(Output + "/BomSetup.txt",
                "BOM / Battle of meme; Pepe startup and frozen character reveal; 17s intro.\n" +
                "Startup: " + library.startupAnimation.name + "; length=" + library.startupAnimation.length +
                "; freeze sample=" + library.startupPoseSeconds + ".\n" +
                "5 unique connected attacks; 30 slashes and 3 gun flashes; final blink shot locked.\n" +
                "170% blade reach; soft edge gradients; 320ms slash glow and 720ms suspended air cuts.\n" +
                "Cinematic attack reference: directed three-quarter angles, smooth body tracking and fitted dolly.\n" +
                "Enhanced muzzle flashes: bright core, flame jet, pressure ring, sparks and fading smoke.\n" +
                "Full duration: " + library.Duration.ToString("F3") + "s.\n");
        }
    }

    public static partial class FrankRetargetBuilder
    {
        public static void CreateBomPepeTrailer() => PepeShowcaseSetup.BuildBomTrailer();
        public static void ValidateBomPepeTrailer() => PepeShowcaseCapture.ValidateBomTrailer();
        public static void RecordBomPepeTrailer() => PepeShowcaseCapture.BeginBomTrailer();
    }
}
