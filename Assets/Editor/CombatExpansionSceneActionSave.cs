using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSceneActionSave
    {
        const string BattlePath = "Assets/Scenes/BattleScene.unity";
        static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public static void Save(Scene scene, CharacterCombat[] fighters, string[] actionIds)
        {
            SaveRegistry(scene, fighters, actionIds, "heavyCombatMoves");
        }

        public static void SaveLight(Scene scene, CharacterCombat[] fighters, string[] actionIds)
        {
            SaveRegistry(scene, fighters, actionIds, "lightCombatMoves");
        }

        static void SaveRegistry(Scene scene, CharacterCombat[] fighters, string[] actionIds, string registry)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid() || !scene.isLoaded ||
                scene.path != BattlePath)
                throw new InvalidOperationException("Open BattleScene in Edit Mode before persisting actions.");
            ValidateIds(actionIds);
            if (fighters == null || fighters.Length == 0)
                throw new ArgumentException("Supply at least one saved fighter.", nameof(fighters));
            var componentIds = new HashSet<ulong>();
            string sceneGuid = AssetDatabase.AssetPathToGUID(scene.path);
            foreach (var fighter in fighters)
            {
                if (!fighter || fighter.gameObject.scene != scene)
                    throw new ArgumentException("Every fighter must belong to the supplied scene.");
                var id = GlobalObjectId.GetGlobalObjectIdSlow(fighter);
                if (id.targetObjectId == 0 || id.assetGUID.ToString() != sceneGuid ||
                    !componentIds.Add(id.targetObjectId))
                    throw new InvalidOperationException("Fighters require distinct saved scene component IDs.");
            }

            string path = Path.GetFullPath(scene.path);
            byte[] original = File.ReadAllBytes(path);
            bool bom;
            string saved = Decode(original, out bom);
            string folder = "Library/CombatExpansionTools/SceneSnapshots/" +
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "BattleScene.saved-original.unity"), original);
            try
            {
                if (!EditorSceneManager.SaveScene(scene, Path.Combine(folder, "BattleScene.live.unity"), true))
                    throw new IOException("Could not snapshot the live scene; saved scene was not changed.");
                if (scene.path != BattlePath || !scene.isLoaded)
                    throw new InvalidOperationException("Snapshot unexpectedly changed the live scene identity.");
                bool sourceBom;
                string source = Decode(File.ReadAllBytes(Path.Combine(folder, "BattleScene.live.unity")),
                    out sourceBom);
                string merged = Merge(saved, source, componentIds.ToArray(), actionIds, registry);
                byte[] output = Encode(merged, bom);
                if (!original.SequenceEqual(output))
                    ReplaceAtomically(path, original, output, folder);
            }
            finally
            {
                // The live scene still contains all unrelated changes omitted from the disk merge.
                if (scene.IsValid() && scene.isLoaded && !scene.isDirty)
                    EditorSceneManager.MarkSceneDirty(scene);
            }
        }

        static void ReplaceAtomically(string path, byte[] original, byte[] output, string folder)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(output, 0, output.Length);
                    stream.Flush(true);
                }
                if (!File.ReadAllBytes(path).SequenceEqual(original))
                    throw new IOException("BattleScene changed on disk during preparation; merge cancelled.");
                File.Replace(temporary, path, Path.Combine(folder, "BattleScene.atomic-backup.unity"));
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }

        static string Decode(byte[] bytes, out bool bom)
        {
            bom = bytes.Length >= 3 && bytes[0] == 239 && bytes[1] == 187 && bytes[2] == 191;
            int offset = bom ? 3 : 0;
            string text = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
            if (text.IndexOf('\0') >= 0 || !text.StartsWith("%YAML 1.1\n", StringComparison.Ordinal) &&
                !text.StartsWith("%YAML 1.1\r\n", StringComparison.Ordinal))
                throw new InvalidDataException("Expected a UTF8 Unity YAML scene.");
            Newline(text);
            return text;
        }

        static byte[] Encode(string text, bool bom)
        {
            byte[] body = StrictUtf8.GetBytes(text);
            return bom ? new byte[] { 239, 187, 191 }.Concat(body).ToArray() : body;
        }

        static string Newline(string text)
        {
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            string normalized = text.Replace(newline, "");
            if (normalized.Contains("\r") || normalized.Contains("\n") ||
                !text.EndsWith(newline, StringComparison.Ordinal))
                throw new InvalidDataException("Mixed or unterminated scene line endings are unsupported.");
            return newline;
        }

        static void ValidateIds(string[] ids)
        {
            if (ids == null || ids.Length == 0 || ids.Any(id => id == null || !NamePattern.IsMatch(id)) ||
                ids.Distinct(StringComparer.Ordinal).Count() != ids.Length)
                throw new ArgumentException("Action IDs must be distinct plain YAML names.", nameof(ids));
        }

        // This check reads the saved scene only and never saves, loads, or dirties a Unity scene.
        public static void ValidatePreservation()
        {
            bool bom;
            byte[] bytes = File.ReadAllBytes(BattlePath);
            string saved = Decode(bytes, out bom);
            var documents = Documents(saved);
            foreach (string registry in new[] { "heavyCombatMoves", "lightCombatMoves" })
            {
                var ids = documents.Where(pair => pair.Value.Text.Contains("  " + registry + ":"))
                    .Select(pair => pair.Key).ToArray();
                if (ids.Length == 0)
                    throw new InvalidDataException("No inline combat registry found: " + registry);
                foreach (ulong id in ids)
                {
                    var list = ReadList(documents[id], registry);
                    var names = list.Entries.Keys.ToArray();
                    if (Merge(saved, saved, new[] { id }, names, registry) != saved)
                        throw new InvalidDataException("Identical entry replacement changed scene content.");
                    const string probe = "ValidationOwnedEntry";
                    if (list.Entries.ContainsKey(probe))
                        throw new InvalidDataException("Reserved preservation probe name already exists.");
                    var first = list.Entries.First();
                    string entry = first.Value.Text.Replace("  - moveName: " + first.Key + Newline(saved),
                        "  - moveName: " + probe + Newline(saved));
                    string expected = saved.Insert(list.End, entry);
                    string source = expected.Insert(expected.IndexOf(Newline(saved), StringComparison.Ordinal),
                        Newline(saved) + "# Unrelated unsaved change must not be persisted.");
                    if (Merge(saved, source, new[] { id }, new[] { probe }, registry) != expected)
                        throw new InvalidDataException("Registry addition changed unrelated scene content.");
                }
            }
            if (!Encode(saved, bom).SequenceEqual(bytes))
                throw new InvalidDataException("Scene encoding did not round trip byte for byte.");
        }
    }
}
