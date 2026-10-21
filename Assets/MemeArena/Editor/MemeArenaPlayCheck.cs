using System;
using System.IO;
using System.Linq;
using System.Text;
using MemeArena;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MemeArenaEditor
{
    [InitializeOnLoad]
    public static partial class MemeArenaPlayCheck
    {
        const string Key = "MemeArena.IntegrationCheck";
        const string Review = "Assets/MemeArena/Documentation/Review";
        static readonly StringBuilder report = new StringBuilder();
        static MemeArenaStage stage;
        static MemeArenaCombatAdapter adapter;
        static MemeArenaPlayProbe probe;
        static GameManager game;
        static Camera camera;
        static RenderTexture target;
        static bool previousBackground;
        static int phase;
        static float began, phaseBegan;
        static int errorCount;
        static CombatTripletData combo;
        static string capture;
        static bool savedIdle, savedCombo, savedKo;

        static MemeArenaPlayCheck()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Changed;
        }

        [MenuItem("Tools/Meme Arena/Validate actual battle Play mode")]
        public static void Begin() => BeginScene(MemeArenaIntegration.LatestScene);

        public static void BeginProduction() => BeginScene(MemeArenaProduction.BattlePath);

        static void BeginScene(string scenePath)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SessionState.GetBool(Key, false))
                throw new InvalidOperationException("Start the arena check from Edit mode.");
            Directory.CreateDirectory(Review);
            var dirtyScenes = new System.Collections.Generic.List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (scene.isDirty)
                {
                    dirtyScenes.Add(scene.path);
                    EditorSceneManager.SaveScene(scene, AssetDatabase.GenerateUniqueAssetPath(
                        Review + "/Unsaved_" + scene.name + ".unity"), true);
                }
            }
            SessionState.SetString(Key + ".dirtyScenes", string.Join("\n", dirtyScenes));
            SessionState.SetString(Key + ".previous", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".entered", false);
            SessionState.SetString(Key + ".scene", scenePath);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath);
            File.WriteAllText(Review + "/PlayValidation.txt", "RUNNING actual battle, crowd, hit/combo/KO and editor profiling.\n");
            EditorApplication.isPlaying = true;
        }

        static void Changed(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Key, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                phase = 0;
                SessionState.SetBool(Key + ".entered", true);
                began = (float)EditorApplication.timeSinceStartup;
                report.Clear();
                report.AppendLine("Scene under test: " + SceneManager.GetActiveScene().path);
                errorCount = 0;
                savedIdle = savedCombo = savedKo = false;
                Application.logMessageReceived += OnLog;
            }
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Application.logMessageReceived -= OnLog;
                EndFloorCheck();
                if (camera) camera.targetTexture = null;
                if (target) Object.DestroyImmediate(target);
                target = null;
                Application.runInBackground = previousBackground;
            }
            if (state == PlayModeStateChange.EnteredEditMode)
            {
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(
                    SessionState.GetString(Key + ".previous", ""));
                SessionState.SetBool(Key, false);
                foreach (var path in SessionState.GetString(Key + ".dirtyScenes", "").Split('\n'))
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    var scene = SceneManager.GetSceneByPath(path);
                    if (scene.IsValid() && scene.isLoaded) EditorSceneManager.MarkSceneDirty(scene);
                }
                stage = null;
            }
        }

        static void OnLog(string message, string trace, LogType type)
        {
            if ((type == LogType.Exception || type == LogType.Error) &&
                (trace.Contains("MemeArena") || message.Contains("NullReference") || message.Contains("Shader error")))
            {
                errorCount++;
                report.AppendLine("ERROR " + message);
            }
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !SessionState.GetBool(Key + ".entered", false) ||
                !EditorApplication.isPlaying || EditorApplication.isPaused) return;
            try
            {
                if (EditorApplication.timeSinceStartup - began > 240) throw new TimeoutException("Arena Play check timed out.");
                if (phase == 0)
                {
                    stage = Object.FindAnyObjectByType<MemeArenaStage>();
                    if (!stage) return;
                    adapter = stage.GetComponent<MemeArenaCombatAdapter>();
                    if (!adapter.IsBound) return;
                    game = adapter.game;
                    if (!FloorReady()) return;
                    camera = adapter.crowd.view;
                    previousBackground = Application.runInBackground;
                    Application.runInBackground = true;
                    target = new RenderTexture(1280, 720, 24);
                    target.Create();
                    camera.targetTexture = target;
                    probe = stage.gameObject.AddComponent<MemeArenaPlayProbe>();
                    probe.AfterFrame = AfterFrame;
                    BeginFloorCheck();
                    var moves = game.leftCombat.heavyCombatMoves.Concat(game.leftCombat.lightCombatMoves)
                        .Where(move => move != null && move.IsValid && move.sourcePair != null).ToArray();
                    foreach (var move in moves)
                        report.AppendLine("Available move: " + move.moveName + "; contact cues=" + ContactCues(move));
                    combo = moves.OrderByDescending(ContactCues).First();
                    game.roundManager?.StartTurnTimer(2, DateTime.UtcNow.ToString("o"),
                        DateTime.UtcNow.AddSeconds(60).ToString("o"));
                    report.AppendLine("PASS original GameManager, camera and two fighters initialized; adapter subscribed.");
                    EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
                    Next(1);
                    return;
                }
                Advance();
            }
            catch (Exception error)
            {
                report.AppendLine("FAIL " + error);
                Finish();
            }
        }

        static void Next(int value)
        {
            phase = value;
            phaseBegan = Time.unscaledTime;
        }

        static int ContactCues(CombatTripletData move)
        {
            var profile = game.battleVfx.timeline.FindMove(move);
            return profile == null ? 0 : profile.cues.Count(cue => cue.group == "light_hit" ||
                cue.group == "heavy_hit" || cue.group == "stab_hit");
        }

        static void AfterFrame()
        {
            if (string.IsNullOrEmpty(capture) || !target) return;
            var previous = RenderTexture.active;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                string prefix = SessionState.GetString(Key + ".scene", "") == MemeArenaProduction.BattlePath ?
                    "Production" : "";
                File.WriteAllBytes(Review + "/" + prefix + capture + ".png", image.EncodeToPNG());
                capture = null;
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(image);
            }
        }

        static void Finish()
        {
            File.WriteAllText(Review + "/PlayValidation.txt", report.ToString());
            if (SessionState.GetString(Key + ".scene", "") == MemeArenaProduction.BattlePath)
                File.WriteAllText(Review + "/ProductionPlayValidation.txt", report.ToString());
            phase = -1;
            EditorApplication.isPlaying = false;
        }
    }
}
