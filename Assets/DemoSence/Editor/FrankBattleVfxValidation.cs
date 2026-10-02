using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CartoonFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        [MenuItem("Tools/Battle/Validate battle VFX")]
        public static void ValidateBattleVfx()
        {
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var report = new StringBuilder();
            Directory.CreateDirectory("Temp/FrankRetarget/Vfx");
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var vfx = game.battleVfx;
                if (!vfx || vfx.timeline != game.battleSfx.bank ||
                    game.leftCombat.battleVfx != vfx || game.rightCombat.battleVfx != vfx)
                    throw new Exception("Missing shared VFX/timeline bindings.");
                foreach (var prefab in new[] { vfx.lightHit, vfx.heavyHit, vfx.landingDust, vfx.bladeSlash })
                {
                    if (!prefab || prefab.GetComponentsInChildren<ParticleSystem>(true).Length == 0)
                        throw new Exception("Missing particle prefab.");
                    foreach (var particles in prefab.GetComponentsInChildren<ParticleSystem>(true))
                        if (particles.main.playOnAwake || particles.main.loop ||
                            particles.main.scalingMode != ParticleSystemScalingMode.Hierarchy)
                            throw new Exception("Invalid pooled particle setup: " + prefab.name);
                    foreach (var effect in prefab.GetComponentsInChildren<CFXR_Effect>(true))
                        if (effect.clearBehavior != CFXR_Effect.ClearBehavior.None || effect.cameraShake.enabled)
                            throw new Exception("Effect could destroy its pool object or shake the camera: " + prefab.name);
                    foreach (var renderer in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    {
                        if (!renderer.sharedMaterial) throw new Exception("Missing particle material: " + prefab.name);
                        foreach (var material in renderer.sharedMaterials.Where(m => m))
                            if (!material.shader || !material.shader.isSupported)
                                throw new Exception("Unsupported effect material: " + prefab.name + "/" +
                                    (material ? material.name : "missing") + "; shader=" +
                                    (material && material.shader ? material.shader.name : "missing"));
                    }
                }
                var fighters = new[] { game.leftCombat, game.rightCombat };
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).First();
                int cases = 0;
                foreach (var attacker in fighters)
                foreach (var move in attacker.lightCombatMoves.Concat(attacker.heavyCombatMoves))
                foreach (bool lethal in new[] { false, true })
                {
                    var receiver = fighters.Single(f => f != attacker);
                    foreach (var fighter in fighters) fighter.ResetCombat();
                    vfx.ResetForMatch();
                    attacker.transform.position = new Vector3(-.5f, 0, 0);
                    receiver.transform.position = new Vector3(-.5f + move.attackRange, 0, 0);
                    var heard = new List<string>();
                    Action<string, GameObject> observe = (id, root) =>
                    {
                        heard.Add(id);
                        Vector3 position = root.transform.position;
                        if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
                            throw new Exception("Non-finite effect position.");
                        if (id.EndsWith("fall") && Mathf.Abs(position.y - vfx.groundHeight - .035f) > .001f)
                            throw new Exception("Dust is not on the arena floor.");
                        if (!root.GetComponentsInChildren<ParticleSystem>().Any(p => p.isPlaying))
                            throw new Exception("Cue did not start any particle systems.");
                    };
                    vfx.EffectPlayed += observe;
                    try
                    {
                        if (!attacker.ExecuteAttack(move, receiver, lethal)) throw new Exception("Attack rejected: " + move.moveName);
                        var pair = attacker.SourcePlayback;
                        if (heard.Count != 0) throw new Exception("VFX fired during the animation windup.");
                        var profile = vfx.timeline.FindMove(move);
                        foreach (float time in profile.cues.Select(c => c.seconds).Distinct())
                        {
                            pair.EvaluateAt(time);
                            vfx.AdvanceSequence(pair, time);
                            int count = heard.Count;
                            vfx.AdvanceSequence(pair, time);
                            vfx.AdvanceSequence(pair, Mathf.Max(0, time - .25f));
                            if (count != heard.Count) throw new Exception("Duplicate VFX on repeated/reversed time.");
                            if (attacker == fighters[0] && !lethal &&
                                (move.moveName == "Light_1" && time == profile.cues.Last().seconds ||
                                 move.moveName == "Heavy_6" && Mathf.Abs(time - 1.47f) < .01f))
                                CaptureBattleVfx(camera, vfx, "Temp/FrankRetarget/Vfx/" + move.moveName + ".png");
                        }
                        int hits = profile.cues.Count(c => c.group == "light_hit" || c.group == "heavy_hit" || c.group == "stab_hit");
                        if (move.weapon == TrumpWeaponManager.WeaponType.None) hits++;
                        if (heard.Count(id => id.EndsWith("hit")) != hits ||
                            heard.Count(id => id.EndsWith("fall")) != profile.cues.Count(c => c.group == "body_fall"))
                            throw new Exception("Missing hit/landing effects: " + move.moveName);
                        if (lethal && heard.Count(id => id == "knockout_fall") != 1)
                            throw new Exception("Knockout landing missing or repeated.");
                        if (move.weapon == TrumpWeaponManager.WeaponType.None && heard.Contains("blade_slash"))
                            throw new Exception("Unarmed move spawned a weapon slash.");
                        pair.Cancel();
                        int cancelledCount = heard.Count;
                        vfx.AdvanceSequence(pair, pair.Duration);
                        if (heard.Count != cancelledCount || vfx.ActiveEffectCount != 0)
                            throw new Exception("Cancellation did not stop VFX.");
                        report.AppendLine($"PASS {attacker.name} {move.moveName} lethal={lethal}: {heard.Count} effects.");
                        cases++;
                    }
                    finally { vfx.EffectPlayed -= observe; }
                }
                // A dropped frame can cross an entire combo; audio being disabled must not disable VFX.
                foreach (var fighter in fighters) fighter.ResetCombat();
                vfx.ResetForMatch();
                game.battleSfx.enabled = false;
                fighters[0].transform.position = Vector3.zero;
                var skippedMove = fighters[0].heavyCombatMoves.Single(m => m.moveName == "Heavy_8");
                fighters[1].transform.position = Vector3.right * skippedMove.attackRange;
                int beforeSkip = vfx.PlayedEffectCount;
                if (!fighters[0].ExecuteAttack(skippedMove, fighters[1])) throw new Exception("Skipped-frame attack rejected.");
                var skippedPair = fighters[0].SourcePlayback;
                skippedPair.EvaluateAt(skippedPair.Duration);
                vfx.AdvanceSequence(skippedPair, skippedPair.Duration);
                if (vfx.PlayedEffectCount - beforeSkip != 13)
                    throw new Exception("Skipped-frame or muted-audio playback lost VFX cues.");
                skippedPair.Cancel();
                game.battleSfx.enabled = true;
                report.AppendLine("PASS whole-combo frame skip with audio disabled: 13 VFX cues retained.");
                if (vfx.PooledEffectCount > vfx.maxInstances) throw new Exception("Effect pool exceeded its cap.");
                report.AppendLine($"PASS {cases} cases; pooled={vfx.PooledEffectCount}/{vfx.maxInstances}; shaders supported; duplicate/cancel checks passed.");
                File.WriteAllText("Temp/FrankRetarget/battle-vfx-validation.txt", report.ToString());
            }
            catch (Exception exception)
            {
                File.WriteAllText("Temp/FrankRetarget/battle-vfx-validation.txt", report + "FAIL " + exception);
                throw;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void CaptureBattleVfx(Camera camera, BattleVfxPlayer vfx, string path)
        {
            var position = camera.transform.position;
            var rotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            var previousTarget = camera.targetTexture;
            var previousScene = camera.scene;
            var previousActive = RenderTexture.active;
            var target = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.transform.position = new Vector3(.1f, 1.55f, 5);
                camera.transform.LookAt(new Vector3(.1f, .85f, 0));
                camera.fieldOfView = 42;
                camera.scene = vfx.gameObject.scene;
                foreach (var particles in vfx.gameObject.scene.GetRootGameObjects()
                    .Where(r => r.name == "Battle VFX (runtime)").SelectMany(r => r.GetComponentsInChildren<ParticleSystem>()))
                    particles.Simulate(.12f, false, false, true);
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fieldOfView;
                camera.targetTexture = previousTarget;
                camera.scene = previousScene;
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(image);
            }
        }
    }
}
