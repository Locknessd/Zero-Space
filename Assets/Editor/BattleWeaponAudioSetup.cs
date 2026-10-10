using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class BattleWeaponAudioSetup
{
    const string BankPath = "Assets/Audio/Battle/BattleSfxBank.asset";
    const string ManifestPath = "Assets/Audio/Battle/WeaponSoundSelections.json";
    internal const string ReportPath = "GeneratedAssets/WeaponAudioReview";

    [Serializable]
    sealed class Manifest
    {
        public Selection[] groups;
    }

    [Serializable]
    sealed class Selection
    {
        public string id;
        public string[] clips;
        public float volume;
        public float pitchLow;
        public float pitchHigh;
        public string bus;
        public int priority;
        public string[] layers;
        public int maxConcurrent;
    }

    [MenuItem("Tools/Battle/Audio/Apply reviewed weapon sound packs")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Apply weapon audio in Edit Mode.");
        var bank = AssetDatabase.LoadAssetAtPath<BattleSfxBank>(BankPath);
        var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
        var routes = bank.mixer.FindMatchingGroups(string.Empty).ToDictionary(g => g.name);
        var resolved = new Dictionary<string, AudioClip>();
        foreach (string path in manifest.groups.SelectMany(g => g.clips).Distinct())
        {
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (!importer)
                throw new InvalidOperationException("Missing native audio importer: " + path);
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.loadInBackground = false;
            importer.SaveAndReimport();
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (!clip || clip.samples == 0 || clip.channels != 1)
                throw new InvalidOperationException("Invalid reviewed weapon clip: " + path);
            resolved.Add(path, clip);
        }
        Undo.RecordObject(bank, "Apply reviewed sword sound packs");
        var groups = bank.groups.ToList();
        foreach (var selection in manifest.groups)
        {
            var group = groups.Find(g => g.id == selection.id);
            if (group == null)
            {
                group = new BattleSfxBank.Group { id = selection.id };
                groups.Add(group);
            }
            group.clips = selection.clips.Select(p => resolved[p]).ToArray();
            group.clipGains = Enumerable.Repeat(1f, group.clips.Length).ToArray();
            group.startOffsets = BattleAudioTimingSetup.Offsets(group);
            group.volume = selection.volume;
            group.pitch = new Vector2(selection.pitchLow, selection.pitchHigh);
            group.output = routes[selection.bus];
            group.priority = selection.priority;
            group.layers = selection.layers;
            group.maxConcurrent = selection.maxConcurrent;
            group.gainVariationDb = .45f;
        }
        var body = groups.Find(g => g.id == "blade_body");
        if (body == null)
        {
            body = new BattleSfxBank.Group { id = "blade_body" };
            groups.Add(body);
        }
        body.clips = bank.FindGroup("light_hit").clips.ToArray();
        body.clipGains = bank.FindGroup("light_hit").clipGains.ToArray();
        body.startOffsets = bank.FindGroup("light_hit").startOffsets.ToArray();
        body.volume = .22f;
        body.output = routes["Impacts"];
        body.pitch = new Vector2(.97f, 1.03f);
        body.priority = 96;
        body.maxConcurrent = 3;
        body.layers = Array.Empty<string>();
        bank.groups = groups.ToArray();
        ConfigureWeapons(bank);
        EditorUtility.SetDirty(bank);
        AssetDatabase.SaveAssetIfDirty(bank);
        ValidateBindings();
    }

    static void ConfigureWeapons(BattleSfxBank bank)
    {
        var sounds = new List<BattleSfxBank.WeaponSoundSet>();
        foreach (TrumpWeaponManager.WeaponType weapon in Enum.GetValues(typeof(TrumpWeaponManager.WeaponType)))
        {
            if (weapon == TrumpWeaponManager.WeaponType.None)
                continue;
            var entry = new BattleSfxBank.WeaponSoundSet
            {
                weapon = weapon,
                swing = "steel_swing",
                heavySwing = "heavy_blade_swing",
                thrustSwing = "dagger_swing",
                lightHit = "steel_light_hit",
                heavyHit = "katana_heavy_hit",
                stabHit = "blade_stab_hit"
            };
            if (weapon == TrumpWeaponManager.WeaponType.GreatSword)
            {
                entry.swing = entry.heavySwing = "heavy_blade_swing";
                entry.heavyHit = "steel_heavy_hit";
            }
            if (weapon == TrumpWeaponManager.WeaponType.TwoHandedAxe)
            {
                entry.swing = entry.heavySwing = "axe_swing";
                entry.heavyHit = "steel_heavy_hit";
            }
            if (weapon == TrumpWeaponManager.WeaponType.Spear)
                entry.swing = entry.heavySwing = entry.thrustSwing = "spear_swing";
            if (weapon == TrumpWeaponManager.WeaponType.Katana)
                entry.heavySwing = "steel_swing";
            if (weapon == TrumpWeaponManager.WeaponType.DualDaggers ||
                weapon == TrumpWeaponManager.WeaponType.Assassin)
            {
                entry.swing = entry.heavySwing = entry.thrustSwing = "ice_dagger_swing";
                entry.lightHit = entry.heavyHit = "dagger_hit";
            }
            sounds.Add(entry);
        }
        bank.weaponSounds = sounds.ToArray();
        bank.shieldSwingGroup = "heavy_blade_swing";
        bank.shieldHitGroup = "shield_bash_hit";
    }

    public static void ValidateBindings()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
        try
        {
            var game = scene.GetRootGameObjects()
                .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
            var bank = game.battleSfx.bank;
            if (!bank || !bank.mixer || !game.battleSfx.isActiveAndEnabled || game.battleSfx.masterVolume <= 0 ||
                game.battleVfx.timeline != bank ||
                game.leftCombat.battleSfx != game.battleSfx || game.rightCombat.battleSfx != game.battleSfx)
                throw new InvalidOperationException("BattleScene audio bindings do not share the installed bank.");
            var report = new List<string>();
            if (bank.weaponSounds.Length != 8 || bank.weaponSounds.Select(s => s.weapon).Distinct().Count() != 8)
                throw new InvalidOperationException("Missing reviewed weapon sound sets.");
            foreach (var weapon in bank.weaponSounds)
            {
                foreach (string id in new[] { weapon.swing, weapon.heavySwing, weapon.thrustSwing,
                    weapon.lightHit, weapon.heavyHit, weapon.stabHit })
                {
                    var group = bank.FindGroup(id);
                    if (group?.clips == null || group.clips.Length == 0 || group.clips.Any(c => !c) || !group.output)
                        throw new InvalidOperationException("Missing weapon group: " + weapon.weapon + "/" + id);
                }
                report.Add($"PASS {weapon.weapon}: swing={weapon.swing}; heavy={weapon.heavySwing}; " +
                    $"thrust={weapon.thrustSwing}; light={weapon.lightHit}; " +
                    $"hit={weapon.heavyHit}; stab={weapon.stabHit}");
            }
            foreach (var group in bank.groups.Where(g => g.clips.Any(c => c &&
                AssetDatabase.GetAssetPath(c).Contains("/WeaponPacks/"))))
            {
                if (group.clipGains.Length != group.clips.Length || group.startOffsets.Length != group.clips.Length ||
                    group.output.audioMixer != bank.mixer || group.layers.Any(id => bank.FindGroup(id) == null))
                    throw new InvalidOperationException("Invalid weapon group settings: " + group.id);
                foreach (var clip in group.clips)
                {
                    var importer = (AudioImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip));
                    if (clip.channels != 1 || clip.length > .5f ||
                        importer.defaultSampleSettings.loadType != AudioClipLoadType.DecompressOnLoad ||
                        !importer.defaultSampleSettings.preloadAudioData)
                        throw new InvalidOperationException(
                            "Weapon cue is not short, mono and preloaded: " + clip.name);
                }
                report.Add($"PASS {group.id}: {group.clips.Length} short PCM variants; mixer={group.output.name}");
            }
            report.Add("PASS installed bank is already referenced by BattleScene and both fighters.");
            report.Add("Selection uses pack categories, waveform onset/peak/RMS and animation cue roles.");
            report.Add("NOT AUDITIONED HERE: direct listening and speaker/headphone balance.");
            Directory.CreateDirectory(ReportPath);
            File.WriteAllLines(ReportPath + "/Bindings.txt", report);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
}
