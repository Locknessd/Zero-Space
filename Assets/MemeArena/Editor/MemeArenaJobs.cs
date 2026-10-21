using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void CompleteMemeColosseum() => MemeArenaEditor.MemeArenaIntegration.CreateBattleVariant();
        public static void CheckMemeColosseumPlay() => MemeArenaEditor.MemeArenaPlayCheck.Begin();
        public static void CaptureMemeColosseumOverview() => MemeArenaEditor.MemeArenaOverview.Capture();
        public static void ApplyMemeColosseumToBattle() => MemeArenaEditor.MemeArenaProduction.Apply();
        public static void CheckProductionMemeColosseumPlay() => MemeArenaEditor.MemeArenaPlayCheck.BeginProduction();
        public static void UpgradeMemeCrowdAndFloor() => MemeArenaEditor.MemeArenaRosterUpgrade.UpgradeBattle();
        public static void ImportRobinhoodMemeCatalog() => MemeArenaEditor.MemeCatalogImport.Apply();
        public static void InspectRobinhoodSpriteSheets() => MemeArenaEditor.MemeCatalogImport.InspectSheets();
        public static void RepairMemeFloorShardMesh() => MemeArenaEditor.MemeArenaBuild.RepairFloorShardMesh();
        public static void VerifyMemeEditorScenePreserved() => MemeArenaEditor.MemeArenaEditorContext.VerifySavedCopy(
            "Assets/MemeArena/Documentation/Review/Unsaved_LoadingScene.unity");
        public static void GenerateMemeColosseumFoundation()
        {
            var prefab = MemeArenaEditor.MemeArenaBuild.Generate();
            var report = new StringBuilder("PHASE 1/2: generated " + AssetDatabase.GetAssetPath(prefab) + "\n");
            if (prefab.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new System.Exception("The visual stage must not add colliders.");
            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                foreach (var material in renderer.sharedMaterials)
                    if (!material || !material.shader || !material.shader.isSupported)
                        throw new System.Exception("Invalid stage material on " + renderer.name);
            report.AppendLine("PASS prefab saved, valid URP materials, no added colliders.");
            var stage = prefab.GetComponent<MemeArena.MemeArenaStage>();
            var tier = AssetDatabase.LoadAssetAtPath<Mesh>(stage.generatedAssetFolder + "/Meshes/Tier_0.asset");
            if (tier.normals.Take(4).Average(normal => normal.y) < .9f)
                throw new System.Exception("The seating top normals must face up.");
            report.AppendLine("PASS curved seating top normals face up.");
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                    report.AppendLine("Battle root: " + root.name + " at " + root.transform.position);
                foreach (var combat in scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<CharacterCombat>(true)))
                    report.AppendLine("Fighter: " + combat.name + " at " + combat.transform.position);
                foreach (var camera in scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Camera>(true)))
                    report.AppendLine("Camera: " + camera.name + " at " + camera.transform.position +
                        "; rotation=" + camera.transform.eulerAngles);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
            File.WriteAllText("Assets/MemeArena/Documentation/FoundationValidation.txt", report.ToString());
        }
    }
}
