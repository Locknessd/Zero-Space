using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FrankRetarget;

/// <summary>Local Editor review commands. No runtime or network endpoint.</summary>
[InitializeOnLoad]
public static partial class BattlePresentationWorkbench
{
    public const string Review = "GeneratedAssets/EditorPolishReview";
    const string RequestPath = "Library/BattlePresentationRequest.json";
    const string ResponsePath = "Library/BattlePresentationResponse.txt";
    const string BattlePath = "Assets/Scenes/BattleScene.unity";
    [Serializable]
    public sealed class Request
    {
        public string action, value;
        public float time;
        public int side;
    }

    static double nextPoll;
    static string capturePath;
    static int captureAfter;
    static double captureEarliest;

    static BattlePresentationWorkbench()
    {
        EditorApplication.update += Tick;
        EditorApplication.update += ReviewTick;
        Application.logMessageReceived += Log;
    }

    static void Log(string text, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)
            return;
        Directory.CreateDirectory(Review);
        File.AppendAllText(Review + "/ConsoleErrors.txt",
            DateTime.UtcNow.ToString("O") + " " + type + " " + text + "\n" + stack + "\n");
    }

    static void Tick()
    {
        if (capturePath != null && Time.frameCount >= captureAfter && EditorApplication.isPlaying &&
            EditorApplication.timeSinceStartup >= captureEarliest)
        {
            ScreenCapture.CaptureScreenshot(capturePath);
            capturePath = null;
            captureEarliest = 0;
        }

        if (EditorApplication.timeSinceStartup < nextPoll ||
            EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        nextPoll = EditorApplication.timeSinceStartup + .2;
        if (!File.Exists(RequestPath))
            return;
        var json = File.ReadAllText(RequestPath);
        File.Delete(RequestPath);
        try
        {
            File.WriteAllText(ResponsePath, Dispatch(JsonUtility.FromJson<Request>(json)));
        }

        catch (Exception e)
        {
            File.WriteAllText(ResponsePath, "FAIL " + e);
            Debug.LogException(e);
        }

    }
    static string Dispatch(Request r)
    {
        Directory.CreateDirectory(Review);
        switch (r.action)
        {
            case "status":
                return Status();
            case "battle":
                OpenBattle();
                return Status();
            case "play":
                EditorApplication.isPlaying = true;
                return "Entering Play Mode";
            case "stop":
                EditorApplication.isPlaying = false;
                return "Exiting Play Mode";
            case "capture":
                capturePath = Review + "/" + Path.GetFileName(r.value) + ".png";
                captureAfter = Time.frameCount + 3;
                return "Game view capture queued: " + capturePath;
            case "replay":
                var game = Battle();
                var fighter = r.side == 0 ? game.leftCombat : game.rightCombat;
                var move = fighter.lightCombatMoves.Concat(fighter.heavyCombatMoves).Single(m => m.moveName == r.value);
                return "Playback accepted: " + game.PlayAnimationTest(
                    r.side == 0 ? PlayerUI.Side.Left : PlayerUI.Side.Right, move, r.time > 0);
            case "seek":
                var pair = Battle().leftCombat.SourcePlayback ?? Battle().rightCombat.SourcePlayback;
                if (!pair || !pair.Playing)
                    throw new Exception("Replay a move before seeking.");
                pair.AdvanceTo(r.time);
                EditorApplication.isPaused = true;
                return "Paused on contact " + pair.SampleTime;
            case "resume":
                EditorApplication.isPaused = false;
                return "Resumed";
            case "invoke":
                var split = r.value.LastIndexOf('.');
                var typeName = r.value.Substring(0, split);
                if (!typeName.StartsWith("FrankRetarget.Editor.", StringComparison.Ordinal) &&
                    typeName != "BattlePresentationWorkbench" && typeName != "BattleUrpPolishSetup" &&
                    typeName != "BattlePresentationAudioSetup" && typeName != "BattlePresentationContactSetup")
                    throw new Exception("Only project battle review operations are supported.");
                var type = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType(typeName)).First(t => t != null);
                type.GetMethod(r.value.Substring(split + 1), BindingFlags.Static | BindingFlags.Public)
                    .Invoke(null, null);
                return "Completed " + r.value;
            case "refresh":
                AssetDatabase.Refresh();
                return "Refreshed";
            default:
                throw new Exception("Unknown local review action " + r.action);
        }

    }
    public static GameManager Battle() => SceneManager.GetSceneByPath(BattlePath).GetRootGameObjects()
        .SelectMany(g => g.GetComponentsInChildren<GameManager>(true)).Single();

    [MenuItem("Tools/Battle/Presentation/Open battle safely")]
    public static void OpenBattle()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new Exception("Exit Play Mode first.");
        Directory.CreateDirectory(Review + "/UnsavedScenes");
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s.isDirty)
            {
                var copy = Review + "/UnsavedScenes/" + s.name + "_" +
                    DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity";
                if (!EditorSceneManager.SaveScene(s, copy, true))
                    throw new Exception("Could not preserve unsaved scene " + s.name);
            }

        }

        var scene = SceneManager.GetSceneByPath(BattlePath);
        if (!scene.IsValid() || !scene.isLoaded)
            scene = EditorSceneManager.OpenScene(BattlePath, OpenSceneMode.Additive);
        // Preserve other scenes on disk before isolating their roots for the review.
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            if (s == scene)
                continue;
            foreach (var root in s.GetRootGameObjects())
            {
                if (!root.activeSelf)
                    continue;
                string key = "BattleReview.Isolated." + GlobalObjectId.GetGlobalObjectIdSlow(root);
                SessionState.SetBool(key, true);
                root.SetActive(false);
            }

        }

        SceneManager.SetActiveScene(scene);
        var view = EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView"));
        view.Show();
        view.Focus();
        view.maximized = true;
    }

    [MenuItem("Tools/Battle/Presentation/Restore isolated scene roots")]
    public static void RestoreOtherScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new Exception("Exit Play Mode first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            foreach (var root in SceneManager.GetSceneAt(i).GetRootGameObjects())
            {
                string key = "BattleReview.Isolated." + GlobalObjectId.GetGlobalObjectIdSlow(root);
                if (!SessionState.GetBool(key, false))
                    continue;
                root.SetActive(true);
                SessionState.EraseBool(key);
            }

        }

    }
    public static string Status()
    {
        var b = new StringBuilder();
        b.AppendLine($"Unity={Application.unityVersion}; Playing={Application.isPlaying}; " +
            $"paused={EditorApplication.isPaused}; compiling={EditorApplication.isCompiling}");
        b.AppendLine($"GPU={SystemInfo.graphicsDeviceName}; API={SystemInfo.graphicsDeviceType}; " +
            $"CPU={SystemInfo.processorType}; RAM={SystemInfo.systemMemorySize} MB");
        b.AppendLine($"Pipeline={UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline}; " +
            $"Quality={QualitySettings.names[QualitySettings.GetQualityLevel()]}; " +
            $"VSync={QualitySettings.vSyncCount}; " +
            $"frameCap={Application.targetFrameRate}; resolution={Screen.width}x{Screen.height}; " +
            $"focus={Application.isFocused}; scale={Time.timeScale}; fixedStep={Time.fixedDeltaTime}");
        b.AppendLine($"EnterPlayModeOptions={EditorSettings.enterPlayModeOptionsEnabled}/" +
            $"{EditorSettings.enterPlayModeOptions}");
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var s = SceneManager.GetSceneAt(i);
            b.AppendLine($"Scene={s.path}; dirty={s.isDirty}; roots={s.rootCount}");
        }

        foreach (var g in UnityEngine.Object.FindObjectsByType<GameManager>(FindObjectsSortMode.None))
            b.AppendLine($"Battle={g.name}; queue={g.IsEventQueueBusy}; error={g.QueueError}; " +
                $"test={g.IsAnimationTestPlaying}; moves=" + string.Join(",",
                    g.leftCombat.lightCombatMoves.Concat(g.leftCombat.heavyCombatMoves).Select(m => m.moveName)));
        return b.ToString();
    }
}
