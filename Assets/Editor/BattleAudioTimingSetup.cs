using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class BattleAudioTimingSetup
{
    public const string ReportPath = "GeneratedAssets/BattleAudioSyncReview";
    const float WindowSeconds = .002f;
    const float AttackThreshold = .3f;
    const float LeadSeconds = .005f;

    [MenuItem("Tools/Battle/Audio/Align sound attacks to presentation cues")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Align audio in Edit Mode.");
        var bank = AssetDatabase.LoadAssetAtPath<BattleSfxBank>(BattlePresentationAudioSetup.BankPath);
        if (!bank)
            throw new InvalidOperationException("Missing battle audio bank.");
        Directory.CreateDirectory(ReportPath);
        var report = new StringBuilder("group,clip,onsetSeconds,startOffsetSeconds,remainingOnsetSeconds\n");
        Undo.RecordObject(bank, "Align battle sound transients");
        foreach (var group in bank.groups)
        {
            if (!ShouldAlign(group.id))
                continue;
            group.startOffsets = Offsets(group);
            for (int i = 0; i < group.clips.Length; i++)
            {
                float onset = AttackOnset(group.clips[i]);
                report.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:R},{3:R},{4:R}",
                    group.id, AssetDatabase.GetAssetPath(group.clips[i]), onset, group.startOffsets[i],
                    Mathf.Max(0, onset - group.startOffsets[i])));
            }
        }
        EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssetIfDirty(bank);
        File.WriteAllText(ReportPath + "/ClipTiming.csv", report.ToString());
    }

    public static bool ShouldAlign(string id) => id.EndsWith("swing", StringComparison.Ordinal) ||
        id.EndsWith("hit", StringComparison.Ordinal) || id.EndsWith("fall", StringComparison.Ordinal) ||
        id.StartsWith("grapple_", StringComparison.Ordinal) || id == "magic_swipe" || id == "blade_cut" ||
        id == "metal_clash" || id == "shield_block" || id == "heavy_weight" || id == "blade_body" ||
        id == "gun_shot" || id == "footstep";

    // Keep the existing source WAVs and gains. Only skip a quiet lead-in at a near-zero sample.
    public static float[] Offsets(BattleSfxBank.Group group)
    {
        var offsets = new float[group.clips.Length];
        if (!ShouldAlign(group.id))
            return offsets;
        for (int i = 0; i < group.clips.Length; i++)
        {
            var clip = group.clips[i];
            var samples = Read(clip);
            float onset = AttackOnset(clip, samples);
            int wanted = Mathf.Max(0, Mathf.FloorToInt((onset - LeadSeconds) * clip.frequency));
            if (wanted == 0)
                continue;
            int radius = Mathf.Max(1, Mathf.RoundToInt(.002f * clip.frequency));
            int best = wanted;
            float lowest = float.PositiveInfinity;
            for (int frame = Mathf.Max(0, wanted - radius);
                frame <= Mathf.Min(clip.samples - 1, wanted + radius); frame++)
            {
                float level = 0;
                for (int channel = 0; channel < clip.channels; channel++)
                    level += Mathf.Abs(samples[frame * clip.channels + channel]);
                if (level >= lowest)
                    continue;
                lowest = level;
                best = frame;
            }
            offsets[i] = (float)best / clip.frequency;
        }
        return offsets;
    }

    public static float AttackOnset(AudioClip clip) => AttackOnset(clip, Read(clip));

    static float AttackOnset(AudioClip clip, float[] samples)
    {
        int window = Mathf.Max(1, Mathf.RoundToInt(WindowSeconds * clip.frequency)) * clip.channels;
        var energy = new double[(samples.Length + window - 1) / window];
        double peak = 0;
        for (int index = 0; index < energy.Length; index++)
        {
            int start = index * window;
            int end = Mathf.Min(samples.Length, start + window);
            double sum = 0;
            for (int sample = start; sample < end; sample++)
                sum += (double)samples[sample] * samples[sample];
            energy[index] = sum / (end - start);
            peak = Math.Max(peak, energy[index]);
        }
        if (peak <= 0)
            throw new InvalidOperationException("Silent battle audio: " + clip.name);
        for (int index = 0; index < energy.Length; index++)
            if (energy[index] >= peak * AttackThreshold * AttackThreshold)
                return (float)(index * window) / clip.channels / clip.frequency;
        throw new InvalidOperationException("No sound attack found: " + clip.name);
    }

    static float[] Read(AudioClip clip)
    {
        if (!clip || clip.samples <= 0)
            throw new InvalidOperationException("Invalid battle audio clip.");
        if (clip.loadState != AudioDataLoadState.Loaded)
            clip.LoadAudioData();
        var samples = new float[clip.samples * clip.channels];
        if (!clip.GetData(samples, 0))
            throw new InvalidOperationException("Cannot measure battle audio: " + clip.name);
        return samples;
    }
}
