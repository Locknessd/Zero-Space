using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>Audits assets included by the enabled game scenes and Resources.</summary>
[InitializeOnLoad]
public static class WebGLAssetOptimization
{
    private const string RequestPath = "Temp/WebGLAssetOptimization.request";
    private const string BackupRoot = "BuildOptimization/Originals";
    private const string ResultRoot = "BuildOptimization";

    static WebGLAssetOptimization()
    {
        EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode) return;
        var request = File.ReadAllText(RequestPath).Trim();
        File.Delete(RequestPath);
        if (File.Exists("Temp/WebGLAssetOptimization.error")) File.Delete("Temp/WebGLAssetOptimization.error");
        File.WriteAllText("Temp/WebGLAssetOptimization.progress", "Starting " + request);
        try
        {
            if (request == "audit") WriteAudit();
            else if (request == "optimize") Optimize();
            else if (request == "audio") OptimizeAudioOnly();
            else if (request == "models") OptimizeStaticModelsOnly();
            else if (request == "desktop") BuildDesktop();
            else if (request == "mobile") BuildMobile();
            else throw new InvalidOperationException("Unknown optimization job: " + request);
            File.WriteAllText("Temp/WebGLAssetOptimization.progress", "Completed " + request);
        }
        catch (Exception exception)
        {
            File.WriteAllText("Temp/WebGLAssetOptimization.error", exception.ToString());
            Debug.LogException(exception);
        }
    }

    [Serializable]
    private class Audit
    {
        public string unityVersion;
        public string activeBuildTarget;
        public bool webGLSupported;
        public string compression;
        public string stripping;
        public string[] scenes;
        public string[] sceneDependencies;
        public string[] includedDependencies;
        public List<TextureInfo> textures = new List<TextureInfo>();
        public List<AudioInfo> audio = new List<AudioInfo>();
        public List<ModelInfo> models = new List<ModelInfo>();
    }

    [Serializable]
    private class TextureInfo
    {
        public string path;
        public int width, height, sourceWidth, sourceHeight, maxSize, webGLMaxSize;
        public string type, format, webGLFormat, compression;
        public bool readable, mipmaps, alpha, webGLOverride, crunch;
    }

    [Serializable]
    private class AudioInfo
    {
        public string path, format, loadType;
        public float duration, quality;
        public int channels, frequency;
        public bool mono;
    }

    [Serializable]
    private class ModelInfo
    {
        public string path, meshCompression, animationCompression;
        public bool readable, animation, blendShapes;
        public float rotationError, positionError, scaleError;
        public int vertices, meshes;
    }

    [Serializable]
    private class OptimizationResult
    {
        public List<string> movedFolders = new List<string>();
        public List<string> changedTextures = new List<string>();
        public List<string> changedAudio = new List<string>();
        public List<string> changedModels = new List<string>();
        public List<string> changedAnimations = new List<string>();
        public List<string> skippedAudio = new List<string>();
        public List<string> audioDefaultQualityFallback = new List<string>();
        public int animationKeysBefore, animationKeysAfter, constantCurves;
        public long animationSourceBytesBefore, animationSourceBytesAfter;
    }

    private static string[] IncludedAssets()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path);
        var resources = AssetDatabase.GetAllAssetPaths().Where(p =>
            p.StartsWith("Assets/", StringComparison.Ordinal) && p.Contains("/Resources/") &&
            !p.Contains("/Editor/") && !AssetDatabase.IsValidFolder(p));
        return AssetDatabase.GetDependencies(scenes.Concat(resources).ToArray(), true)
            .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).Distinct().OrderBy(p => p).ToArray();
    }

    private static void Backup(string path)
    {
        var destination = Path.Combine(BackupRoot, path);
        if (File.Exists(destination)) return;
        Directory.CreateDirectory(Path.GetDirectoryName(destination));
        File.Copy(path, destination);
    }

    // These packages use direct GUID references. No runtime Resources.Load calls address these folders.
    // Renaming only the Resources folder keeps the assets and demo scenes available in the Editor.
    private static void MoveUnusedResourceFolders(OptimizationResult result)
    {
        var folders = new[]
        {
            "Assets/Hovl Studio/Resources",
            "Assets/Plugins/Vefects/Flipbook VFX/Demo/Resources",
            "Assets/Asstes/FightingUnityChan_Asset10/Resources"
        };
        foreach (var original in folders)
        {
            var destination = original.Substring(0, original.Length - "Resources".Length) + "DemoAssets";
            if (!AssetDatabase.IsValidFolder(original))
            {
                if (AssetDatabase.IsValidFolder(destination)) result.movedFolders.Add(original + " -> " + destination);
                continue;
            }
            if (AssetDatabase.IsValidFolder(destination))
                throw new InvalidOperationException("Cannot move Resources: destination already exists: " + destination);
            Backup(original + ".meta");
            var error = AssetDatabase.MoveAsset(original, destination);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException(error);
            result.movedFolders.Add(original + " -> " + destination);
        }
    }

    [MenuItem("Tools/WebGL/Optimize Game Assets")]
    public static void Optimize()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
            throw new InvalidOperationException("Optimize assets in Edit mode before building.");
        Directory.CreateDirectory(ResultRoot);
        Backup("ProjectSettings/ProjectSettings.asset");
        var result = new OptimizationResult();
        MoveUnusedResourceFolders(result);
        var paths = IncludedAssets();
        int index = 0;
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var path in paths)
            {
                File.WriteAllText("Temp/WebGLAssetOptimization.progress", $"Optimizing {++index}/{paths.Length}: {path}");
                var importer = AssetImporter.GetAtPath(path);
                if (importer is TextureImporter texture) OptimizeTexture(path, texture, result);
                else if (importer is AudioImporter audio) OptimizeAudio(path, audio, result);
                else if (importer is ModelImporter model) OptimizeModel(path, model, result);
                else if (path.StartsWith("Assets/DemoSence/", StringComparison.Ordinal) && path.EndsWith(".anim", StringComparison.Ordinal))
                    ReduceConstantCurves(path, result);
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = false;
        PlayerSettings.WebGL.dataCaching = true;
        // Keep the existing managed stripping level: networking relies on reflection and JSON serializers.
        AssetDatabase.SaveAssets();
        File.WriteAllText(ResultRoot + "/OptimizationResult.json", JsonUtility.ToJson(result, true));
        Debug.Log($"WebGL optimization complete: {result.changedTextures.Count} textures, {result.changedAudio.Count} audio, " +
                  $"{result.changedModels.Count} static models, {result.changedAnimations.Count} animations; " +
                  $"{result.animationKeysBefore - result.animationKeysAfter} redundant animation keys removed.");
    }

    private static bool IsUiOrAtlas(string path, TextureImporter texture)
    {
        // Preserve the resolution and formats of UI, gradients, masks and flipbook effects.
        return texture.textureType == TextureImporterType.Sprite || texture.textureType == TextureImporterType.GUI ||
               !texture.mipmapEnabled || path.Contains("/Vfx/") || path.Contains("/VFX/") ||
               path.Contains("/Fonts") || path.Contains("/TextMesh Pro/");
    }

    private static void OptimizeTexture(string path, TextureImporter texture, OptimizationResult result)
    {
        if (texture.textureShape != TextureImporterShape.Texture2D || IsUiOrAtlas(path, texture)) return;
        var settings = texture.GetPlatformTextureSettings("WebGL");
        var original = JsonUtility.ToJson(settings);
        bool hadOverride = settings.overridden;
        // Automatic format follows the WebGL build's DXT/ASTC subtarget. Explicit per-asset formats would override it.
        settings.name = "WebGL";
        settings.overridden = true;
        settings.format = TextureImporterFormat.Automatic;
        settings.maxTextureSize = hadOverride && settings.maxTextureSize > 0
            ? Mathf.Min(texture.maxTextureSize, settings.maxTextureSize) : texture.maxTextureSize;
        settings.textureCompression = TextureImporterCompression.CompressedHQ;
        settings.compressionQuality = 90;
        settings.crunchedCompression = texture.textureType != TextureImporterType.NormalMap;
        // Only small environmental props get a smaller cap. Fighter textures and background buildings retain their size.
        if (path.StartsWith("Assets/BeatEmUp_GameTemplate3D/Textures/", StringComparison.Ordinal) &&
            (Path.GetFileNameWithoutExtension(path) == "TrashProps" || Path.GetFileNameWithoutExtension(path) == "Lamps"))
            settings.maxTextureSize = Mathf.Min(settings.maxTextureSize, 1024);
        if (original == JsonUtility.ToJson(settings)) return;
        Backup(path + ".meta");
        texture.SetPlatformTextureSettings(settings);
        texture.SaveAndReimport();
        result.changedTextures.Add(path);
    }

    private static void OptimizeAudio(string path, AudioImporter audio, OptimizationResult result)
    {
        var settings = audio.ContainsSampleSettingsOverride(BuildTargetGroup.WebGL)
            ? audio.GetOverrideSampleSettings(BuildTargetGroup.WebGL) : audio.defaultSampleSettings;
        // WebGL encodes audio as AAC. Retain short sound timing, source sample rate, channels and preload behavior.
        bool needsChange = !audio.ContainsSampleSettingsOverride(BuildTargetGroup.WebGL) ||
                           (settings.compressionFormat != AudioCompressionFormat.AAC &&
                            settings.compressionFormat != AudioCompressionFormat.Vorbis) ||
                           settings.quality > .8f || settings.loadType == AudioClipLoadType.Streaming;
        if (!needsChange) return;
        Backup(path + ".meta");
        settings.compressionFormat = AudioCompressionFormat.AAC;
        settings.quality = Mathf.Min(settings.quality, .8f);
        if (settings.loadType == AudioClipLoadType.Streaming)
            settings.loadType = AudioClipLoadType.CompressedInMemory;
        if (!audio.SetOverrideSampleSettings(BuildTargetGroup.WebGL, settings))
        {
            // Some Editor versions expose Vorbis import settings and transcode to AAC for WebGL.
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            if (!audio.SetOverrideSampleSettings(BuildTargetGroup.WebGL, settings))
            {
                // Editors without WebGL audio overrides use the default quality when transcoding to AAC.
                // Keep PCM/other formats and load behavior intact on native platforms.
                var defaults = audio.defaultSampleSettings;
                if (defaults.quality > .8f)
                {
                    defaults.quality = .8f;
                    audio.defaultSampleSettings = defaults;
                    audio.SaveAndReimport();
                    result.changedAudio.Add(path);
                }
                result.audioDefaultQualityFallback.Add(path);
                return;
            }
        }
        audio.SaveAndReimport();
        result.changedAudio.Add(path);
    }

    // Retry only audio after enabling a WebGL module, keeping the first optimization measurements.
    [MenuItem("Tools/WebGL/Retry WebGL Audio Settings")]
    public static void OptimizeAudioOnly()
    {
        var result = JsonUtility.FromJson<OptimizationResult>(File.ReadAllText(ResultRoot + "/OptimizationResult.json"));
        var paths = result.skippedAudio.ToArray();
        result.skippedAudio.Clear();
        if (result.audioDefaultQualityFallback == null) result.audioDefaultQualityFallback = new List<string>();
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var path in paths)
            {
                File.WriteAllText("Temp/WebGLAssetOptimization.progress", "Optimizing audio " + path);
                if (AssetImporter.GetAtPath(path) is AudioImporter importer) OptimizeAudio(path, importer, result);
                else result.skippedAudio.Add(path);
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.SaveAssets();
        File.WriteAllText(ResultRoot + "/OptimizationResult.json", JsonUtility.ToJson(result, true));
    }

    private static void OptimizeModel(string path, ModelImporter model, OptimizationResult result)
    {
        // Static environment only. Source combat rigs, skinning, readable meshes and animated models stay intact.
        if (!path.StartsWith("Assets/BeatEmUp_GameTemplate3D/Environment/Models/", StringComparison.Ordinal) ||
            model.meshCompression != ModelImporterMeshCompression.Off) return;
        Backup(path + ".meta");
        model.meshCompression = ModelImporterMeshCompression.Low;
        model.SaveAndReimport();
        result.changedModels.Add(path);
    }

    [MenuItem("Tools/WebGL/Optimize Static Environment Meshes")]
    public static void OptimizeStaticModelsOnly()
    {
        var result = JsonUtility.FromJson<OptimizationResult>(File.ReadAllText(ResultRoot + "/OptimizationResult.json"));
        var audit = JsonUtility.FromJson<Audit>(File.ReadAllText(ResultRoot + "/BaselineAudit.json"));
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var model in audit.models)
            {
                if (AssetImporter.GetAtPath(model.path) is ModelImporter importer) OptimizeModel(model.path, importer, result);
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.SaveAssets();
        File.WriteAllText(ResultRoot + "/OptimizationResult.json", JsonUtility.ToJson(result, true));
    }

    private static void ReduceConstantCurves(string path, OptimizationResult result)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if (!clip) return;
        int before = 0, after = 0, changed = 0;
        float duration = clip.length;
        var events = clip.events;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            var keys = curve.keys;
            before += keys.Length;
            if (keys.Length <= 2 || keys.Any(k => k.value != keys[0].value || k.inTangent != 0f || k.outTangent != 0f))
            {
                after += keys.Length;
                continue;
            }
            if (changed == 0) Backup(path);
            // Keep the endpoints and wrap modes; a Hermite curve with equal values and zero slopes is constant.
            var reduced = new AnimationCurve(keys[0], keys[keys.Length - 1])
            {
                preWrapMode = curve.preWrapMode, postWrapMode = curve.postWrapMode
            };
            AnimationUtility.SetEditorCurve(clip, binding, reduced);
            after += 2;
            changed++;
        }
        if (changed == 0) return;
        if (clip.length != duration || clip.events.Length != events.Length)
            throw new InvalidOperationException("Animation duration/events changed unexpectedly: " + path);
        result.animationSourceBytesBefore += new FileInfo(path).Length;
        EditorUtility.SetDirty(clip);
        AssetDatabase.SaveAssetIfDirty(clip);
        // SetEditorCurve creates large editing caches. They are absent in the original generated clips
        // and aren't needed by playback; retain all serialized runtime curves and clip settings verbatim.
        var serialized = File.ReadAllText(path);
        var trimmed = Regex.Replace(serialized, @"(?ms)^  m_EditorCurves:.*?(?=^  m_\w+:)", "  m_EditorCurves: []\r\n");
        trimmed = Regex.Replace(trimmed, @"(?ms)^  m_EulerEditorCurves:.*?(?=^  m_\w+:)", "  m_EulerEditorCurves: []\r\n");
        if (serialized != trimmed)
        {
            File.WriteAllText(path, trimmed);
            AssetDatabase.ImportAsset(path);
        }
        result.animationSourceBytesAfter += new FileInfo(path).Length;
        result.animationKeysBefore += before;
        result.animationKeysAfter += after;
        result.constantCurves += changed;
        result.changedAnimations.Add(path);
    }

    [MenuItem("Tools/WebGL/Build Release Desktop")]
    public static void BuildDesktop() => BuildRelease(WebGLTextureSubtarget.DXT, "Build/WebGL-Desktop");

    // GameCI starts Unity with -buildTarget WebGL before invoking this method.
    // Startup mode is selected by the player platform, with no scene option to configure.
    public static void BuildDesktopCI() => BuildDesktop();

    [MenuItem("Tools/WebGL/Build Release Mobile")]
    public static void BuildMobile() => BuildRelease(WebGLTextureSubtarget.ASTC, "Build/WebGL-Mobile");

    private static void BuildRelease(WebGLTextureSubtarget textures, string output)
    {
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.WebGL)
            throw new InvalidOperationException("Switch to WebGL in Build Profiles before starting a release build.");
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
        PlayerSettings.WebGL.decompressionFallback = false;
        EditorUserBuildSettings.webGLBuildSubtarget = textures;
        // Crunch supports DXT/ETC, while ASTC is already a separate GPU encoding.
        var paths = IncludedAssets();
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var path in paths)
            {
                if (!(AssetImporter.GetAtPath(path) is TextureImporter importer) || IsUiOrAtlas(path, importer)) continue;
                var settings = importer.GetPlatformTextureSettings("WebGL");
                bool crunch = textures == WebGLTextureSubtarget.DXT && importer.textureType != TextureImporterType.NormalMap;
                if (!settings.overridden || settings.format != TextureImporterFormat.Automatic || settings.crunchedCompression == crunch) continue;
                settings.crunchedCompression = crunch;
                importer.SetPlatformTextureSettings(settings);
                importer.SaveAndReimport();
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = output,
            target = BuildTarget.WebGL,
            options = BuildOptions.DetailedBuildReport
        });
        Directory.CreateDirectory(ResultRoot);
        File.WriteAllText(ResultRoot + "/Build-" + textures + ".txt",
            $"Result: {report.summary.result}\nTotal build bytes: {report.summary.totalSize}\n" +
            $"Duration: {report.summary.totalTime}\n" +
            string.Join("\n", report.packedAssets.SelectMany(p => p.contents)
                .OrderByDescending(p => p.packedSize).Select(p => $"{p.packedSize}\t{p.sourceAssetPath}")));
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("WebGL release build failed. See Console and BuildOptimization report.");
    }

    [MenuItem("Tools/WebGL/Write Asset Audit")]
    public static void WriteAudit()
    {
        var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        var sceneDependencies = AssetDatabase.GetDependencies(scenes, true);
        var resources = AssetDatabase.GetAllAssetPaths().Where(p =>
            p.StartsWith("Assets/", StringComparison.Ordinal) && p.Contains("/Resources/") &&
            !p.Contains("/Editor/") && !AssetDatabase.IsValidFolder(p)).ToArray();
        var included = AssetDatabase.GetDependencies(scenes.Concat(resources).ToArray(), true)
            .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).Distinct().OrderBy(p => p).ToArray();
        var audit = new Audit
        {
            unityVersion = Application.unityVersion,
            activeBuildTarget = EditorUserBuildSettings.activeBuildTarget.ToString(),
            webGLSupported = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.WebGL, BuildTarget.WebGL),
            compression = PlayerSettings.WebGL.compressionFormat.ToString(),
            stripping = PlayerSettings.GetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.WebGL).ToString(),
            scenes = scenes,
            sceneDependencies = sceneDependencies,
            includedDependencies = included
        };
        foreach (var path in included)
        {
            File.WriteAllText("Temp/WebGLAssetOptimization.progress", "Auditing " + path);
            var importer = AssetImporter.GetAtPath(path);
            if (importer is TextureImporter texture)
            {
                var asset = AssetDatabase.LoadAssetAtPath<Texture>(path);
                var settings = texture.GetPlatformTextureSettings("WebGL");
                texture.GetSourceTextureWidthAndHeight(out int width, out int height);
                audit.textures.Add(new TextureInfo
                {
                    path = path, width = asset ? asset.width : 0, height = asset ? asset.height : 0,
                    sourceWidth = width, sourceHeight = height, maxSize = texture.maxTextureSize,
                    webGLMaxSize = settings.maxTextureSize, type = texture.textureType.ToString(),
                    format = texture.GetAutomaticFormat("WebGL").ToString(), webGLFormat = settings.format.ToString(),
                    compression = texture.textureCompression.ToString(), readable = texture.isReadable,
                    mipmaps = texture.mipmapEnabled, alpha = texture.DoesSourceTextureHaveAlpha(),
                    webGLOverride = settings.overridden, crunch = settings.crunchedCompression
                });
            }
            else if (importer is AudioImporter audio)
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                var settings = audio.ContainsSampleSettingsOverride(BuildTargetGroup.WebGL)
                    ? audio.GetOverrideSampleSettings(BuildTargetGroup.WebGL) : audio.defaultSampleSettings;
                audit.audio.Add(new AudioInfo
                {
                    path = path, format = settings.compressionFormat.ToString(), loadType = settings.loadType.ToString(),
                    duration = clip ? clip.length : 0, channels = clip ? clip.channels : 0,
                    frequency = clip ? clip.frequency : 0, quality = settings.quality, mono = audio.forceToMono
                });
            }
            else if (importer is ModelImporter model)
            {
                var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().ToArray();
                audit.models.Add(new ModelInfo
                {
                    path = path, readable = model.isReadable, meshCompression = model.meshCompression.ToString(),
                    animationCompression = model.animationCompression.ToString(), animation = model.importAnimation,
                    blendShapes = model.importBlendShapes, rotationError = model.animationRotationError,
                    positionError = model.animationPositionError, scaleError = model.animationScaleError,
                    vertices = meshes.Sum(m => m.vertexCount), meshes = meshes.Length
                });
            }
        }
        Directory.CreateDirectory("Temp");
        File.WriteAllText("Temp/WebGLAssetAudit.json", JsonUtility.ToJson(audit, true));
        Debug.Log($"WebGL asset audit: {included.Length} dependencies, {audit.textures.Count} textures, " +
                  $"{audit.audio.Count} audio clips, {audit.models.Count} models. Temp/WebGLAssetAudit.json");
    }
}
