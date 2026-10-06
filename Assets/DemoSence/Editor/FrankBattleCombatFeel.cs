using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public const string FeelReview = "GeneratedAssets/CombatFeelVideoReview";

        [MenuItem("Tools/Battle/Apply combat feel from video")]
        public static void InstallVideoCombatFeel()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new Exception("Apply combat feel in Edit mode.");
            var scene = SceneManager.GetSceneByPath(SfxScene);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(SfxScene, OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                var hold = game.GetComponent<BattleImpactFeedback>();
                var shake = camera.GetComponent<BattleCameraShake>();
                if (!hold || !shake || !game.battleVfx) throw new Exception("Existing contact feedback bindings are missing.");
                var recoil = game.GetComponent<BattleKnockbackFeedback>();
                if (!recoil) recoil = Undo.AddComponent<BattleKnockbackFeedback>(game.gameObject);
                Undo.RecordObjects(new Object[] { hold, shake, recoil }, "Apply video combat feel");
                hold.lightHold = .04f; hold.heavyHold = .08f; hold.groundHold = .055f; hold.finishingHold = .10f;
                shake.lightStrength = .014f; shake.heavyStrength = .055f; shake.groundStrength = .04f;
                shake.lightDuration = .09f; shake.duration = .18f;
                recoil.vfx = game.battleVfx; recoil.lightDistance = .055f; recoil.heavyDistance = .10f;
                recoil.lightSeconds = .12f; recoil.heavySeconds = .18f;
                EditorUtility.SetDirty(hold); EditorUtility.SetDirty(shake); EditorUtility.SetDirty(recoil);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save BattleScene.");
                Directory.CreateDirectory(FeelReview);
                File.WriteAllText(FeelReview + "/Install.txt", "Applied shared contact feedback, light/heavy/finisher hit-stop, light camera shake and bounded visual recoil.\n");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        static bool FeelImpactCue(BattleSfxBank.Cue c) => c.group == "light_hit" || c.group == "heavy_hit" ||
            c.group == "stab_hit" || c.group == "body_fall" || c.group == "knockout_fall";

        [MenuItem("Tools/Battle/Validate video combat feel")]
        public static void ValidateVideoCombatFeel()
        {
            Directory.CreateDirectory(FeelReview);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var report = new StringBuilder();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                var recoil = game.GetComponent<BattleKnockbackFeedback>();
                var hold = game.GetComponent<BattleImpactFeedback>();
                var shake = camera.GetComponent<BattleCameraShake>();
                if (!recoil || recoil.vfx != game.battleVfx || !hold || !shake || shake.lightStrength <= 0)
                    throw new Exception("Saved video feedback bindings/settings are missing.");
                recoil.Bind(); hold.Bind(); shake.Bind();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters) if (!fighter.Initialize()) throw new Exception("Fighter initialization failed.");
                int cases = 0, contacts = 0;
                foreach (var attacker in fighters)
                foreach (var move in attacker.lightCombatMoves.Concat(attacker.heavyCombatMoves))
                foreach (bool lethal in new[] { false, true })
                {
                    foreach (var fighter in fighters) fighter.ResetCombat();
                    game.battleVfx.ResetForMatch();
                    var receiver = fighters.Single(f => f != attacker);
                    attacker.transform.position = Vector3.zero;
                    receiver.transform.position = (attacker == game.leftCombat ? Vector3.right : Vector3.left) * move.attackRange;
                    int initialHold = hold.HitStopCount, initialShake = shake.ShakeCount, observed = 0;
                    Action<BattleVfxPlayer.Impact> observe = impact =>
                    {
                        if (impact.receiver != receiver || impact.attacker != attacker || impact.move != move ||
                            !float.IsFinite(impact.position.x) || !float.IsFinite(impact.position.y) ||
                            Mathf.Abs(impact.playback.SampleTime - impact.seconds) > .0001f)
                            throw new Exception("Impact is not bound to its exact receiver/contact pose.");
                        observed++;
                    };
                    game.battleVfx.ContactOccurred += observe;
                    try
                    {
                        if (!attacker.ExecuteAttack(move, receiver, lethal)) throw new Exception("Attack rejected: " + move.moveName);
                        var pair = attacker.SourcePlayback;
                        var profile = game.battleVfx.timeline.FindMove(move);
                        var impactCues = profile.cues.Where(FeelImpactCue).ToArray();
                        if (impactCues.Length == 0) throw new Exception("No impact cues: " + move.moveName);
                        pair.AdvanceTo(Mathf.Max(0, impactCues[0].seconds - .001f));
                        if (observed != 0 || initialHold != hold.HitStopCount || initialShake != shake.ShakeCount)
                            throw new Exception("Contact feedback began during windup.");
                        foreach (var cue in impactCues)
                        {
                            pair.AdvanceTo(cue.seconds);
                            var hips = receiver.Animator.GetBoneTransform(HumanBodyBones.Hips);
                            Vector3 baseHips = hips.position, root = receiver.Animator.transform.position;
                            recoil.AdvanceRecoil(.025f); recoil.ApplyRecoil();
                            Vector3 first = hips.position;
                            recoil.ApplyRecoil();
                            if (Vector3.Distance(first, hips.position) > .00001f ||
                                Vector3.Distance(root, receiver.Animator.transform.position) > .00001f ||
                                Mathf.Abs(recoil.CurrentOffset.y) > .00001f || Mathf.Abs(recoil.CurrentOffset.z) > .00001f ||
                                Mathf.Abs(recoil.CurrentOffset.x) > .10001f)
                                throw new Exception("Recoil accumulated, moved the gameplay root or exceeded its bound.");
                            recoil.RestoreOffset();
                            if (Vector3.Distance(baseHips, hips.position) > .00001f) throw new Exception("Recoil left bone-position drift.");
                        }
                        pair.AdvanceTo(pair.Duration);
                        pair.AdvanceTo(pair.Duration);
                        pair.EvaluateAt(0); pair.AdvanceTo(.001f);
                        if (observed != impactCues.Length || hold.HitStopCount - initialHold != observed || shake.ShakeCount - initialShake != observed)
                            throw new Exception("Contacts were dropped or replayed: " + move.moveName);
                        if (profile.cues.Any(c => c.damageOnLanding) && recoil.IsRecoiling)
                            throw new Exception("Procedural recoil interfered with an authored throw.");
                        pair.Cancel();
                        if (recoil.IsRecoiling || recoil.CurrentOffset != Vector3.zero || hold.IsHolding || shake.IsShaking)
                            throw new Exception("Cancellation left feedback active.");
                        contacts += observed; cases++;
                    }
                    finally { game.battleVfx.ContactOccurred -= observe; }
                }

                // The same physical feedback must work even when no visual prefab can spawn.
                var vfx = game.battleVfx;
                vfx.lightHit = vfx.heavyHit = vfx.landingDust = vfx.bladeSlash = null;
                vfx.lightSwing = vfx.thrustSwing = vfx.groundImpact = vfx.landingText = null;
                vfx.impactVariants = Array.Empty<BattleVfxPlayer.WeaponImpactVariant>();
                vfx.bladeSlashVariants = Array.Empty<BattleVfxPlayer.WeaponSlashVariant>();
                vfx.skillVariants = Array.Empty<BattleVfxPlayer.SkillVariant>();
                foreach (var fighter in fighters) fighter.ResetCombat();
                vfx.ResetForMatch();
                var testMove = game.leftCombat.heavyCombatMoves.First(m => m.skill == BattleSkill.None);
                game.leftCombat.transform.position = Vector3.zero;
                game.rightCombat.transform.position = Vector3.right * testMove.attackRange;
                int heldBefore = hold.HitStopCount, shakenBefore = shake.ShakeCount, visualsBefore = vfx.PlayedEffectCount;
                if (!game.leftCombat.ExecuteAttack(testMove, game.rightCombat)) throw new Exception("Prefab-free attack rejected.");
                var testPair = game.leftCombat.SourcePlayback;
                testPair.AdvanceTo(testPair.Duration);
                int expected = vfx.timeline.FindMove(testMove).cues.Count(FeelImpactCue);
                if (hold.HitStopCount - heldBefore != expected || shake.ShakeCount - shakenBefore != expected ||
                    vfx.PlayedEffectCount != visualsBefore || vfx.ActiveEffectCount != 0)
                    throw new Exception("Contact feedback still depends on a spawned effect.");
                testPair.Cancel();
                report.AppendLine($"PASS {cases} move/role/lethal cases, {contacts} exact-pose contacts; windup quiet; duplicate/backwards evaluation does not replay feedback.");
                report.AppendLine("PASS visual recoil bounded to 10 cm, no Y/Z displacement, no gameplay-root movement, repeated application/removal leaves no bone drift; throws preserved.");
                report.AppendLine("PASS missing visual prefabs retain all contact holds and shakes; cancel/reset clears feedback.");
                File.WriteAllText(FeelReview + "/Validation.txt", report.ToString());
            }
            catch (Exception error) { File.WriteAllText(FeelReview + "/Validation.txt", report + "FAIL " + error); throw; }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [MenuItem("Tools/Battle/Capture video combat feel")]
        public static void CaptureVideoCombatFeel()
        {
            Directory.CreateDirectory(FeelReview);
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
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig();
                var recoil = game.GetComponent<BattleKnockbackFeedback>(); recoil.Bind();
                var hold = game.GetComponent<BattleImpactFeedback>(); hold.Bind();
                var shake = camera.GetComponent<BattleCameraShake>(); shake.Bind();
                var director = camera.GetComponent<FrankCinematicCamera>();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters) fighter.Initialize();
                foreach (bool heavy in new[] { false, true })
                {
                    foreach (var fighter in fighters) fighter.ResetCombat();
                    game.battleVfx.ResetForMatch();
                    var move = (heavy ? game.leftCombat.heavyCombatMoves : game.leftCombat.lightCombatMoves)
                        .First(m => m.skill == BattleSkill.None && (!heavy ||
                            !game.battleVfx.timeline.FindMove(m).cues.Any(c => c.damageOnLanding) &&
                            game.battleVfx.timeline.FindMove(m).cues.Any(c => c.group == "heavy_hit")));
                    game.leftCombat.transform.position = Vector3.zero;
                    game.rightCombat.transform.position = Vector3.right * move.attackRange;
                    var spawned = new Dictionary<GameObject, float>();
                    Action<string, GameObject> observe = (cue, effect) => spawned[effect] = game.leftCombat.SourcePlayback.SampleTime;
                    game.battleVfx.EffectPlayed += observe;
                    FrankBattlePairPlayback pair;
                    float time;
                    try
                    {
                        if (!game.leftCombat.ExecuteAttack(move, game.rightCombat)) throw new Exception("Capture attack rejected.");
                        pair = game.leftCombat.SourcePlayback;
                        time = game.battleVfx.timeline.FindMove(move).cues.First(c => heavy ? c.group == "heavy_hit" : FeelImpactCue(c)).seconds;
                        pair.AdvanceTo(time); pair.EvaluateAt(time + .03f);
                        foreach (var effect in spawned.Where(e => e.Key && e.Key.activeSelf))
                        foreach (var particle in effect.Key.GetComponentsInChildren<ParticleSystem>())
                        {
                            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                            particle.useAutoRandomSeed = false; particle.randomSeed = 321;
                            particle.Simulate(time - effect.Value + .03f, false, true, true);
                        }
                    }
                    finally { game.battleVfx.EffectPlayed -= observe; }
                    recoil.AdvanceRecoil(.03f); recoil.ApplyRecoil();
                    lighting.RefreshLighting(.03f); hold.AdvanceFeedback(.03f);
                    director.ResetView(); director.Apply(0, true);
                    shake.AdvanceShake(.03f); shake.ApplyShake();
                    Canvas.ForceUpdateCanvases(); hold.RefreshFlashProjection(); Canvas.ForceUpdateCanvases();
                    CaptureBattleCamera(camera, FeelReview + (heavy ? "/HeavyContact.png" : "/LightContact.png"));
                    recoil.RestoreOffset(); pair.Cancel(); shake.ResetShake();
                }
            }
            finally
            {
                if (lighting) lighting.ShutdownRig(); if (camera) camera.targetTexture = null;
                Object.DestroyImmediate(target); EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [MenuItem("Tools/Battle/Validate video combat feel in Play Mode")]
        public static void BeginVideoCombatFeelPlayValidation() => CombatFeelPlayValidation.Begin();
    }
}
