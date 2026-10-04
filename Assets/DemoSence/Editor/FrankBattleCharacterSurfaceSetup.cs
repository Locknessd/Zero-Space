using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string CharacterSurfaceReview = "GeneratedAssets/BattleCharacterSurfaceReview";

        [MenuItem("Tools/Battle/Clean character shading")]
        public static void CleanBattleCharacterShading()
        {
            Directory.CreateDirectory(CharacterSurfaceReview + "/MaterialsBefore");
            if (!File.Exists(CharacterSurfaceReview + "/BattleSceneBefore.unity.txt"))
                File.Copy(SfxScene, CharacterSurfaceReview + "/BattleSceneBefore.unity.txt");
            if (!File.Exists(CharacterSurfaceReview + "/Before.png"))
            {
                foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/Materials/BattleFlatKit" }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileName(path).StartsWith("Environment_", StringComparison.Ordinal)) continue;
                    File.Copy(path, CharacterSurfaceReview + "/MaterialsBefore/" + Path.GetFileName(path) + ".txt", true);
                }
                CaptureCharacterSurfaces("Before");
            }
            InstallBattleComicStep3();
            CaptureCharacterSurfaces("After");
        }

        static void CaptureCharacterSurfaces(string stage)
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var target = new RenderTexture(1280, 720, 24);
            BattleLightingRig lighting = null; Camera camera = null;
            var report = new StringBuilder();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GameManager>(true)).Single();
                camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.targetTexture = target;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Canvas>(true)))
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters) fighter.Initialize();
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig();
                game.uiManager.SetTurnNumber(1);
                var director = camera.GetComponent<FrankCinematicCamera>();
                lighting.RefreshLighting(0); director.ResetView(); director.Apply(0, true);
                Canvas.ForceUpdateCanvases(); CaptureBattleCamera(camera, CharacterSurfaceReview + "/" + stage + ".png");
                foreach (var fighter in fighters)
                    fighter.Animator.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(
                        camera.transform.position - fighter.Animator.transform.position, Vector3.up));
                Canvas.ForceUpdateCanvases(); CaptureBattleCamera(camera, CharacterSurfaceReview + "/" + stage + "_Front.png");
                foreach (var attacker in fighters)
                {
                    foreach (var fighter in fighters) fighter.ResetCombat();
                    var move = attacker.heavyCombatMoves.Single(m => m.moveName == "Heavy_2");
                    var receiver = fighters.Single(f => f != attacker);
                    bool right = attacker == game.rightCombat;
                    attacker.transform.position = new Vector3(right ? .5f : -.5f, 0, 0);
                    receiver.transform.position = attacker.transform.position + Vector3.right * (right ? -move.attackRange : move.attackRange);
                    if (!attacker.ExecuteAttack(move, receiver)) throw new Exception("Character surface preview rejected.");
                    var playback = attacker.SourcePlayback;
                    playback.EvaluateAt(1.28f);
                    lighting.RefreshLighting(0); director.ResetView(); director.Apply(0, true);
                    Canvas.ForceUpdateCanvases();
                    CaptureBattleCamera(camera, CharacterSurfaceReview + "/" + stage + "_" + attacker.name + ".png");
                    playback.Cancel();
                }
                var materials = fighters.SelectMany(f => f.GetComponentsInChildren<Renderer>(true))
                    .SelectMany(r => r.sharedMaterials).Where(m => m &&
                        AssetDatabase.GetAssetPath(m).StartsWith("Assets/Materials/BattleFlatKit/", StringComparison.Ordinal)).Distinct().ToArray();
                foreach (var material in materials)
                {
                    if (!material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
                        throw new Exception("Invalid character shader: " + material.name);
                    if (stage == "After" && (material.IsKeywordEnabled("DR_SPECULAR_ON") || material.IsKeywordEnabled("DR_RIM_ON")))
                        throw new Exception("Dark surface patch layer is still enabled: " + material.name);
                    report.AppendLine("PASS " + material.name + ": supported shader; specular=" + material.GetFloat("_SpecularEnabled") +
                        "; rim=" + material.GetFloat("_RimEnabled") + "; toon=" + material.GetFloat("_CelPrimaryMode") +
                        "; outline=" + material.GetFloat("_OutlineWidth") + "; texture=" + AssetDatabase.GetAssetPath(material.mainTexture));
                }
                report.AppendLine("PASS native idle and two opposing combat poses rendered with Battle lighting; " + materials.Length + " body materials.");
                File.WriteAllText(CharacterSurfaceReview + "/" + stage + "Validation.txt", report.ToString());
            }
            finally
            {
                if (lighting) lighting.ShutdownRig(); if (camera) camera.targetTexture = null;
                Object.DestroyImmediate(target); EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
