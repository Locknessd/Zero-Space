using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void CaptureBattleComicContacts()
        {
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var target = new RenderTexture(1280, 720, 24);
            BattleLightingRig lighting = null; Camera camera = null;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.targetTexture = target;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                var director = camera.GetComponent<FrankCinematicCamera>();
                game.leftCombat.Initialize(); game.rightCombat.Initialize();
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig();
                game.GetComponent<BattleImpactFeedback>().Bind(); game.uiManager.hudFeedback.Bind(); game.uiManager.SetTurnNumber(1);
                foreach (string name in new[] {"Light_1", "Heavy_6", "Heavy_Katana"})
                {
                    var attacker = game.leftCombat; var receiver = game.rightCombat;
                    attacker.ResetCombat(); receiver.ResetCombat(); game.battleVfx.ResetForMatch();
                    var move = attacker.lightCombatMoves.Concat(attacker.heavyCombatMoves).Single(m => m.moveName == name);
                    var profile = game.battleVfx.timeline.FindMove(move);
                    bool ground = name != "Heavy_6";
                    var cue = ground ? profile.cues.Last(c => c.finalLanding) : profile.cues.First(c => c.group.EndsWith("hit", StringComparison.Ordinal));
                    attacker.transform.position = new Vector3(-.5f, 0, 0); receiver.transform.position = attacker.transform.position + Vector3.right * move.attackRange;
                    var effects = new Dictionary<GameObject, float>();
                    Action<string, GameObject> observe = (id, effect) => effects[effect] = attacker.SourcePlayback.SampleTime;
                    game.battleVfx.EffectPlayed += observe;
                    try
                    {
                        if (!attacker.ExecuteAttack(move, receiver, name == "Heavy_Katana")) throw new Exception("Rejected " + name);
                        var pair = attacker.SourcePlayback;
                        pair.EvaluateAt(cue.seconds); game.battleVfx.AdvanceSequence(pair, cue.seconds);
                        const float age = .04f;
                        pair.EvaluateAt(cue.seconds + age);
                        foreach (var effect in effects.Where(e => e.Key && e.Key.activeSelf))
                        foreach (var p in effect.Key.GetComponentsInChildren<ParticleSystem>())
                        { p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); p.useAutoRandomSeed = false; p.randomSeed = 321; p.Simulate(cue.seconds - effect.Value + age, false, true, true); }
                        lighting.RefreshLighting(age); game.GetComponent<BattleImpactFeedback>().AdvanceFeedback(age);
                        game.uiManager.hudFeedback.AdvanceHud(age);
                        director.ResetView(); director.Apply(0, true); Canvas.ForceUpdateCanvases();
                        game.GetComponent<BattleImpactFeedback>().RefreshFlashProjection();
                        Canvas.ForceUpdateCanvases();
                        CaptureBattleCamera(camera, ComicReview + "/Contact_" + name + (ground ? "_Ground" : "_Hit") + ".png");
                        pair.Cancel();
                    }
                    finally { game.battleVfx.EffectPlayed -= observe; }
                }
            }
            finally
            {
                if (lighting) lighting.ShutdownRig(); if (camera) camera.targetTexture = null;
                Object.DestroyImmediate(target); EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public static void ValidateBattleComicMeshes()
        {
            Directory.CreateDirectory(ComicReview);
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Comic mesh preview");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
            var camera = root.AddComponent<Camera>();
            camera.enabled = false; camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = 1.4f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .08f, .11f);
            var report = new StringBuilder();
            try
            {
                foreach (bool ground in new[] {false, true})
                {
                    camera.transform.position = ground ? new Vector3(0, 3, 4) : new Vector3(0, 0, 5);
                    camera.transform.LookAt(Vector3.zero);
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ComicAssets + (ground ? "/BattleComicGroundImpact.prefab" : "/BattleComicSlash.prefab"));
                    var effect = Object.Instantiate(prefab);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(effect, scene);
                    try
                    {
                        effect.transform.SetPositionAndRotation(Vector3.zero, ground ? Quaternion.identity : camera.transform.rotation);
                        foreach (var p in effect.GetComponentsInChildren<ParticleSystem>(true))
                            p.gameObject.SetActive(p.name == (ground ? "Expanding comic ground ring" : "Directed comic slash"));
                        var particles = effect.GetComponentsInChildren<ParticleSystem>().Single();
                        particles.useAutoRandomSeed = false; particles.randomSeed = 321;
                        particles.Simulate(.08f, false, true, true);
                        var renderer = particles.GetComponent<ParticleSystemRenderer>();
                        var alive = new ParticleSystem.Particle[1]; particles.GetParticles(alive);
                        report.AppendLine("Mesh colors: " + string.Join(", ", renderer.mesh.colors.Distinct().Select(c => c.ToString())) + "; particle=" + alive[0].GetCurrentColor(particles));
                        var baked = new Mesh(); renderer.BakeMesh(baked, camera, false);
                        report.AppendLine("Baked colors: " + string.Join(", ", baked.colors.Distinct().Take(10).Select(c => c.ToString())));
                        Object.DestroyImmediate(baked);
                        var imagePath = ComicReview + (ground ? "/Step2_GroundRing.png" : "/Step2_Slash.png");
                        // The shared capture helper creates a render target and restores the camera.
                        var target = new RenderTexture(512, 512, 24);
                        camera.targetTexture = target;
                        try { CaptureBattleCamera(camera, imagePath); }
                        finally { camera.targetTexture = null; Object.DestroyImmediate(target); }
                        var image = new Texture2D(2, 2);
                        try
                        {
                            image.LoadImage(File.ReadAllBytes(imagePath));
                            int gold = image.GetPixels32().Count(c => c.r > 150 && c.g > 75 && c.g < 220 && c.b < 90);
                            report.AppendLine($"{particles.name}: live particles={particles.particleCount}, mesh vertices={renderer.mesh.vertexCount}, bounds={renderer.bounds}, gold pixels={gold}.");
                            if (particles.particleCount != 1 || gold < 100) throw new Exception("Comic mesh has no visible gold band: " + particles.name);
                        }
                        finally { Object.DestroyImmediate(image); }
                    }
                    finally { Object.DestroyImmediate(effect); }
                }
                report.AppendLine("PASS native slash and floor-ring rendering, gold bands and single-particle budget.");
            }
            catch (Exception exception) { report.AppendLine("FAIL " + exception); throw; }
            finally
            {
                File.WriteAllText(ComicReview + "/Step2MeshValidation.txt", report.ToString());
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
