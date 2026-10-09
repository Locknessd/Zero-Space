using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamurai10Setup
    {
        static void Commit(Scene scene, CharacterCombat[] fighters,
            Dictionary<string, CombatActionDefinition> definitions,
            Dictionary<CharacterCombat, CombatTripletData[]> replacements, string report)
        {
            var clearDirtiness = typeof(EditorSceneManager).GetMethod("ClearSceneDirtiness",
                BindingFlags.Static | BindingFlags.NonPublic);
            if (!scene.isDirty && clearDirtiness == null)
                throw new InvalidOperationException("Cannot preserve clean scene state on failed installation.");
            string reportPath = Output + "/Execution10Installation.txt";
            Directory.CreateDirectory(Output);
            var paths = definitions.Keys.SelectMany(k => new[] { ActionPath(k), ActionPath(k) + ".meta" })
                .Append(scene.path).Append(reportPath).ToArray();
            var bytes = paths.ToDictionary(p => p, p => File.Exists(p) ? File.ReadAllBytes(p) : null);
            var original = fighters.ToDictionary(f => f, f => f.heavyCombatMoves);
            var dirty = fighters.ToDictionary(f => f, EditorUtility.IsDirty);
            bool sceneDirty = scene.isDirty;
            using var preservation = new CombatExpansionSamuraiStudy.Execution10InstallationGuard();
            preservation.VerifyBeforeCommit();
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install measured Samurai Execution10");
            try
            {
                foreach (var source in fighters)
                {
                    string path = ActionPath(source.name);
                    var saved = AssetDatabase.LoadAssetAtPath<CombatActionDefinition>(path);
                    if (!saved)
                    {
                        saved = definitions[source.name];
                        AssetDatabase.CreateAsset(saved, path);
                    }
                    else
                    {
                        Undo.RecordObject(saved, "Configure measured Samurai Execution10");
                        EditorUtility.CopySerialized(definitions[source.name], saved);
                        EditorUtility.SetDirty(saved);
                    }
                    AssetDatabase.SaveAssetIfDirty(saved);
                    replacements[source].Single(m => m != null && m.moveName == Id).actionDefinition = saved;
                }
                Undo.RecordObjects(fighters.Cast<UnityEngine.Object>().ToArray(), "Install Samurai Execution10");
                foreach (var source in fighters)
                {
                    source.heavyCombatMoves = replacements[source];
                    EditorUtility.SetDirty(source);
                }
                CombatExpansionSceneActionSave.Save(scene, fighters, new[] { Id });
                preservation.VerifySourcesAfterCommit();
                File.WriteAllText(reportPath, report);
                Undo.CollapseUndoOperations(undo);
            }
            catch
            {
                Undo.RevertAllDownToGroup(undo);
                foreach (var source in fighters)
                {
                    source.heavyCombatMoves = original[source];
                    if (dirty[source])
                        EditorUtility.SetDirty(source);
                    else
                        EditorUtility.ClearDirty(source);
                }
                foreach (string name in definitions.Keys)
                    if (bytes[ActionPath(name)] == null)
                        AssetDatabase.DeleteAsset(ActionPath(name));
                foreach (var entry in bytes)
                {
                    if (entry.Value == null)
                    {
                        if (File.Exists(entry.Key))
                            File.Delete(entry.Key);
                    }
                    else
                        File.WriteAllBytes(entry.Key, entry.Value);
                }
                foreach (string name in definitions.Keys)
                {
                    string path = ActionPath(name);
                    if (bytes[path] != null)
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
                if (sceneDirty)
                    EditorSceneManager.MarkSceneDirty(scene);
                else
                    clearDirtiness.Invoke(null, new object[] { scene });
                throw;
            }
        }
    }
}
