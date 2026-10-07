using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>Resolve the saved clip selection before editing the bank, including missing native object references.</summary>
public static class BattleAudioClipImportRepair
{
    const string ClipRoot = "Assets/Audio/Battle/Clips";
    const string ReportPath = "GeneratedAssets/BattlePresentationReview/AudioClipImports.txt";

    public static Dictionary<string, AudioClip[]> Prepare(string bankPath)
    {
        var report = new List<string>();
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        try
        {
            var paths = ReadSavedPaths(bankPath);
            // Finish every import before taking persistent AudioClip references.
            // A reimport can invalidate a previously loaded native object wrapper.
            foreach (string path in paths.Values.SelectMany(value => value).Distinct()) ImportClip(path, report);
            var result = new Dictionary<string, AudioClip[]>();
            foreach (var group in paths)
            {
                var clips = new AudioClip[group.Value.Length];
                for (int i = 0; i < clips.Length; i++)
                {
                    string path = group.Value[i];
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (!clip || clip.samples <= 0 || clip.length <= 0)
                    {
                        string contents = DescribeAssets(path);
                        throw new InvalidOperationException(
                            $"Audio import did not produce a playable clip: {group.Key}[{i}], {path}; {contents}");
                    }
                    clips[i] = clip;
                    AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long localId);
                    report.Add($"PASS {group.Key}[{i}] {path}; GUID={guid}, fileID={localId}, samples={clip.samples}");
                }
                result.Add(group.Key, clips);
            }
            report.Add("PASS all saved variants resolved after synchronous native audio import; selection order preserved.");
            return result;
        }
        catch (Exception error)
        {
            report.Add("FAIL " + error);
            throw;
        }
        finally
        {
            File.WriteAllLines(ReportPath, report);
        }
    }

    static Dictionary<string, string[]> ReadSavedPaths(string bankPath)
    {
        // Use the serialized GUIDs, not filename sorting or the selection catalogue's order.
        // This also handles references whose fileID no longer resolves in the current import.
        string yaml = File.ReadAllText(bankPath);
        var metadataPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string meta in Directory.EnumerateFiles(ClipRoot, "*.wav.meta", SearchOption.AllDirectories))
        {
            var guid = Regex.Match(File.ReadAllText(meta), @"(?m)^guid: ([a-fA-F0-9]{32})");
            if (!guid.Success) continue;
            string path = meta.Substring(0, meta.Length - 5).Replace('\\', '/');
            if (metadataPaths.ContainsKey(guid.Groups[1].Value))
                throw new InvalidOperationException("Duplicate playback clip GUID: " + guid.Groups[1].Value);
            metadataPaths.Add(guid.Groups[1].Value, path);
        }
        var result = new Dictionary<string, string[]>();
        var groups = Regex.Matches(yaml,
            @"(?ms)^  - id: ([^\r\n]+)\r?\n(.*?)(?=^  - id: |^  moves:|\z)");
        foreach (Match group in groups)
        {
            string id = group.Groups[1].Value.Trim();
            var clipSection = Regex.Match(group.Groups[2].Value,
                @"(?m)^    clips:[ \t]*\r?\n((?:    - [^\r\n]*\r?\n)+)");
            var references = Regex.Matches(clipSection.Groups[1].Value, @"guid: ([a-fA-F0-9]{32})");
            if (references.Count == 0)
                throw new InvalidOperationException("Saved bank has no recoverable clip GUIDs for " + id);
            var paths = new string[references.Count];
            for (int i = 0; i < references.Count; i++)
            {
                string guid = references[i].Groups[1].Value;
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) metadataPaths.TryGetValue(guid, out path);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    throw new FileNotFoundException($"Missing selected clip {id}[{i}], GUID={guid}");
                if (!path.StartsWith(ClipRoot + "/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Unexpected playback source outside project-owned clips: " + path);
                paths[i] = path;
            }
            result.Add(id, paths);
        }
        if (result.Count == 0) throw new InvalidOperationException("No serialized cue groups found in " + bankPath);
        return result;
    }

    static void ImportClip(string path, List<string> report)
    {
        var before = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        var importerBefore = AssetImporter.GetAtPath(path);
        report.Add($"BEFORE {path}; importer={importerBefore?.GetType().FullName ?? "none"}, " +
            $"clip={(before ? before.name : "missing")}");
        if (!(importerBefore is AudioImporter) && AssetDatabase.GetImporterOverride(path) != null)
        {
            report.Add("Clearing non-audio importer override for selected PCM playback copy: " + path);
            AssetDatabase.ClearImporterOverride(path);
        }
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        var importer = AssetImporter.GetAtPath(path) as AudioImporter;
        if (!importer)
            throw new InvalidOperationException("Expected native AudioImporter: " + path + "; " + DescribeAssets(path));
        var settings = importer.defaultSampleSettings;
        bool changed = settings.loadType != AudioClipLoadType.DecompressOnLoad ||
            !settings.preloadAudioData || importer.loadInBackground;
        if (!changed) return;
        settings.loadType = AudioClipLoadType.DecompressOnLoad;
        settings.preloadAudioData = true;
        importer.defaultSampleSettings = settings;
        importer.loadInBackground = false;
        importer.SaveAndReimport();
    }

    static string DescribeAssets(string path)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        return "importer=" + (AssetImporter.GetAtPath(path)?.GetType().FullName ?? "none") +
            "; objects=" + string.Join(", ", assets.Where(asset => asset).Select(asset => asset.GetType().FullName));
    }
}
