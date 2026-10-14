using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using ZeroSpace.Showcase;

public static partial class MemeArenaSetup
{
    internal const string Root = "Assets/Rendering/MemeArena";
    internal const string Review = "GeneratedAssets/MemeArena";
    internal const string ArenaName = "BOM Meme Plaza";
    static readonly string[] Scenes = { "Assets/Scenes/BattleScene.unity", "Assets/Scenes/BOMPepeTrailer.unity" };

    [MenuItem("Tools/Battle/Build meme plaza and powerful slashes")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("Build the map in Edit mode.");
        Directory.CreateDirectory(Review);
        string backup = Review + "/SceneBackup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(backup);
        PrepareAssets();
        var previous = SceneManager.GetActiveScene();
        try
        {
            foreach (string path in Scenes)
            {
                var scene = SceneManager.GetSceneByPath(path);
                bool opened = !scene.IsValid() || !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    if (!EditorSceneManager.SaveScene(scene, backup + "/" + Path.GetFileName(path), true))
                        throw new IOException("Could not preserve the existing scene: " + path);
                    SceneManager.SetActiveScene(scene);
                    Build(scene);
                    EditorSceneManager.MarkSceneDirty(scene);
                    if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save map: " + path);
                }
                finally
                {
                    if (opened) EditorSceneManager.CloseScene(scene, true);
                }
            }
            MemeBladeAudioSetup.Apply();
        }
        finally
        {
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
        File.WriteAllText(Review + "/Install.txt", "Meme Plaza installed in BattleScene and BOMPepeTrailer.\n" +
            "Previous scene copies, including loaded unsaved edits: " + backup + "\n");
        Validate();
    }

    public static void Build(Scene scene)
    {
        foreach (var old in scene.GetRootGameObjects())
            if (old.name == ArenaName || old.name == "Environment" || old.name == "Environment (1)" ||
                old.name == "Environment (2)" || old.name == "Checker room and luminous panels" ||
                old.name == "Combo floor extension")
                Undo.DestroyObjectImmediate(old);
        var root = new GameObject(ArenaName).transform;
        SceneManager.MoveGameObjectToScene(root.gameObject, scene);
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Build meme plaza");
        BuildCourt(root);
        BuildMarket(root);
        BuildDecor(root);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.25f, .27f, .31f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = .012f;
        RenderSettings.fogColor = new Color(.28f, .28f, .36f);
        RenderSettings.skybox = Sky();
        foreach (var camera in scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<Camera>(true)))
        {
            if (!camera.CompareTag("MainCamera")) continue;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.backgroundColor = RenderSettings.fogColor;
            camera.farClipPlane = Mathf.Max(80, camera.farClipPlane);
            EditorUtility.SetDirty(camera);
        }
    }

    public static void InspectScenes()
    {
        var report = new StringBuilder();
        foreach (string path in Scenes)
        {
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                report.AppendLine(path);
                foreach (var root in scene.GetRootGameObjects())
                    report.AppendLine(root.name + ": renderers=" + root.GetComponentsInChildren<Renderer>(true).Length +
                        "; scripts=" + string.Join(",", root.GetComponentsInChildren<MonoBehaviour>(true)
                            .Where(item => item).Select(item => item.GetType().Name).Distinct()));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        Directory.CreateDirectory(Review);
        File.WriteAllText(Review + "/Hierarchy.txt", report.ToString());
    }
}

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void InstallMemeArena() => MemeArenaSetup.Install();
        public static void InspectMemeArenaScenes() => MemeArenaSetup.InspectScenes();
        public static void ValidateMemeArena() => MemeArenaSetup.Validate();
        public static void CaptureMemeBattle()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            try
            {
                CaptureBattleLook(scene, MemeArenaSetup.Review + "/BattleMemePlaza.png");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
