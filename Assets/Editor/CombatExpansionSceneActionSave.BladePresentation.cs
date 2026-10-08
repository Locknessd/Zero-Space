using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSceneActionSave
    {
        public static void SaveBladePresentation(Scene scene, BattleWeaponTrails trails)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !scene.IsValid() || !scene.isLoaded ||
                scene.path != BattlePath)
                throw new InvalidOperationException("Open saved BattleScene in Edit Mode before saving blade presentation.");
            if (!trails || trails.gameObject.scene != scene || PrefabUtility.IsPartOfPrefabInstance(trails))
                throw new ArgumentException("Supply an inline BattleWeaponTrails component in BattleScene.");
            var id = GlobalObjectId.GetGlobalObjectIdSlow(trails);
            if ((int)id.identifierType != 2 || id.targetObjectId == 0 || id.targetPrefabId != 0 ||
                id.assetGUID.ToString() != AssetDatabase.AssetPathToGUID(scene.path))
                throw new InvalidOperationException("BattleWeaponTrails requires a saved scene component ID.");

            string path = Path.GetFullPath(scene.path);
            byte[] original = File.ReadAllBytes(path);
            string saved = Decode(original, out bool bom);
            // Reject unsupported saved data before even creating a live scene snapshot.
            ReadBladePresentation(BladeDocument(saved, id.targetObjectId), false);
            string folder = "Library/CombatExpansionTools/SceneSnapshots/" +
                DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff") + "_blades_" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(folder);
            File.WriteAllBytes(Path.Combine(folder, "BattleScene.saved-original.unity"), original);
            try
            {
                string snapshot = Path.Combine(folder, "BattleScene.live.unity");
                if (!EditorSceneManager.SaveScene(scene, snapshot, true))
                    throw new IOException("Could not snapshot live blade presentation; saved scene was not changed.");
                if (!scene.IsValid() || !scene.isLoaded || scene.path != BattlePath ||
                    !trails || trails.gameObject.scene != scene ||
                    !GlobalObjectId.GetGlobalObjectIdSlow(trails).Equals(id))
                    throw new InvalidOperationException("Snapshot changed the scene or component identity.");
                string source = Decode(File.ReadAllBytes(snapshot), out _);
                byte[] output = Encode(MergeBladePresentation(saved, source, id.targetObjectId), bom);
                if (!File.ReadAllBytes(path).SequenceEqual(original))
                    throw new IOException("BattleScene changed on disk during preparation; blade merge cancelled.");
                if (!original.SequenceEqual(output))
                    ReplaceAtomically(path, original, output, folder);
            }
            finally
            {
                // Other live changes are intentionally still unsaved after this selective disk merge.
                if (scene.IsValid() && scene.isLoaded && !scene.isDirty)
                    EditorSceneManager.MarkSceneDirty(scene);
            }
        }
    }
}
