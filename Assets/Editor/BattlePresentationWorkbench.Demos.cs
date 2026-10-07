using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class BattlePresentationWorkbench
{
    const string DemoKey = "BattleReview.DemosPlay";
    const string DemoDirectory = Review + "/DemosPlay";
    const BindingFlags DemoMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly string[] DemoPaths = {
        "Assets/ErbGameArt/Sword slash FX/Demo scene/Slash demo mesh-path(old).unity",
        "Assets/ErbGameArt/Sword slash FX/Demo scene/Slash demo new.unity",
        "Assets/JMO Assets/Cartoon FX (legacy)/Demo/CFX1 Demo.unity",
        "Assets/JMO Assets/Cartoon FX (legacy)/Demo/CFX2 Demo.unity",
        "Assets/JMO Assets/Cartoon FX (legacy)/Demo/CFX3 Demo.unity" };
    [Serializable] sealed class DemoRun
    {
        public string previous, copy, snapshot;
        public int index, phase, options;
        public bool optionsEnabled, paused, background, audioPaused;
        public float scale;
        public double began;
    }
    // Phases: 0 ready, 1 entering, 2 running, 3 exiting successfully, 4 cancelling.
    static DemoRun demoRun;
    static int demoAttempt, demoCaptureFrame, demoEditTicks;
    static double demoCaptureAt;
    static GameObject demoEffect;
    static string demoImage;
    static DemoRun DemoState
    {
        get
        {
            string json = SessionState.GetString(DemoKey, "");
            return demoRun ?? (demoRun = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<DemoRun>(json));
        }
    }
    static void SaveDemoState() => SessionState.SetString(DemoKey, JsonUtility.ToJson(demoRun));
    static void DemoReport(string value) => File.AppendAllText(DemoDirectory + "/Report.txt", value + "\n");

    [InitializeOnLoadMethod] static void HookDemoReview()
    {
        EditorApplication.update -= DemoTick;
        EditorApplication.update += DemoTick;
        EditorApplication.playModeStateChanged -= DemoPlayState;
        EditorApplication.playModeStateChanged += DemoPlayState;
        Application.logMessageReceived -= DemoLog;
        Application.logMessageReceived += DemoLog;
    }
    [MenuItem("Tools/Battle/Presentation/Run five demos in Play Mode")]
    public static void BeginDemoPlayReview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || DemoState != null)
            throw new InvalidOperationException("Start in Edit Mode with no demo review already running.");
        Directory.CreateDirectory(DemoDirectory);
        demoRun = new DemoRun {
            previous = AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene),
            copy = "Assets/Editor/BattleDemoReview_" + Guid.NewGuid().ToString("N") + ".unity",
            snapshot = DemoSnapshot(), options = (int)EditorSettings.enterPlayModeOptions,
            optionsEnabled = EditorSettings.enterPlayModeOptionsEnabled, paused = EditorApplication.isPaused,
            scale = Time.timeScale, background = Application.runInBackground, audioPaused = AudioListener.pause };
        SaveDemoState();
        try
        {
            File.WriteAllText(DemoDirectory + "/Report.txt", "RUNNING " + DateTime.UtcNow.ToString("O") + "\n" +
                "Isolated source copies; native controls invoked; no audio audition.\n" + demoRun.snapshot);
            // Unity preserves the loaded dirty scenes in its Play Mode backup; never close or save them.
            EditorSettings.enterPlayModeOptionsEnabled = false;
            PrepareDemo();
        }
        catch (Exception e) { FailDemo(e); }
    }
    static string DemoSnapshot()
    {
        var text = new StringBuilder();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            text.AppendLine($"{i}|{scene.path}|loaded={scene.isLoaded}|dirty={scene.isDirty}|" +
                $"active={scene == SceneManager.GetActiveScene()}");
            if (scene.isLoaded)
                foreach (var root in scene.GetRootGameObjects())
                    text.AppendLine($"  {root.transform.GetSiblingIndex()}|{root.name}|{root.activeSelf}");
        }
        return text.ToString();
    }
    static void PrepareDemo()
    {
        try
        {
            if (DemoState == null || demoRun.phase != 0 || EditorApplication.isPlayingOrWillChangePlaymode) return;
            demoEditTicks = 0;
            File.Copy(DemoPaths[demoRun.index], demoRun.copy, false);
            AssetDatabase.ImportAsset(demoRun.copy, ImportAssetOptions.ForceSynchronousImport);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(demoRun.copy);
            if (!EditorSceneManager.playModeStartScene) throw new Exception("Temporary scene import failed.");
            demoRun.phase = 1;
            demoRun.began = EditorApplication.timeSinceStartup;
            SaveDemoState();
            DemoReport("\nSCENE " + DemoPaths[demoRun.index]);
            EditorApplication.isPaused = false;
            EditorWindow.GetWindow(typeof(Editor).Assembly.GetType("UnityEditor.GameView")).Show();
            EditorApplication.isPlaying = true;
        }
        catch (Exception e) { FailDemo(e); }
    }
    static void DemoPlayState(PlayModeStateChange state)
    {
        if (DemoState == null) return;
        try
        {
            if (state == PlayModeStateChange.EnteredPlayMode && demoRun.phase == 1)
            {
                demoRun.phase = 2;
                demoRun.began = EditorApplication.timeSinceStartup;
                SaveDemoState();
                demoAttempt = 0;
                demoImage = null;
                if (SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().path != demoRun.copy)
                    throw new Exception("Isolation failed; refusing to operate on other loaded scenes.");
                var cameras = UnityEngine.Object.FindObjectsByType<Camera>();
                var camera = cameras.FirstOrDefault(c => c.enabled && c.CompareTag("MainCamera")) ??
                    cameras.FirstOrDefault(c => c.enabled);
                if (!camera) throw new Exception("No active authored demo camera.");
                foreach (var item in cameras) item.enabled = item == camera;
                foreach (var listener in UnityEngine.Object.FindObjectsByType<AudioListener>())
                    listener.enabled = listener.gameObject == camera.gameObject;
                if (!camera.GetComponent<AudioListener>()) camera.gameObject.AddComponent<AudioListener>();
                camera.GetComponent<AudioListener>().enabled = true;
                AudioListener.pause = true;
                Application.runInBackground = true;
                Time.timeScale = 1;
                DemoReport("Camera=" + camera.name + "; pipeline=" +
                    UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline);
            }
            // Edit Mode cleanup runs in DemoTick after scene restoration, never inside the transition callback.
        }
        catch (Exception e) { FailDemo(e); }
    }
    static void AdvanceDemo()
    {
        bool advance = demoRun.phase == 3;
        DemoReport("RESTORED scene=" + demoRun.index + "; advance=" + advance);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(demoRun.previous);
        AssetDatabase.DeleteAsset(demoRun.copy);
        Time.timeScale = demoRun.scale;
        if (advance && ++demoRun.index < DemoPaths.Length)
        {
            demoRun.phase = 0;
            demoRun.began = EditorApplication.timeSinceStartup;
            SaveDemoState();
        }
        else RestoreDemo(advance ? "COMPLETED; inspect images for visual compatibility." : "STOPPED/FAILED");
    }
    static void DemoTick()
    {
        if (DemoState == null || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            bool editing = !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode;
            if (editing && demoRun.phase == 0)
            {
                PrepareDemo();
                return;
            }
            if (editing && demoRun.phase >= 2 && DemoSnapshot() == demoRun.snapshot)
            {
                if (++demoEditTicks >= 2) AdvanceDemo();
                return;
            }
            demoEditTicks = 0;
            if (demoRun.phase == 1 && EditorApplication.isPlaying &&
                SceneManager.GetActiveScene().path == demoRun.copy)
                DemoPlayState(PlayModeStateChange.EnteredPlayMode);
            double age = EditorApplication.timeSinceStartup - demoRun.began;
            if (age > 60) throw new TimeoutException($"Demo transition phase={demoRun.phase}; " +
                $"scene={demoRun.index}; playing={EditorApplication.isPlaying}; editing={editing}");
            if (demoRun.phase != 2 || !EditorApplication.isPlaying) return;
            EditorApplication.isPaused = false;
            Time.timeScale = 1; // Only the isolated runtime scene; restored to the user's clock in Edit Mode.
            if (demoImage != null && Time.timeAsDouble >= demoCaptureAt && Time.frameCount > demoCaptureFrame)
            {
                DemoReport($"CAPTURE {demoImage}; active={demoEffect && demoEffect.activeInHierarchy}; " +
                    "liveParticles=" + (demoEffect ? demoEffect.GetComponentsInChildren<ParticleSystem>()
                    .Sum(p => p.particleCount) : 0));
                ScreenCapture.CaptureScreenshot(demoImage);
                demoImage = null;
            }
            if (demoAttempt < 2 && age >= .7 + demoAttempt * 2.1)
            {
                int attempt = demoAttempt++;
                try { TriggerDemo(attempt); }
                catch (Exception e)
                {
                    DemoReport("TRIGGER FAILED: " + e);
                    demoImage = DemoImagePath(attempt);
                    if (File.Exists(demoImage)) File.Delete(demoImage);
                    demoCaptureAt = Time.timeAsDouble + .18;
                    demoCaptureFrame = Time.frameCount;
                }
            }
            if (age < 5.3 || demoImage != null) return;
            for (int i = 0; i < 2; i++)
                DemoReport("Image " + i + " written=" + File.Exists(DemoImagePath(i)));
            demoRun.phase = 3;
            demoRun.began = EditorApplication.timeSinceStartup;
            SaveDemoState();
            DemoReport("EXIT requested scene=" + demoRun.index);
            EditorApplication.isPlaying = false;
        }
        catch (Exception e) { FailDemo(e); }
    }
    static string DemoImagePath(int attempt) => DemoDirectory + "/" + demoRun.index + "_" +
        Path.GetFileNameWithoutExtension(DemoPaths[demoRun.index]) + "_" + attempt + ".png";
    static object DemoField(MonoBehaviour owner, string name) =>
        owner.GetType().GetField(name, DemoMembers).GetValue(owner);
    static void TriggerDemo(int attempt)
    {
        float delay = 0;
        var scripts = UnityEngine.Object.FindObjectsByType<MonoBehaviour>();
        var selector = scripts.FirstOrDefault(s => s && s.GetType().Name == "DemoToonVFX");
        var cartoon = scripts.FirstOrDefault(s => s && s.GetType().Name == "CFX_Demo_New");
        if (selector)
        {
            selector.GetType().GetMethod("Counter", DemoMembers).Invoke(selector, new object[] { attempt });
            int index = (int)DemoField(selector, "Prefab");
            demoEffect = (GameObject)DemoField(selector, "Instance");
            delay = ((float[])DemoField(selector, "activationTime"))[index];
            DemoReport("TRIGGER Counter(" + attempt + "): " +
                AssetDatabase.GetAssetPath(((GameObject[])DemoField(selector, "Prefabs"))[index]));
        }
        else if (cartoon)
        {
            if (attempt > 0) cartoon.GetType().GetMethod("OnNextEffect", DemoMembers).Invoke(cartoon, null);
            int index = (int)DemoField(cartoon, "exampleIndex");
            var source = ((GameObject[])DemoField(cartoon, "ParticleExamples"))[index];
            DemoReport("TRIGGER native spawnParticle: " + source.name + "; prefab=" +
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source));
            demoEffect = (GameObject)cartoon.GetType().GetMethod("spawnParticle", DemoMembers).Invoke(cartoon, null);
        }
        else
        {
            // The old mesh-path scene is an authored particle gallery without a demo controller.
            var particles = UnityEngine.Object.FindObjectsByType<ParticleSystem>();
            if (particles.Length == 0) throw new Exception("No authored selector or live particle gallery found.");
            foreach (var particle in particles)
            {
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                particle.Play(false);
            }
            demoEffect = particles[0].transform.root.gameObject;
            DemoReport("TRIGGER authored gallery ParticleSystem.Play; systems=" + particles.Length + "; prefabs=" +
                string.Join(";", particles.Select(p => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(p))
                    .Distinct()));
        }
        foreach (var material in UnityEngine.Object.FindObjectsByType<Renderer>()
            .Concat(demoEffect ? demoEffect.GetComponentsInChildren<Renderer>(true) : new Renderer[0])
            .SelectMany(r => r.sharedMaterials).Where(m => m).Distinct())
        {
            var shader = material.shader;
            string tag = material.GetTag("RenderPipeline", false, "");
            string status = !shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader) ? "ERROR" :
                tag == "UniversalPipeline" ? "URP_TAGGED" :
                tag.Length > 0 ? "PIPELINE_MISMATCH" :
                shader.name == "Standard" || shader.name.StartsWith("Legacy Shaders/") ? "BUILTIN_INCOMPATIBLE_URP" :
                "UNTAGGED_LEGACY_OR_CUSTOM: URP compatibility unproven; inspect capture";
            DemoReport($"SHADER {material.name}: {shader?.name}; supported={shader && shader.isSupported}; " +
                $"compileErrors={(shader && ShaderUtil.ShaderHasError(shader))}; pipelineTag={tag}; {status}");
        }
        demoImage = DemoImagePath(attempt);
        if (File.Exists(demoImage)) File.Delete(demoImage);
        demoCaptureAt = Time.timeAsDouble + delay + .18;
        demoCaptureFrame = Time.frameCount;
    }
    static void DemoLog(string message, string stack, LogType type)
    {
        if (DemoState != null && (type == LogType.Error || type == LogType.Exception || type == LogType.Assert))
            DemoReport("RUNTIME " + type + ": " + message + "\n" + stack);
    }
    static void FailDemo(Exception error)
    {
        DemoReport("FAIL " + error);
        if (DemoState == null) return;
        demoRun.phase = 4;
        SaveDemoState();
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.isPlaying = false;
        else RestoreDemo("FAILED; restored Edit Mode settings.");
    }
    static void RestoreDemo(string result)
    {
        var state = DemoState;
        if (state == null) return;
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(state.previous);
        EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)state.options;
        EditorSettings.enterPlayModeOptionsEnabled = state.optionsEnabled;
        EditorApplication.isPaused = state.paused;
        Time.timeScale = state.scale;
        Application.runInBackground = state.background;
        AudioListener.pause = state.audioPaused;
        AssetDatabase.DeleteAsset(state.copy);
        SessionState.EraseString(DemoKey);
        demoRun = null;
        DemoReport(result + "\nFinal loaded scene/root state matches=" + (DemoSnapshot() == state.snapshot));
    }
}
