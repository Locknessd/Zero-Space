using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using ZeroSpace.Showcase;

public static partial class MemeArenaSetup
{
    public static void Validate()
    {
        var report = new StringBuilder();
        foreach (string path in Scenes)
        {
            var scene = EditorSceneManager.OpenPreviewScene(path);
            try
            {
                var roots = scene.GetRootGameObjects();
                var arena = roots.Single(item => item.name == ArenaName);
                var camera = roots.SelectMany(item => item.GetComponentsInChildren<Camera>(true))
                    .Single(item => item.CompareTag("MainCamera"));
                var renderers = arena.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 450 || arena.GetComponentsInChildren<Collider>().Length != 100)
                    throw new InvalidOperationException("Unexpected map geometry or collision count.");
                foreach (var material in renderers.SelectMany(item => item.sharedMaterials).Where(item => item).Distinct())
                    if (!material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
                        throw new InvalidOperationException("Unsupported arena material: " + material.name);
                if (!camera) throw new InvalidOperationException("The scene camera is missing.");
                var game = roots.SelectMany(item => item.GetComponentsInChildren<GameManager>(true)).SingleOrDefault();
                if (game && (!game.leftCombat || !game.rightCombat || !game.battleVfx || !game.battleSfx))
                    throw new InvalidOperationException("Battle gameplay bindings changed.");
                var showcase = roots.Select(item => item.GetComponent<PepeAttackShowcase>()).SingleOrDefault(item => item);
                if (showcase && (!showcase.library || !showcase.character || !showcase.director.cinematicAttackReference))
                    throw new InvalidOperationException("BOM character/camera bindings changed.");
                report.AppendLine("PASS " + path + ": " + renderers.Length + " map renderers; 100 floor colliders; " +
                    "camera, shaders and character bindings valid.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        var bank = AssetDatabase.LoadAssetAtPath<BattleSfxBank>("Assets/Audio/Battle/BattleSfxBank.asset");
        foreach (var group in bank.groups.Where(item => item.id.EndsWith("swing", StringComparison.Ordinal)))
            if (group.clips.Any(clip => !clip) || group.clips.Length == 0 || !group.output ||
                group.startOffsets.Length != group.clips.Length || group.clipGains.Length != group.clips.Length)
                throw new InvalidOperationException("Missing sword sound binding: " + group.id);
        var library = AssetDatabase.LoadAssetAtPath<PepeShowcaseLibrary>("Assets/PepeShowcase/BomPepeTrailerLibrary.asset");
        var cues = library.takes.SelectMany(take => take.cues).Where(cue => cue.swing).ToArray();
        if (cues.Length != 30 || cues.Any(cue => cue.sounds.Length == 0 || cue.sounds.Any(sound => !sound.clip)))
            throw new InvalidOperationException("A showcase slash cue lost its sound.");
        report.AppendLine("PASS 30 slash sound cues; Battle weapon routing and mixer bindings valid.");
        Directory.CreateDirectory(Review);
        File.WriteAllText(Review + "/Validation.txt", report.ToString());
    }
}
