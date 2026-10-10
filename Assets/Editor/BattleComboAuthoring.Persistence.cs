using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class BattleComboAuthoring
    {
        internal static void SavePresentationFields(Scene scene, BattleVfxPlayer vfx)
        {
            string folder = "Library/AttackPresentationReview/" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            Directory.CreateDirectory(folder);
            string snapshot = folder + "/BattleScene.live.unity";
            byte[] original = File.ReadAllBytes(Battle);
            File.WriteAllBytes(folder + "/BattleScene.original.unity", original);
            if (!EditorSceneManager.SaveScene(scene, snapshot, true))
                throw new IOException("Cannot snapshot the scene presentation.");
            bool bom = original.Length > 2 && original[0] == 239 && original[1] == 187 && original[2] == 191;
            var encoding = new UTF8Encoding(bom, true);
            string saved = File.ReadAllText(Battle, encoding);
            string live = File.ReadAllText(snapshot, encoding);
            saved = MergeFields(saved, live, vfx, new[] { "gunMuzzleFlash", "impactVariants", "bladeSlashVariants" });
            saved = MergeFields(saved, live, vfx.weaponTrails, new[] { "styles", "staticBlades" });
            if (!File.ReadAllBytes(Battle).SequenceEqual(original))
                throw new IOException("BattleScene changed while preparing its presentation.");
            string temporary = folder + "/BattleScene.merged.unity";
            File.WriteAllText(temporary, saved, encoding);
            File.Replace(temporary, Battle, folder + "/BattleScene.backup.unity");
            if (!scene.isDirty)
                EditorSceneManager.MarkSceneDirty(scene);
        }

        static string MergeFields(string saved, string live, Component component, string[] names)
        {
            string newline = saved.Contains("\r\n") ? "\r\n" : "\n";
            if (!component || component.gameObject.scene.path != Battle ||
                PrefabUtility.IsPartOfPrefabInstance(component))
                throw new InvalidOperationException("Expected a saved inline presentation component.");
            ulong id = GlobalObjectId.GetGlobalObjectIdSlow(component).targetObjectId;
            string pattern = @"(?m)^--- !u!114 &" + id + @"\r?\n[\s\S]*?(?=^--- !u!|\z)";
            var oldDocument = Regex.Match(saved, pattern);
            var newDocument = Regex.Match(live, pattern);
            if (!oldDocument.Success || !newDocument.Success)
                throw new InvalidDataException("Missing presentation component " + id);
            string replacement = oldDocument.Value;
            foreach (string name in names)
            {
                string field = @"(?m)^  " + Regex.Escape(name) + @":[^\r\n]*\r?\n" +
                    @"(?:(?!^  [A-Za-z_]\w*:)[^\r\n]+\r?\n)*";
                var authored = Regex.Match(newDocument.Value, field);
                if (!authored.Success)
                    throw new InvalidDataException("Missing authored presentation field " + name);
                string authoredText = authored.Value.Replace("\r\n", "\n").Replace("\n", newline);
                var previous = Regex.Match(replacement, field);
                replacement = previous.Success
                    ? replacement.Substring(0, previous.Index) + authoredText +
                        replacement.Substring(previous.Index + previous.Length)
                    : replacement + authoredText;
            }
            return saved.Substring(0, oldDocument.Index) + replacement +
                saved.Substring(oldDocument.Index + oldDocument.Length);
        }
    }
}
