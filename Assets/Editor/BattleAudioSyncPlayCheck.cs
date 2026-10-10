using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class BattleAudioSyncPlayCheck
{
    const string Key = "BattleAudioSyncPlayCheck";
    const string Copy = "Assets/Audio/Battle/Editor/BattleAudioSyncProbe.unity";
    static readonly StringBuilder report = new StringBuilder();
    static readonly float[] output = new float[256];
    static readonly MethodInfo play = typeof(BattleSfxPlayer).GetMethod("PlayGroup",
        BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly MethodInfo stop = typeof(BattleSfxPlayer).GetMethod("StopVoices",
        BindingFlags.Instance | BindingFlags.NonPublic);
    static BattleSfxPlayer audio;
    static BattleSfxBank original;
    static BattleSfxBank copy;
    static List<(int group, int clip)> variants;
    static int next;
    static int starts;
    static int outputFrames;
    static int pausedFrame;
    static int[] pausedSamples;
    static AudioSource[] pausedVoices;
    static bool initialized;
    static double began;

    static BattleAudioSyncPlayCheck()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += Restore;
    }

    [MenuItem("Tools/Battle/Audio/Check native AudioSource timing")]
    public static void Start()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(Copy))
            throw new InvalidOperationException("Audio timing probe needs Edit Mode and a fresh temporary scene.");
        var previous = SceneManager.GetActiveScene();
        Directory.CreateDirectory("Assets/Audio/Battle/Editor");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var root = new GameObject("Battle Audio Timing Probe");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.AddComponent<AudioListener>();
            var player = root.AddComponent<BattleSfxPlayer>();
            player.bank = AssetDatabase.LoadAssetAtPath<BattleSfxBank>(BattlePresentationAudioSetup.BankPath);
            player.enableAnnouncer = false;
            player.enableHurtVoices = false;
            player.enableUiSounds = false;
            player.reproducibleVariation = true;
            player.maxVoices = 24;
            EditorSceneManager.SaveScene(scene, Copy);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
            SceneManager.SetActiveScene(previous);
        }
        AssetDatabase.ImportAsset(Copy, ImportAssetOptions.ForceSynchronousImport);
        SessionState.SetString(Key + ".previous", AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Copy);
        SessionState.SetBool(Key, true);
        Directory.CreateDirectory(BattleAudioTimingSetup.ReportPath);
        File.WriteAllText(BattleAudioTimingSetup.ReportPath + "/PlayMode.txt", "RUNNING\n");
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying ||
            EditorApplication.isCompiling || EditorApplication.isPaused)
            return;
        try
        {
            if (!initialized)
            {
                audio = SceneManager.GetActiveScene().GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<BattleSfxPlayer>()).Single();
                original = audio.bank;
                copy = UnityEngine.Object.Instantiate(original);
                copy.groups = original.groups.ToArray();
                audio.bank = copy;
                audio.Prewarm();
                variants = new List<(int group, int clip)>();
                for (int groupIndex = 0; groupIndex < original.groups.Length; groupIndex++)
                    if (BattleAudioTimingSetup.ShouldAlign(original.groups[groupIndex].id))
                        for (int clip = 0; clip < original.groups[groupIndex].clips.Length; clip++)
                            variants.Add((groupIndex, clip));
                audio.CuePlayed += OnCue;
                report.Clear();
                next = starts = outputFrames = pausedFrame = 0;
                began = EditorApplication.timeSinceStartup;
                initialized = true;
            }
            if (EditorApplication.timeSinceStartup - began > 120)
                throw new InvalidOperationException("Audio timing Play Mode probe timed out.");
            ObserveOutput();
            if (next == variants.Count)
            {
                CheckPauseAndReset();
                return;
            }
            stop.Invoke(audio, null);
            var variant = variants[next++];
            var group = original.groups[variant.group];
            copy.groups = original.groups.ToArray();
            copy.groups[variant.group] = new BattleSfxBank.Group
            {
                id = group.id,
                clips = new[] { group.clips[variant.clip] },
                startOffsets = new[] { group.startOffsets[variant.clip] },
                clipGains = new[] { group.clipGains != null && variant.clip < group.clipGains.Length
                    ? group.clipGains[variant.clip] : 1f },
                volume = group.volume,
                pitch = group.pitch,
                output = group.output,
                priority = group.priority,
                maxConcurrent = group.maxConcurrent,
                layers = group.layers,
                gainVariationDb = group.gainVariationDb
            };
            play.Invoke(audio, new object[] { group.id, false, true });
        }
        catch (Exception error)
        {
            Finish(false, error.ToString());
        }
    }

    static void OnCue(string id, AudioClip clip)
    {
        var group = copy.FindGroup(id);
        int index = Array.IndexOf(group.clips, clip);
        int expected = Mathf.RoundToInt(group.startOffsets[index] * clip.frequency);
        var source = audio.GetComponentsInChildren<AudioSource>()
            .Where(source => source.clip == clip && source.isPlaying)
            .OrderBy(source => Mathf.Abs(source.timeSamples - expected)).FirstOrDefault();
        if (!source || source.volume <= 0 || source.outputAudioMixerGroup != group.output)
            throw new InvalidOperationException("Sound did not start an audible routed voice: " + id);
        // Allow one small DSP buffer between source.Play and this synchronous callback.
        if (Mathf.Abs(source.timeSamples - expected) > clip.frequency * .020f)
            throw new InvalidOperationException("AudioSource lost its calibrated start sample: " + id);
        report.AppendLine($"PASS {id}/{clip.name}: sample={source.timeSamples}, expected={expected}, " +
            $"pitch={source.pitch:F3}, audible routed AudioSource.");
        starts++;
    }

    static void ObserveOutput()
    {
        foreach (var source in audio.GetComponentsInChildren<AudioSource>())
        {
            if (!source.isPlaying)
                continue;
            source.GetOutputData(output, 0);
            if (output.Any(sample => Mathf.Abs(sample) > .00001f))
            {
                outputFrames++;
                return;
            }
        }
    }

    static void CheckPauseAndReset()
    {
        if (pausedFrame == 0)
        {
            audio.SetMenuPaused(true);
            pausedVoices = audio.GetComponentsInChildren<AudioSource>().Where(source => source.clip).ToArray();
            pausedSamples = pausedVoices.Select(source => source.timeSamples).ToArray();
            pausedFrame++;
            return;
        }
        if (!pausedVoices.Select(source => source.timeSamples).SequenceEqual(pausedSamples))
            throw new InvalidOperationException("Paused sound samples advanced.");
        if (++pausedFrame < 4)
            return;
        audio.SetMenuPaused(false);
        audio.ResetForMatch();
        audio.ResetForMatch();
        if (audio.ActiveVoiceCount != 0 || audio.GetComponentsInChildren<AudioSource>().Any(source => source.clip))
            throw new InvalidOperationException("Reset left an active audio voice.");
        if (outputFrames == 0 || audio.DroppedCueCount != 0)
            throw new InvalidOperationException("Missing nonzero audio output or a dropped voice.");
        Finish(true, $"{variants.Count} variants; {starts} native AudioSource starts including layers; " +
            $"{outputFrames} frames with nonzero output. Calibrated samples, mixer routing, pause and reset passed.");
    }

    static void Finish(bool success, string message)
    {
        if (audio)
            audio.CuePlayed -= OnCue;
        if (copy)
            UnityEngine.Object.Destroy(copy);
        report.AppendLine((success ? "PASS " : "FAIL ") + message);
        File.WriteAllText(BattleAudioTimingSetup.ReportPath + "/PlayMode.txt", report.ToString());
        EditorApplication.isPlaying = false;
    }

    static void Restore(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key, false))
            return;
        SessionState.SetBool(Key, false);
        string previous = SessionState.GetString(Key + ".previous", "");
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null :
            AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        AssetDatabase.DeleteAsset(Copy);
        initialized = false;
    }
}
