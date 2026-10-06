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
        [MenuItem("Tools/Battle/Preview imported slash candidates")]
        public static void CaptureBattleSlashCandidates()
        {
            string[] paths = {
                "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs/Impacts/CFXR Slash (Blue).prefab",
                "Assets/ErbGameArt/Sword slash FX/Prefabs/New/Slash 3.prefab",
                "Assets/ErbGameArt/Sword slash FX/Prefabs/Slash8 thin orange.prefab",
                "Assets/Hovl Studio/Sword slash VFX/Prefabs/Sword Slash 3.prefab"
            };
            Directory.CreateDirectory("Temp/FrankRetarget/Vfx/Candidates");
            var preview = EditorSceneManager.NewPreviewScene();
            var cameraRoot = new GameObject("Slash preview camera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraRoot, preview);
            var camera = cameraRoot.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = preview;
            camera.transform.position = new Vector3(0, 0, 5);
            camera.transform.LookAt(Vector3.zero);
            camera.orthographic = true;
            camera.orthographicSize = 1.7f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.08f, .08f, .11f);
            var target = new RenderTexture(512, 512, 24);
            var texture = new Texture2D(512, 512, TextureFormat.RGB24, false);
            var previousActive = RenderTexture.active;
            var report = new StringBuilder();
            try
            {
                camera.targetTexture = target;
                for (int index = 0; index < paths.Length; index++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[index]);
                    if (!prefab) { report.AppendLine("MISSING " + paths[index]); continue; }
                    var root = Object.Instantiate(prefab);
                    UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, preview);
                    try
                    {
                        root.transform.SetPositionAndRotation(Vector3.zero, camera.transform.rotation *
                            (index == 0 ? Quaternion.identity : Quaternion.Euler(90, 0, 0)) * prefab.transform.localRotation);
                        root.transform.localScale = Vector3.one * .4f;
                        foreach (var light in root.GetComponentsInChildren<Light>(true)) light.enabled = false;
                        foreach (var particles in root.GetComponentsInChildren<ParticleSystem>())
                        {
                            particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                            var main = particles.main;
                            main.loop = false;
                            main.stopAction = ParticleSystemStopAction.None;
                            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                            particles.Simulate(.08f, false, true, true);
                        }
                        camera.Render();
                        RenderTexture.active = target;
                        texture.ReadPixels(new Rect(0, 0, 512, 512), 0, 0);
                        texture.Apply();
                        File.WriteAllBytes("Temp/FrankRetarget/Vfx/Candidates/" + index + ".png", texture.EncodeToPNG());
                        report.AppendLine(index + ": " + paths[index] + "; shaders=" +
                            string.Join(", ", root.GetComponentsInChildren<ParticleSystemRenderer>()
                                .SelectMany(r => r.sharedMaterials).Where(m => m)
                                .Select(m => m.shader ? m.shader.name + " supported=" + m.shader.isSupported : "missing").Distinct()));
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                File.WriteAllText("Temp/FrankRetarget/Vfx/Candidates/selection.txt", report.ToString());
            }
            finally
            {
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

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
                foreach (var prefab in new[] { vfx.lightHit, vfx.heavyHit, vfx.landingDust, vfx.bladeSlash,
                    vfx.lightSwing, vfx.thrustSwing, vfx.groundImpact, vfx.landingText }
                    .Concat((vfx.bladeSlashVariants ?? Array.Empty<BattleVfxPlayer.WeaponSlashVariant>())
                        .Where(v => v != null && v.prefab).Select(v => v.prefab))
                    .Concat(vfx.impactVariants.SelectMany(v => new[] { v.light, v.heavy, v.stab }.Where(p => p)))
                    .Concat(vfx.shieldImpact ? new[] { vfx.shieldImpact } : Array.Empty<GameObject>())
                    .Concat(vfx.skillVariants.SelectMany(v => new[] { v.cast, v.projectile, v.impact })).Distinct())
                {
                    if (!prefab || prefab.GetComponentsInChildren<ParticleSystem>(true).Length == 0)
                        throw new Exception("Missing particle prefab.");
                    foreach (var particles in prefab.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        if (particles.main.playOnAwake || particles.main.loop ||
                            particles.main.scalingMode != ParticleSystemScalingMode.Hierarchy ||
                            particles.emission.rateOverTime.constantMax != 0 || particles.emission.rateOverDistance.constantMax != 0 || particles.subEmitters.enabled)
                            throw new Exception("Invalid pooled particle setup: " + prefab.name);
                        var bursts = new ParticleSystem.Burst[particles.emission.burstCount]; particles.emission.GetBursts(bursts);
                        if (bursts.Any(b => b.cycleCount != 1)) throw new Exception("Repeating burst: " + prefab.name);
                    }
                    foreach (var effect in prefab.GetComponentsInChildren<CFXR_Effect>(true))
                        if (effect.clearBehavior != CFXR_Effect.ClearBehavior.None ||
                            effect.cameraShake != null && effect.cameraShake.enabled)
                            throw new Exception("Effect could destroy its pool object or shake the camera: " + prefab.name);
                    foreach (var renderer in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    {
                        if (!renderer.sharedMaterial) throw new Exception("Missing particle material: " + prefab.name);
                        foreach (var material in renderer.sharedMaterials.Where(m => m))
                            if (!material.shader || !material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
                                throw new Exception("Unsupported effect material: " + prefab.name + "/" +
                                    (material ? material.name : "missing") + "; shader=" +
                                    (material && material.shader ? material.shader.name : "missing"));
                    }
                }
                var fighters = new[] { game.leftCombat, game.rightCombat };
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).First();
                camera.transform.position = new Vector3(.1f, 1.55f, 5);
                camera.transform.LookAt(new Vector3(.1f, .85f, 0));
                camera.fieldOfView = 42;
                int cases = 0;
                foreach (var attacker in fighters)
                foreach (var move in attacker.lightCombatMoves.Concat(attacker.heavyCombatMoves))
                foreach (bool lethal in new[] { false, true })
                foreach (bool mirrored in new[] { false, true })
                {
                    var receiver = fighters.Single(f => f != attacker);
                    foreach (var fighter in fighters) fighter.ResetCombat();
                    vfx.ResetForMatch();
                    attacker.transform.position = new Vector3(mirrored ? .5f : -.5f, 0, 0);
                    receiver.transform.position = attacker.transform.position + Vector3.right *
                        (mirrored ? -move.attackRange : move.attackRange);
                    var heard = new List<string>();
                    var effectTimes = new Dictionary<GameObject, float>();
                    Action<string, GameObject> observe = (id, root) =>
                    {
                        heard.Add(id);
                        effectTimes[root] = attacker.SourcePlayback.SampleTime;
                        Vector3 position = root.transform.position;
                        if (!float.IsFinite(position.x) || !float.IsFinite(position.y) || !float.IsFinite(position.z))
                            throw new Exception("Non-finite effect position.");
                        if ((id.EndsWith("fall") || id == "ground_impact") &&
                            Mathf.Abs(position.y - vfx.groundHeight - .035f) > .001f)
                            throw new Exception("Dust is not on the arena floor.");
                        bool ribbon = vfx.weaponTrails && vfx.weaponTrails.Owns(root);
                        if (!ribbon && !root.GetComponentsInChildren<ParticleSystem>().Any(p => p.isPlaying))
                            throw new Exception("Cue did not start any particle systems.");
                        if (id == "blade_slash" && !ribbon)
                        {
                            var variant = vfx.bladeSlashVariants?.FirstOrDefault(v => v != null && v.weapon == move.weapon && v.prefab);
                            var selected = variant != null ? variant.prefab : vfx.bladeSlash;
                            if (root.name != selected.name + "(Clone)")
                                throw new Exception("Wrong weapon slash: " + move.moveName + "/" + root.name);
                        }
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
                            if (Mathf.Abs(pair.SampleTime - time) > .0001f)
                                throw new Exception("VFX sampling changed the animation clock.");
                            int count = heard.Count;
                            vfx.AdvanceSequence(pair, time);
                            vfx.AdvanceSequence(pair, Mathf.Max(0, time - .25f));
                            if (count != heard.Count) throw new Exception("Duplicate VFX on repeated/reversed time.");
                            if (attacker == fighters[0] && !lethal && !mirrored &&
                                (move.moveName == "Light_1" && time == profile.cues.Last().seconds ||
                                 move.moveName == "Heavy_6" && (time == profile.cues.First().seconds || Mathf.Abs(time - 1.47f) < .01f) ||
                                 move.moveName == "Heavy_7" && profile.cues.First().seconds == time ||
                                 move.moveName == "Heavy_8" && Mathf.Abs(time - 1.07f) < .01f))
                                CaptureBattleVfx(camera, vfx, "Temp/FrankRetarget/Vfx/" + move.moveName +
                                    (move.moveName == "Heavy_6" && time == profile.cues.First().seconds ? "_swing" : "") + ".png",
                                    effectTimes, time);
                        }
                        int hits = profile.cues.Count(c => c.group == "light_hit" || c.group == "heavy_hit" || c.group == "stab_hit");
                        foreach (string skillCue in new[] { "skill_cast", "skill_shot" })
                            if (heard.Count(id => id == skillCue) != profile.cues.Count(c => c.group == skillCue))
                                throw new Exception("Missing skill phase: " + move.moveName + "/" + skillCue);
                        if (heard.Count(id => id.EndsWith("hit")) != hits ||
                            heard.Count(id => id.EndsWith("fall")) != profile.cues.Count(c => c.group == "body_fall"))
                            throw new Exception("Missing hit/landing effects: " + move.moveName);
                        if (heard.Count(id => id == "light_swing") != profile.cues.Count(c => c.group == "light_swing") ||
                            heard.Count(id => id == "thrust_swing") != profile.cues.Count(c => c.group == "thrust_swing") ||
                            heard.Count(id => id == "ground_impact") != profile.cues.Count(c => c.group == "body_fall") ||
                            heard.Count(id => id == "landing_text") != profile.cues.Count(c => c.finalLanding))
                            throw new Exception("Missing swing/thrust/ground/comic effects: " + move.moveName);
                        if (lethal && heard.Count(id => id == "knockout_fall") != 1)
                            throw new Exception("Knockout landing missing or repeated.");
                        if (move.weapon == TrumpWeaponManager.WeaponType.None && heard.Contains("blade_slash"))
                            throw new Exception("Unarmed move spawned a weapon slash.");
                        if (move.weapon != TrumpWeaponManager.WeaponType.None && heard.Count(id => id == "blade_slash") !=
                            profile.cues.Count(c => c.group == "blade_swing" || c.group == "heavy_swing"))
                            throw new Exception("Missing weapon sweep: " + move.moveName);
                        pair.Cancel();
                        int cancelledCount = heard.Count;
                        vfx.AdvanceSequence(pair, pair.Duration);
                        if (heard.Count != cancelledCount || vfx.ActiveEffectCount != 0)
                            throw new Exception("Cancellation did not stop VFX.");
                        report.AppendLine($"PASS {attacker.name} {move.moveName} lethal={lethal} mirrored={mirrored}: {heard.Count} effects.");
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
                var skippedPositions = new List<Vector3>();
                Action<string, GameObject> recordPosition = (id, root) => skippedPositions.Add(root.transform.position);
                vfx.EffectPlayed += recordPosition;
                if (!fighters[0].ExecuteAttack(skippedMove, fighters[1])) throw new Exception("Skipped-frame attack rejected.");
                var skippedPair = fighters[0].SourcePlayback;
                skippedPair.EvaluateAt(skippedPair.Duration);
                vfx.AdvanceSequence(skippedPair, skippedPair.Duration);
                var skippedProfile = vfx.timeline.FindMove(skippedMove);
                int expected = skippedProfile.cues.Length + skippedProfile.cues.Count(c => c.group == "body_fall") +
                    skippedProfile.cues.Count(c => c.finalLanding);
                if (vfx.PlayedEffectCount - beforeSkip != expected)
                    throw new Exception("Skipped-frame or muted-audio playback lost VFX cues.");
                if (Mathf.Abs(skippedPair.SampleTime - skippedPair.Duration) > .0001f)
                    throw new Exception("Skipped-frame VFX did not restore the displayed pose.");
                vfx.EffectPlayed -= recordPosition;
                skippedPair.Cancel();
                // Compare a whole-combo frame skip with individual cue sampling at the same positions.
                foreach (var fighter in fighters) fighter.ResetCombat();
                vfx.ResetForMatch();
                fighters[0].transform.position = Vector3.zero;
                fighters[1].transform.position = Vector3.right * skippedMove.attackRange;
                var exactPositions = new List<Vector3>();
                Action<string, GameObject> recordExact = (id, root) => exactPositions.Add(root.transform.position);
                vfx.EffectPlayed += recordExact;
                if (!fighters[0].ExecuteAttack(skippedMove, fighters[1])) throw new Exception("Exact-cue attack rejected.");
                var exactPair = fighters[0].SourcePlayback;
                foreach (var cue in skippedProfile.cues)
                {
                    exactPair.EvaluateAt(cue.seconds);
                    vfx.AdvanceSequence(exactPair, cue.seconds);
                }
                vfx.EffectPlayed -= recordExact;
                if (exactPositions.Count != skippedPositions.Count ||
                    exactPositions.Where((p, index) => Vector3.Distance(p, skippedPositions[index]) > .005f).Any())
                    throw new Exception("Frame skip spawned effects at a different pose from their authored cue.");
                exactPair.Cancel();
                game.battleSfx.enabled = true;
                report.AppendLine($"PASS whole-combo frame skip with audio disabled: {expected} effects; positions match exact cue sampling.");
                int poolLimit = vfx.maxInstances + (vfx.weaponTrails ? vfx.weaponTrails.maxTrails : 0);
                if (vfx.PooledEffectCount > poolLimit) throw new Exception("Effect pool exceeded its cap.");
                report.AppendLine($"PASS {cases} cases; pooled={vfx.PooledEffectCount}/{poolLimit}; shaders supported; duplicate/cancel checks passed.");
                File.WriteAllText("Temp/FrankRetarget/battle-vfx-validation.txt", report.ToString());
            }
            catch (Exception exception)
            {
                File.WriteAllText("Temp/FrankRetarget/battle-vfx-validation.txt", report + "FAIL " + exception);
                throw;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void CaptureBattleVfx(Camera camera, BattleVfxPlayer vfx, string path,
            Dictionary<GameObject, float> effectTimes, float seconds)
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
                foreach (var effect in effectTimes.Where(e => e.Key && e.Key.activeSelf))
                    foreach (var particles in effect.Key.GetComponentsInChildren<ParticleSystem>())
                        particles.Simulate(seconds - effect.Value + .08f, false, true, true);
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
