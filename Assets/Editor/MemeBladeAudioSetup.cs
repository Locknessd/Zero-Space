using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using ZeroSpace.Showcase;

public static class MemeBladeAudioSetup
{
    const string Clips = "Assets/Audio/Battle/MemeSlashes";
    const string Bank = "Assets/Audio/Battle/BattleSfxBank.asset";
    public static void Apply()
    {
        AssetDatabase.Refresh();
        var swings = Enumerable.Range(1, 4).Select(i => Load("Slash_Power_" + i.ToString("00"))).ToArray();
        var hits = Enumerable.Range(1, 2).Select(i => Load("Cut_Impact_" + i.ToString("00"))).ToArray();
        var bank = AssetDatabase.LoadAssetAtPath<BattleSfxBank>(Bank);
        Undo.RecordObject(bank, "Use more powerful sword sounds");
        foreach (var group in bank.groups)
        {
            bool swing = new[] { "steel_swing", "heavy_blade_swing", "axe_swing", "dagger_swing",
                "spear_swing", "ice_dagger_swing", "blade_swing" }.Contains(group.id);
            bool bladeHit = new[] { "steel_light_hit", "steel_heavy_hit", "katana_heavy_hit", "dagger_hit" }
                .Contains(group.id);
            if (!swing && !bladeHit) continue;
            bool heavy = group.id.Contains("heavy") || group.id.Contains("axe");
            group.clips = swing ? heavy ? swings.Skip(2).ToArray() : swings : hits;
            group.clipGains = Enumerable.Repeat(1f, group.clips.Length).ToArray();
            group.startOffsets = new float[group.clips.Length];
            group.volume = swing ? heavy ? .78f : .68f : heavy ? .76f : .65f;
            group.pitch = swing ? heavy ? new Vector2(.9f, .98f) : new Vector2(.97f, 1.06f) :
                new Vector2(.95f, 1.02f);
            group.maxConcurrent = 4;
        }
        EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssetIfDirty(bank);
        var library = AssetDatabase.LoadAssetAtPath<PepeShowcaseLibrary>("Assets/PepeShowcase/BomPepeTrailerLibrary.asset");
        ApplyToShowcase(library, swings);
        File.WriteAllText(MemeArenaSetup.Review + "/Audio.txt",
            "Four layered slashes: sword/knife air, low whoosh and bright metallic edge; two weighted cut impacts.\n" +
            "Native timing, contact routing, gun cues and mixer buses retained; mono PCM/preloaded 48 kHz.\n");
    }

    public static void ApplyToShowcase(PepeShowcaseLibrary library)
    {
        var swings = Enumerable.Range(1, 4).Select(i =>
            AssetDatabase.LoadAssetAtPath<AudioClip>(Clips + "/Slash_Power_" + i.ToString("00") + ".wav")).ToArray();
        if (swings.Any(clip => !clip)) return;
        ApplyToShowcase(library, swings);
    }

    static void ApplyToShowcase(PepeShowcaseLibrary library, AudioClip[] swings)
    {
        if (!library) throw new InvalidOperationException("The BOM showcase library is missing.");
        int variant = 0;
        foreach (var take in library.takes)
        {
            var slashCues = take.cues.Where(cue => cue.swing).ToArray();
            foreach (var cue in slashCues)
            {
                bool paired = slashCues.Any(other => other != cue && Mathf.Abs(other.seconds - cue.seconds) < .06f);
                bool finisher = cue == slashCues.Last();
                cue.sounds = new[] { new PepeShowcaseLibrary.Sound
                {
                    clip = swings[finisher ? 2 + variant % 2 : variant % swings.Length],
                    offset = 0,
                    volume = paired ? .47f : finisher ? .69f : .61f,
                    pitch = finisher ? .94f : .98f + variant % 3 * .025f
                } };
                variant++;
            }
        }
        EditorUtility.SetDirty(library);
        AssetDatabase.SaveAssetIfDirty(library);
    }

    static AudioClip Load(string name)
    {
        string path = Clips + "/" + name + ".wav";
        if (AssetImporter.GetAtPath(path) is not AudioImporter importer)
            throw new IOException("Generate the new slash WAV assets first: " + path);
        var settings = importer.defaultSampleSettings;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.compressionFormat = AudioCompressionFormat.PCM;
        settings.preloadAudioData = true;
        importer.forceToMono = true;
        importer.loadInBackground = false;
        importer.defaultSampleSettings = settings;
        importer.SaveAndReimport();
        var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (!clip || clip.samples == 0 || clip.channels != 1)
            throw new IOException("Invalid generated slash: " + path);
        return clip;
    }
}
