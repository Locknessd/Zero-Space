using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        public static void InstallBladePresentation()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            var scene = SceneManager.GetSceneByPath("Assets/Scenes/BattleScene.unity");
            if (!scene.IsValid() || !scene.isLoaded)
                throw new InvalidOperationException("Open the saved BattleScene before installing blade presentation.");
            var players = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<BattleVfxPlayer>(true)).ToArray();
            if (players.Length != 1 || !players[0].weaponTrails ||
                players[0].weaponTrails.gameObject.scene != scene)
                throw new InvalidOperationException("BattleScene requires one VFX player with its owned weapon trails.");
            var trails = players[0].weaponTrails;
            var oldBlades = trails.staticBlades;
            var oldExclusions = trails.excludedMeshes;
            string description;
            try
            {
                description = ConfigureBladePresentation(trails);
                CombatExpansionSceneActionSave.SaveBladePresentation(scene, trails);
            }
            catch
            {
                trails.staticBlades = oldBlades;
                trails.excludedMeshes = oldExclusions;
                EditorUtility.SetDirty(trails);
                throw;
            }
            string output = BladeOutput + "/Presentation";
            Directory.CreateDirectory(output);
            File.WriteAllText(output + "/Installation.txt", description +
                "Saved staticBlades and excludedMeshes on BattleScene component " +
                GlobalObjectId.GetGlobalObjectIdSlow(trails).targetObjectId + ".\n" +
                "Other saved scene fields and unrelated live scene changes preserved.\n" +
                "Samurai gameplay registration, trail appearance and contact directions remain unverified.\n");
            Debug.Log("Saved Samurai blade calibration and sheath exclusions into BattleScene.");
        }
    }
}
