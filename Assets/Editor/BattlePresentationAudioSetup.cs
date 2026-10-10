using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

/// <summary>Configures the existing bank without opening or saving any scene.</summary>
public static class BattlePresentationAudioSetup
{
    public const string BankPath = "Assets/Audio/Battle/BattleSfxBank.asset";
    public const string MixerPath = "Assets/Audio/Battle/BattleMixer.mixer";

    [MenuItem("Tools/Battle/Presentation/Configure battle audio")]
    public static void Configure()
    {
        if (EditorApplication.isPlaying)
            throw new InvalidOperationException("Configure audio outside Play Mode.");
        AssetDatabase.ImportAsset(MixerPath, ImportAssetOptions.ForceSynchronousImport);
        var resolvedClips = BattleAudioClipImportRepair.Prepare(BankPath);
        var bank = AssetDatabase.LoadAssetAtPath<BattleSfxBank>(BankPath);
        var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
        if (!bank || !mixer) throw new InvalidOperationException("Battle bank or mixer could not be imported.");
        Undo.RecordObject(bank, "Configure battle audio");
        bank.mixer = mixer;
        mixer.updateMode = AudioMixerUpdateMode.UnscaledTime;
        var routes = mixer.FindMatchingGroups(string.Empty).ToDictionary(group => group.name);
        var groups = new List<BattleSfxBank.Group>(bank.groups);
        foreach (var group in groups)
        {
            if (!resolvedClips.TryGetValue(group.id, out var clips))
                throw new InvalidOperationException("Missing imported selection for " + group.id);
            group.clips = clips;
        }
        var weight = groups.Find(group => group.id == "heavy_weight");
        if (weight == null)
        {
            weight = new BattleSfxBank.Group { id = "heavy_weight" };
            groups.Add(weight);
        }
        // Reuse short, trimmed body transients already loaded for ground contact.
        var falls = bank.FindGroup("body_fall");
        weight.clips = falls.clips.ToArray();
        weight.clipGains = Enumerable.Repeat(1f, weight.clips.Length).ToArray();
        weight.volume = .18f;
        weight.pitch = new Vector2(.97f, 1.02f);
        weight.priority = 96;
        foreach (var group in groups)
        {
            group.output = routes[Route(group.id)];
            group.maxConcurrent = group.id == "heavy_weight" ? 2 : 3;
            group.gainVariationDb = group.id == "fight_start" || group.id == "victory" ? 0 : .65f;
            group.startOffsets = BattleAudioTimingSetup.Offsets(group);
            group.layers = group.id == "heavy_hit" ? new[] { "heavy_weight" } : Array.Empty<string>();

        }
        bank.groups = groups.ToArray();
        EditorUtility.SetDirty(bank);
        EditorUtility.SetDirty(mixer);
        // Never persist unresolved references. Imports and object rebinding have finished above.
        ValidateAssets();
        AssetDatabase.SaveAssets();
    }

    static string Route(string id)
    {
        if (id == "fight_start" || id == "victory" || id == "hurt_voice") return "Voices";
        if (id == "ui_click") return "UI";
        if (id.EndsWith("swing", StringComparison.Ordinal) || id == "getup" || id == "footstep") return "Weapons";
        return "Impacts";
    }

    [MenuItem("Tools/Battle/Presentation/Validate battle audio assets")]
    public static void ValidateAssets()
    {
        var bank = AssetDatabase.LoadAssetAtPath<BattleSfxBank>(BankPath);
        if (!bank || !bank.mixer) throw new InvalidOperationException("Battle mixer is not configured.");
        var report = new List<string>();
        var ids = new HashSet<string>();
        foreach (var group in bank.groups)
        {
            if (!ids.Add(group.id)) throw new InvalidOperationException("Duplicate cue group: " + group.id);
            if (!group.output || group.output.audioMixer != bank.mixer)
                throw new InvalidOperationException("Missing battle bus: " + group.id);
            if (group.clips.Length == 0 || group.clips.Any(clip => !clip))
                throw new InvalidOperationException("Missing clip: " + group.id);
            if (group.startOffsets.Length != group.clips.Length || group.clipGains.Length != group.clips.Length)
                throw new InvalidOperationException("Variant configuration mismatch: " + group.id);
            for (int i = 0; i < group.clips.Length; i++)
            {
                if (group.startOffsets[i] < 0 || group.startOffsets[i] >= group.clips[i].length)
                    throw new InvalidOperationException("Invalid start offset: " + group.id);
                var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(group.clips[i]));
                if (!importer.defaultSampleSettings.preloadAudioData)
                    throw new InvalidOperationException("Latency-sensitive cue is not preloaded: " + group.id);
            }
            foreach (string layer in group.layers)
                if (layer == group.id || bank.FindGroup(layer) == null)
                    throw new InvalidOperationException("Invalid cue layer: " + group.id + "/" + layer);
            report.Add($"PASS {group.id}: {group.clips.Length} variants, {group.output.name}, cap={group.maxConcurrent}");
        }
        report.Add("PASS existing bank is wired to the project-owned unscaled mixer.");
        report.Add("NOT CHECKED HERE: subjective audition, hardware latency, speaker/headphone mix, runtime concurrency.");
        Directory.CreateDirectory("GeneratedAssets/BattlePresentationReview");
        File.WriteAllLines("GeneratedAssets/BattlePresentationReview/AudioAssets.txt", report);
        Debug.Log("Battle audio asset validation passed. See GeneratedAssets/BattlePresentationReview/AudioAssets.txt");
    }
}
