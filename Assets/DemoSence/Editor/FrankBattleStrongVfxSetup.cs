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
        const string StrongVfxAssets = "Assets/Vfx/Battle/Strong";
        const string StrongVfxReview = "GeneratedAssets/BattleStrongVfxReview";
        const string FlipbookElements = "Assets/Plugins/Vefects/Flipbook VFX/Elements/";
        static readonly string[][] StrongVfxCandidates = {
            new[] {
                AccentAssets + "/FocusedHeavy.prefab",
                AccentAssets + "/GreatSwordHeavyContact.prefab",
                PowerCandidates[0],
                CfxPrefabs + "Impacts/CFXR2 Hit (Contrast).prefab",
                NewVfxCandidates[6], NewVfxCandidates[8], NewVfxCandidates[10],
                NewVfxCandidates[0], NewVfxCandidates[1], NewVfxCandidates[3],
                FlipbookElements + "Impact/VFX_Impact_01_OS.prefab",
                FlipbookElements + "Impact/VFX_ImpactMagic_01_OS.prefab"
            },
            new[] {
                SlashPackAssets + "/BattleHovlGoldSlash.prefab",
                SlashPackAssets + "/BattleErbKatanaSlash.prefab",
                SlashPackAssets + "/BattleErbDaggerSlash.prefab",
                HovlSlashes + "Sword Slash 8.prefab",
                HovlSlashes + "Sword Slash 12.prefab",
                HovlSlashes + "Sword Slash 15.prefab",
                HovlSlashes + "Sword Slash 17.prefab",
                ErbSlashes + "New/Slash 4.prefab",
                ErbSlashes + "New/Slash 5.prefab",
                ErbSlashes + "New/Slash 7.prefab",
                FlipbookElements + "Slash/VFX_Slash_02_OS.prefab"
            },
            new[] {
                AccentAssets + "/FocusedGroundImpact.prefab",
                AccentAssets + "/SpreadingGroundSmoke.prefab",
                PowerCandidates[1],
                NewVfxCandidates[3], NewVfxCandidates[7], NewVfxCandidates[4],
                CfxPrefabs + "Misc/CFXR Smoke Poof Circle Flat.prefab",
                FlipbookElements + "Dust/VFX_Dust_Omni_Directional_01_OS.prefab",
                FlipbookElements + "Explosion/VFX_Explosion_03_OS.prefab"
            }
        };

        [MenuItem("Tools/Battle/VFX/Review stronger project effects")]
        public static void SurveyStrongerBattleVfx()
        {
            Directory.CreateDirectory(StrongVfxReview);
            if (!File.Exists(StrongVfxReview + "/BattleSceneBefore.unity.txt"))
                File.Copy(SfxScene, StrongVfxReview + "/BattleSceneBefore.unity.txt");
            File.Copy("Assets/Audio/Battle/BattleSfxBank.asset", StrongVfxReview + "/ContactBankBefore.asset.txt", true);
            var inventory = new StringBuilder("Project particle prefab inventory\n");
            foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var systems = prefab ? prefab.GetComponentsInChildren<ParticleSystem>(true) : Array.Empty<ParticleSystem>();
                if (systems.Length == 0) continue;
                inventory.AppendLine(path + "; systems=" + systems.Length + "; looping=" + systems.Count(p => p.main.loop));
            }
            File.WriteAllText(StrongVfxReview + "/Inventory.txt", inventory.ToString());
            var scene = EditorSceneManager.NewPreviewScene();
            var cameraRoot = new GameObject("Strong VFX comparison camera");
            SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            var camera = cameraRoot.AddComponent<Camera>(); camera.enabled = false; camera.scene = scene;
            camera.orthographic = true; camera.orthographicSize = 1.1f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.08f, .09f, .12f);
            var rt = RenderTexture.GetTemporary(240, 200, 24);
            var stamp = new Texture2D(240, 200, TextureFormat.RGB24, false);
            var oldActive = RenderTexture.active;
            var report = new StringBuilder("Each row is one candidate; columns are 0.025 / 0.075 / 0.14 / 0.22 seconds.\n");
            try
            {
                camera.targetTexture = rt;
                for (int kind = 0; kind < StrongVfxCandidates.Length; kind++)
                {
                    var paths = StrongVfxCandidates[kind];
                    var sheet = new Texture2D(960, 200 * paths.Length, TextureFormat.RGB24, false);
                    try
                    {
                        for (int i = 0; i < paths.Length; i++)
                        {
                            var source = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                            if (!source) { report.AppendLine($"{kind}/{i} MISSING {paths[i]}"); continue; }
                            var root = Object.Instantiate(source); SceneManager.MoveGameObjectToScene(root, scene);
                            try
                            {
                                PrepareNewVfxCopy(root);
                                bool battle = paths[i].StartsWith(VfxFolder, StringComparison.Ordinal);
                                camera.transform.position = kind == 2 ? new Vector3(0, 2.4f, 4) : new Vector3(0, 0, 4);
                                camera.transform.LookAt(Vector3.zero);
                                root.transform.SetPositionAndRotation(Vector3.zero,
                                    (kind == 2 ? Quaternion.identity : camera.transform.rotation) *
                                    (kind == 1 && !battle ? Quaternion.Euler(90, 0, 0) : Quaternion.identity) * source.transform.localRotation);
                                root.transform.localScale = battle ? source.transform.localScale : Vector3.one * .35f;
                                bool supported = true;
                                foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                                {
                                    if (paths[i].StartsWith(CfxPrefabs, StringComparison.Ordinal))
                                        renderer.sharedMaterials = renderer.sharedMaterials.Select(BattleEffectMaterial).ToArray();
                                    foreach (var material in renderer.sharedMaterials.Where(m => m))
                                        supported &= material.shader && material.shader.isSupported && !ShaderUtil.ShaderHasError(material.shader);
                                }
                                report.AppendLine($"{kind}/{i} {paths[i]}; scale={root.transform.localScale.x:F2}; supported={supported}");
                                foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
                                {
                                    report.AppendLine($"  {p.name} delay={p.main.startDelay.constantMax:F3} lifetime={p.main.startLifetime.constantMax:F3} size={p.main.startSize.constantMax:F3}");
                                    SingleBurst(p, kind == 2 ? .7f : .35f);
                                }
                                for (int column = 0; column < 4; column++)
                                {
                                    float age = new[] { .025f, .075f, .14f, .22f }[column];
                                    foreach (var p in root.GetComponentsInChildren<ParticleSystem>())
                                    {
                                        p.useAutoRandomSeed = false; p.randomSeed = 321;
                                        p.Simulate(age, false, true, false);
                                    }
                                    camera.Render(); RenderTexture.active = rt;
                                    stamp.ReadPixels(new Rect(0, 0, 240, 200), 0, 0); stamp.Apply();
                                    sheet.SetPixels(column * 240, (paths.Length - 1 - i) * 200, 240, 200, stamp.GetPixels());
                                }
                            }
                            finally { Object.DestroyImmediate(root); }
                        }
                        sheet.Apply(); File.WriteAllBytes(StrongVfxReview + "/Candidates" + kind + ".png", sheet.EncodeToPNG());
                    }
                    finally { Object.DestroyImmediate(sheet); }
                }
                File.WriteAllText(StrongVfxReview + "/Candidates.txt", report.ToString());
            }
            finally
            {
                RenderTexture.active = oldActive; camera.targetTexture = null;
                Object.DestroyImmediate(stamp); RenderTexture.ReleaseTemporary(rt);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
        static GameObject StrongContact(GameObject previous, string name, string sourcePath, float scale, bool heavy)
        {
            var root = Object.Instantiate(previous); root.name = name;
            try
            {
                // Keep each weapon's distinct, bright comic contact shape.
                foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                    if (child.name != "Contact core") Object.DestroyImmediate(child.gameObject);
                foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
                { var main = p.main; main.startSizeMultiplier *= 1.2f; }
                var burst = PowerBurst(root, sourcePath, scale, 1.65f);
                foreach (var child in burst.GetComponentsInChildren<Transform>(true))
                    if (child.name == "Arrow") child.gameObject.SetActive(false);
                foreach (var p in burst.GetComponentsInChildren<ParticleSystem>(true))
                {
                    SingleBurst(p, heavy ? .3f : .23f);
                    var main = p.main; main.startDelay = 0;
                }
                FillParticleMaterialSlots(root);
                return PrefabUtility.SaveAsPrefabAsset(root, StrongVfxAssets + "/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static GameObject StrongBase(string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AccentAssets + "/" + name + ".prefab");
            if (!prefab) throw new Exception("Missing original battle effect template: " + name);
            return prefab;
        }

        static GameObject StrongSlash(string name, string sourcePath, float scale, float speed)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (!source) throw new Exception("Missing selected stronger slash: " + sourcePath);
            var root = Object.Instantiate(source); root.name = name;
            try
            {
                bool battle = sourcePath.StartsWith(VfxFolder, StringComparison.Ordinal);
                PrepareNewVfxCopy(root);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = (battle ? Quaternion.identity : Quaternion.Euler(90, 0, 0)) * source.transform.localRotation;
                root.transform.localScale = Vector3.one * scale;
                foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    SingleBurst(p, .4f);
                    var main = p.main; main.simulationSpeed = speed; main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    var trails = p.trails; if (trails.enabled && trails.lifetime.constantMax > .15f)
                        trails.lifetimeMultiplier *= .15f / trails.lifetime.constantMax;
                }
                foreach (var r in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    r.sharedMaterials = r.sharedMaterials.Select(NewPackBattleMaterial).ToArray();
                FillParticleMaterialSlots(root);
                return PrefabUtility.SaveAsPrefabAsset(root, StrongVfxAssets + "/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("Tools/Battle/VFX/Apply reviewed stronger effects")]
        public static void InstallStrongerBattleVfx()
        {
            Directory.CreateDirectory(StrongVfxAssets);
            var rows = File.ReadAllLines(StrongVfxReview + "/Selection.csv").Skip(1)
                .Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Split(',')).ToArray();
            if (rows.Length != 7) throw new Exception("Select exactly seven weapon effects before installation.");
            string bankBefore = File.ReadAllText("Assets/Audio/Battle/BattleSfxBank.asset");
            var report = new StringBuilder();
            ComicScene((game, camera) =>
            {
                var vfx = game.battleVfx; Undo.RecordObject(vfx, "Use stronger readable battle VFX");
                // Always use the original templates so repeating this installation
                // cannot stack ground bursts or multiply the contact sizes again.
                vfx.lightHit = StrongContact(StrongBase("FocusedPunch"), "StrongPunch", NewVfxCandidates[0], .15f, false);
                vfx.heavyHit = StrongContact(StrongBase("FocusedHeavy"), "StrongHeavy", PowerCandidates[0], .23f, true);
                foreach (var row in rows)
                {
                    var weapon = (TrumpWeaponManager.WeaponType)Enum.Parse(typeof(TrumpWeaponManager.WeaponType), row[0]);
                    var impact = vfx.impactVariants.Single(v => v.weapon == weapon);
                    impact.light = StrongContact(StrongBase(weapon + "Contact"), "Strong" + weapon + "Contact", NewVfxCandidates[0], .14f, false);
                    impact.heavy = StrongContact(StrongBase(weapon + "HeavyContact"), "Strong" + weapon + "HeavyContact", row[1], ParseContactFloat(row[2]), true);
                    var slash = vfx.bladeSlashVariants.Single(v => v.weapon == weapon);
                    slash.prefab = StrongSlash("Strong" + weapon + "Slash", row[3], ParseContactFloat(row[4]), ParseContactFloat(row[5]));
                    report.AppendLine($"{weapon}: heavy {row[1]} at {row[2]}; slash {row[3]} at {row[4]}.");
                }
                vfx.bladeSlash = vfx.bladeSlashVariants.Single(v => v.weapon == TrumpWeaponManager.WeaponType.GreatSword).prefab;
                var ground = Object.Instantiate(StrongBase("FocusedGroundImpact")); ground.name = "StrongGroundImpact";
                try
                {
                    var burst = PowerBurst(ground, PowerCandidates[1], .22f, 1.5f);
                    foreach (var p in burst.GetComponentsInChildren<ParticleSystem>(true)) SingleBurst(p, .45f);
                    FillParticleMaterialSlots(ground);
                    vfx.groundImpact = PrefabUtility.SaveAsPrefabAsset(ground, StrongVfxAssets + "/StrongGroundImpact.prefab");
                }
                finally { Object.DestroyImmediate(ground); }
                var smoke = Object.Instantiate(StrongBase("SpreadingGroundSmoke")); smoke.name = "StrongSpreadingSmoke";
                try
                {
                    foreach (var p in smoke.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var main = p.main; main.startSizeMultiplier *= 1.15f;
                        var emission = p.emission; emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)20) });
                    }
                    vfx.landingDust = PrefabUtility.SaveAsPrefabAsset(smoke, StrongVfxAssets + "/StrongSpreadingSmoke.prefab");
                }
                finally { Object.DestroyImmediate(smoke); }
                vfx.effectScale = 1.28f; // Includes swing letters and SMASH, not just the new hits.
                EditorUtility.SetDirty(vfx);
                report.AppendLine("Global VFX scale 1.00 -> 1.28. Contact cores additionally +20%; ground smoke sprites additionally +15%.");
                if (vfx.skillVariants.Length != 0 || new[] { game.leftCombat, game.rightCombat }.Any(f => f.heavyCombatMoves.Length != 7))
                    throw new Exception("Unexpected change to removed skills or heavy move pools.");
            });
            if (bankBefore != File.ReadAllText("Assets/Audio/Battle/BattleSfxBank.asset"))
                throw new Exception("VFX installation changed calibrated contact cues.");
            File.WriteAllText(StrongVfxReview + "/Installation.txt", report + "Contact cue bank and camera settings unchanged.\n");
        }

        public static void CaptureStrongerBattleVfxBefore() => CaptureStrongerBattleVfx("Before");
        public static void CaptureStrongerBattleVfxAfter() => CaptureStrongerBattleVfx("After");

        static void CaptureStrongerBattleVfx(string stage)
        {
            Directory.CreateDirectory(StrongVfxReview + "/" + stage);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            BattleLightingRig lighting = null;
            var rt = RenderTexture.GetTemporary(1280, 720, 24);
            var stamp = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var oldActive = RenderTexture.active;
            var report = new StringBuilder();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GameManager>(true)).Single();
                var camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.targetTexture = rt;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Canvas>(true)))
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters) fighter.Initialize();
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig(); game.uiManager.SetTurnNumber(1);
                var director = camera.GetComponent<FrankCinematicCamera>();
                foreach (var attacker in fighters)
                foreach (var move in attacker.heavyCombatMoves)
                foreach (string kind in new[] { "Slash", "Hit", "Ground" })
                {
                    var receiver = fighters.Single(f => f != attacker);
                    foreach (var f in fighters) f.ResetCombat(); game.battleVfx.ResetForMatch();
                    var profile = game.battleVfx.timeline.FindMove(move);
                    var cue = kind == "Slash" ? profile.cues.First(c => c.group == "blade_swing" || c.group == "heavy_swing") :
                        kind == "Ground" ? profile.cues.Last(c => c.group == "body_fall" || c.group == "knockout_fall") :
                        profile.cues.First(c => c.hasContactPoint);
                    bool reverse = attacker == fighters[1];
                    attacker.transform.position = new Vector3(reverse ? .5f : -.5f, 0, 0);
                    receiver.transform.position = attacker.transform.position + Vector3.right * (reverse ? -move.attackRange : move.attackRange);
                    var effects = new Dictionary<GameObject, float>();
                    Action<string, GameObject> observe = (id, root) => effects[root] = attacker.SourcePlayback.SampleTime;
                    game.battleVfx.EffectPlayed += observe;
                    try
                    {
                        if (!attacker.ExecuteAttack(move, receiver)) throw new Exception("Strong effect preview rejected.");
                        var pair = attacker.SourcePlayback; pair.EvaluateAt(cue.seconds); game.battleVfx.AdvanceSequence(pair, cue.seconds);
                        foreach (var effect in effects)
                        foreach (var p in effect.Key.GetComponentsInChildren<ParticleSystem>())
                        {
                            p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                            p.useAutoRandomSeed = false; p.randomSeed = 321;
                            p.Simulate(cue.seconds - effect.Value + (kind == "Ground" ? .18f : .05f), false, true, false);
                        }
                        lighting.RefreshLighting(.22f); director.ResetView(); director.Apply(0, true);
                        Canvas.ForceUpdateCanvases();
                        string path = StrongVfxReview + "/" + stage + "/" + attacker.name + "_" + move.moveName + "_" + kind + ".png";
                        CaptureStrongPose(camera, fighters, pair, stamp, rt);
                        File.WriteAllBytes(path, stamp.EncodeToPNG()); report.AppendLine("PASS " + path);
                        pair.Cancel();
                    }
                    finally { game.battleVfx.EffectPlayed -= observe; }
                }
                File.WriteAllText(StrongVfxReview + "/" + stage + "Rendering.txt", report.ToString());
            }
            finally
            {
                if (lighting) lighting.ShutdownRig(); RenderTexture.active = oldActive;
                Object.DestroyImmediate(stamp); RenderTexture.ReleaseTemporary(rt); EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void CaptureStrongPose(Camera camera, CharacterCombat[] fighters, FrankBattlePairPlayback pair, Texture2D stamp, RenderTexture rt)
        {
            var skins = fighters.SelectMany(f => f.Animator.GetComponentsInChildren<SkinnedMeshRenderer>())
                .Concat(pair.AttackerActor.GetComponentsInChildren<SkinnedMeshRenderer>()).Where(r => r.enabled && r.sharedMesh && r.bones.Length > 0).Distinct().ToArray();
            var clones = new List<GameObject>(); var meshes = new List<Mesh>();
            try
            {
                foreach (var skin in skins)
                {
                    var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave }; skin.BakeMesh(mesh, true); meshes.Add(mesh);
                    var clone = new GameObject("Strong VFX pose mesh"); SceneManager.MoveGameObjectToScene(clone, camera.gameObject.scene); clones.Add(clone);
                    clone.transform.SetPositionAndRotation(skin.transform.position, skin.transform.rotation); clone.transform.localScale = skin.transform.lossyScale;
                    clone.AddComponent<MeshFilter>().sharedMesh = mesh; clone.AddComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials; skin.enabled = false;
                }
                camera.Render(); RenderTexture.active = rt;
                stamp.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); stamp.Apply();
            }
            finally
            {
                foreach (var skin in skins) if (skin) skin.enabled = true;
                foreach (var clone in clones) Object.DestroyImmediate(clone); foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
            }
        }
    }
}
