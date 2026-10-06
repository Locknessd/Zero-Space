using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CartoonFX;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string NewVfxReview = "GeneratedAssets/BattleNewVfxReview";
        const string WhiteMagePack = "Assets/Hovl Studio/White mage spells";
        const string ArcherPack = "Assets/Hovl Studio/Archer skills pack";

        static readonly string[] NewVfxCandidates = {
            WhiteMagePack + "/Prefabs/Projectiles/Light hit.prefab",
            WhiteMagePack + "/Prefabs/Projectiles/Light Flash.prefab",
            WhiteMagePack + "/Prefabs/Magic rain/Magic rain hit.prefab",
            WhiteMagePack + "/Prefabs/Magic rain/Magic rain hit 2.prefab",
            WhiteMagePack + "/Prefabs/Front attack/Ground shine.prefab",
            ArcherPack + "/Prefabs/Simple shots/Arrow hit.prefab",
            ArcherPack + "/Prefabs/Light arrow/ArrowHit.prefab",
            ArcherPack + "/Prefabs/Light arrow/ShockWave.prefab",
            ArcherPack + "/Prefabs/Arrow discharge/Discharge hit.prefab",
            ArcherPack + "/Prefabs/Arrow rain/ArrowRain Hit.prefab",
            ArcherPack + "/Prefabs/Fire arrow/FireArrow hit.prefab",
            ArcherPack + "/Prefabs/Light arrow/Trails.prefab"
        };

        [MenuItem("Tools/Battle/Survey White Mage and Archer VFX")]
        public static void SurveyBattleNewVfx()
        {
            Directory.CreateDirectory(NewVfxReview);
            var report = new StringBuilder();
            foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { WhiteMagePack, ArcherPack })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(p => p))
            {
                var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!root) { report.AppendLine("MISSING " + path); continue; }
                var particles = root.GetComponentsInChildren<ParticleSystem>(true);
                report.AppendLine(path + ": systems=" + particles.Length + "; scale=" + root.transform.localScale +
                    "; scripts=" + string.Join(",", root.GetComponentsInChildren<MonoBehaviour>(true).Select(c => c ? c.GetType().Name : "MISSING")) +
                    "; audio=" + root.GetComponentsInChildren<AudioSource>(true).Length);
                foreach (var p in particles)
                    report.AppendLine("  " + AnimationUtility.CalculateTransformPath(p.transform, root.transform) +
                        "; active=" + p.gameObject.activeSelf + "; loop=" + p.main.loop + "; duration=" + p.main.duration +
                        "; delay=" + p.main.startDelay.constantMax + "; lifetime=" + p.main.startLifetime.constantMax +
                        "; size=" + p.main.startSize.constantMax + "; max=" + p.main.maxParticles);
                foreach (var m in root.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m).Distinct())
                    report.AppendLine("  MATERIAL " + AssetDatabase.GetAssetPath(m) + "; shader=" + (m.shader ? m.shader.name : "MISSING") +
                        "; supported=" + (m.shader && m.shader.isSupported) + "; errors=" + (m.shader && ShaderUtil.ShaderHasError(m.shader)));
            }
            File.WriteAllText(NewVfxReview + "/PackSurvey.txt", report.ToString());
            CaptureNewVfxCandidates();
        }

        static void PrepareNewVfxCopy(GameObject root)
        {
            // The battle cue player owns playback, audio and lifetime; pack demo scripts must not spawn projectiles.
            foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true))
                if (script) Object.DestroyImmediate(script);
            foreach (var audio in root.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(audio);
            foreach (var light in root.GetComponentsInChildren<Light>(true)) light.enabled = false;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
                if (child.name.IndexOf("distortion", StringComparison.OrdinalIgnoreCase) >= 0)
                    child.gameObject.SetActive(false);
            foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = p.main;
                main.playOnAwake = false;
                main.loop = false;
                main.stopAction = ParticleSystemStopAction.None;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }

        static GameObject NewPackContactEffect(bool heavy)
        {
            var root = new GameObject(heavy ? "ArcherHeavyHit" : "MageLightHit");
            try
            {
                var burst = AddPackBurst(root, heavy ? NewVfxCandidates[6] : NewVfxCandidates[0],
                    heavy ? .4f : .43f, 1.35f);
                if (heavy)
                    foreach (var child in burst.GetComponentsInChildren<Transform>(true))
                        if (child.name == "Arrow") child.gameObject.SetActive(false);
                string textPath = CfxPrefabs + "Texts/CFXR _" + (heavy ? "WHAM" : "POW") + "_.prefab";
                var text = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(textPath), root.transform, false);
                text.name = heavy ? "Comic WHAM" : "Comic POW";
                text.transform.localPosition = new Vector3(.06f, .14f, -.04f);
                text.transform.localScale = Vector3.one * (heavy ? .18f : .16f);
                // The letters are already baked into child particles; freeze their tuned pool settings.
                foreach (var label in text.GetComponentsInChildren<CFXR_ParticleText>(true))
                    Object.DestroyImmediate(label);
                foreach (var p in text.GetComponentsInChildren<ParticleSystem>(true))
                {
                    p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                    var main = p.main;
                    main.playOnAwake = main.loop = false;
                    main.stopAction = ParticleSystemStopAction.None;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.simulationSpeed = 1.6f;
                }
                foreach (var effect in text.GetComponentsInChildren<CFXR_Effect>(true))
                {
                    effect.clearBehavior = CFXR_Effect.ClearBehavior.None;
                    if (effect.cameraShake != null) effect.cameraShake.enabled = false;
                    effect.animatedLights = Array.Empty<CFXR_Effect.AnimatedLight>();
                }
                foreach (var light in text.GetComponentsInChildren<Light>(true)) light.enabled = false;
                foreach (var renderer in text.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(BattleEffectMaterial).ToArray();
                return PrefabUtility.SaveAsPrefabAsset(root, VfxFolder + "/" + root.name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static GameObject NewPackGroundEffect()
        {
            var root = new GameObject("MageGroundImpact");
            try
            {
                AddPackBurst(root, NewVfxCandidates[3], .5f, 1.7f);
                return PrefabUtility.SaveAsPrefabAsset(root, VfxFolder + "/" + root.name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static GameObject AddPackBurst(GameObject parent, string path, float scale, float speed)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!source) throw new Exception("Missing selected VFX: " + path);
            var burst = Object.Instantiate(source, parent.transform, false);
            PrepareNewVfxCopy(burst);
            burst.transform.localPosition = Vector3.zero;
            burst.transform.localScale = Vector3.one * scale;
            foreach (var p in burst.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = p.main;
                main.simulationSpeed = speed;
                main.maxParticles = Mathf.Min(main.maxParticles, 96);
                // Continuous orbit trails from the spell stop shortly after the melee contact.
                if (p.emission.rateOverTime.constantMax > 0)
                    main.duration = Mathf.Min(main.duration, .22f);
            }
            foreach (var renderer in burst.GetComponentsInChildren<ParticleSystemRenderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(NewPackBattleMaterial).ToArray();
            return burst;
        }

        static Material NewPackBattleMaterial(Material source)
        {
            if (!source) return null;
            if (!source.shader || !source.shader.isSupported || ShaderUtil.ShaderHasError(source.shader))
                throw new Exception("Unsupported selected VFX material: " + AssetDatabase.GetAssetPath(source));
            string folder = VfxFolder + "/Materials/NewPacks";
            Directory.CreateDirectory(folder);
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
            string path = folder + "/" + source.name + "_" + guid.Substring(0, 8) + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
            else material.CopyPropertiesFromMaterial(source);
            material.DisableKeyword("SOFTPARTICLES_ON");
            EditorUtility.SetDirty(material);
            return material;
        }

        [MenuItem("Tools/Battle/Apply selected White Mage and Archer VFX")]
        public static void InstallBattleNewVfx()
        {
            Directory.CreateDirectory(NewVfxReview);
            Directory.CreateDirectory(VfxFolder + "/Materials");
            string before = NewVfxReview + "/BattleSceneBefore.unity.txt";
            if (!File.Exists(before)) File.Copy(SfxScene, before);
            var light = NewPackContactEffect(false);
            var heavy = NewPackContactEffect(true);
            var ground = NewPackGroundEffect();
            var scene = SceneManager.GetSceneByPath(SfxScene);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(SfxScene, OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var vfx = game.battleVfx;
                if (!vfx) throw new Exception("Battle VFX player is missing.");
                Undo.RecordObject(vfx, "Update White Mage and Archer battle effects");
                vfx.lightHit = light;
                vfx.heavyHit = heavy;
                vfx.groundImpact = ground;
                EditorUtility.SetDirty(vfx);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save updated BattleScene.");
                File.WriteAllText(NewVfxReview + "/Selection.txt",
                    "Inspected 66 pack prefabs and rendered 12 contact/ground candidates at three ages.\n" +
                    "Light hit: White Mage/Projectiles/Light hit, scale .43, simulation speed 1.35 + comic POW.\n" +
                    "Heavy hit: Archer/Light arrow/ArrowHit, scale .40, speed 1.35 + comic WHAM; arrow mesh disabled.\n" +
                    "Ground impact: White Mage/Magic rain/Magic rain hit 2, scale .50, speed 1.70; golden floor burst.\n" +
                    "WHOOSH, native sword sweeps, landing dust and SMASH remain. Existing authored sound/animation cues retained.\n" +
                    "Battle-only material copies; pack audio/demo scripts removed; distortion disabled; loops off; 96 particles/system cap.\n");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        [MenuItem("Tools/Battle/Validate updated pack VFX")]
        public static void ValidateBattleNewVfx()
        {
            ValidateBattleVfx();
            File.Copy("Temp/FrankRetarget/battle-vfx-validation.txt", NewVfxReview + "/TimelineValidation.txt", true);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var report = new StringBuilder();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var vfx = game.battleVfx;
                if (vfx.lightHit.name != "MageLightHit" || vfx.heavyHit.name != "ArcherHeavyHit" || vfx.groundImpact.name != "MageGroundImpact")
                    throw new Exception("BattleScene did not retain the selected pack effects.");
                foreach (var prefab in new[] { vfx.lightHit, vfx.heavyHit, vfx.groundImpact })
                {
                    if (prefab.GetComponentsInChildren<AudioSource>(true).Length != 0)
                        throw new Exception("Pack audio bypasses the battle mixer.");
                    foreach (var script in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                        if (!script || !(script is CFXR_Effect)) throw new Exception("Unexpected demo script in " + prefab.name);
                    foreach (var child in prefab.GetComponentsInChildren<Transform>(true))
                        if ((child.name.IndexOf("distortion", StringComparison.OrdinalIgnoreCase) >= 0 || child.name == "Arrow") && child.gameObject.activeSelf)
                            throw new Exception("Distortion/projectile layer still enabled: " + child.name);
                    report.AppendLine("PASS " + prefab.name + ": scene binding, scripts, audio and distortion/projectile layers.");
                }
                CaptureUpdatedVfxInBattle(game, report);
                File.WriteAllText(NewVfxReview + "/RenderValidation.txt", report.ToString());
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void CaptureUpdatedVfxInBattle(GameManager game, StringBuilder report)
        {
            var camera = game.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
            var director = camera.GetComponent<FrankCinematicCamera>();
            camera.scene = game.gameObject.scene;
            foreach (var canvas in game.gameObject.scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
            { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
            game.leftCombat.Initialize(); game.rightCombat.Initialize();
            var target = new RenderTexture(1280, 720, 24);
            var texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            var previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                foreach (string moveName in new[] { "Light_1", "Heavy_6", "Heavy_8" })
                foreach (bool ground in new[] { false, true })
                {
                    var attacker = game.leftCombat;
                    var receiver = game.rightCombat;
                    var move = attacker.lightCombatMoves.Concat(attacker.heavyCombatMoves).Single(m => m.moveName == moveName);
                    var profile = game.battleVfx.timeline.FindMove(move);
                    var cue = ground ? profile.cues.LastOrDefault(c => c.finalLanding) :
                        profile.cues.FirstOrDefault(c => c.group.EndsWith("hit", StringComparison.Ordinal));
                    if (cue == null) continue;
                    attacker.ResetCombat(); receiver.ResetCombat(); game.battleVfx.ResetForMatch();
                    attacker.transform.position = new Vector3(-.5f, 0, 0);
                    receiver.transform.position = attacker.transform.position + Vector3.right * move.attackRange;
                    var effects = new Dictionary<GameObject, float>();
                    Action<string, GameObject> observe = (id, root) => effects[root] = attacker.SourcePlayback.SampleTime;
                    game.battleVfx.EffectPlayed += observe;
                    try
                    {
                        if (!attacker.ExecuteAttack(move, receiver, ground)) throw new Exception("Preview move rejected.");
                        var pair = attacker.SourcePlayback;
                        pair.EvaluateAt(cue.seconds);
                        game.battleVfx.AdvanceSequence(pair, cue.seconds);
                        foreach (float age in new[] { .045f, .12f, .28f })
                        {
                            pair.EvaluateAt(Mathf.Min(pair.Duration, cue.seconds + age));
                            if (director) director.Apply(0, true);
                            foreach (var effect in effects.Where(e => e.Key && e.Key.activeSelf))
                            foreach (var particles in effect.Key.GetComponentsInChildren<ParticleSystem>())
                            {
                                particles.useAutoRandomSeed = false; particles.randomSeed = 321;
                                particles.Simulate(cue.seconds - effect.Value + age, false, true, true);
                            }
                            Canvas.ForceUpdateCanvases();
                            camera.Render();
                            RenderTexture.active = target;
                            texture.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                            texture.Apply();
                            int magenta = texture.GetPixels32().Count(c => c.r > 235 && c.g < 25 && c.b > 235);
                            string imagePath = NewVfxReview + "/" + moveName + (ground ? "_Ground_" : "_Hit_") + age.ToString("F3", CultureInfo.InvariantCulture) + ".png";
                            File.WriteAllBytes(imagePath, texture.EncodeToPNG());
                            report.AppendLine("PASS " + imagePath + ": rendered at authored cue pose; error-magenta pixels=" + magenta);
                            if (magenta > 0) throw new Exception("Magenta effect pixels in battle capture.");
                        }
                        pair.Cancel();
                    }
                    finally { game.battleVfx.EffectPlayed -= observe; }
                }
            }
            finally
            {
                RenderTexture.active = previousActive;
                camera.targetTexture = null;
                Object.DestroyImmediate(texture); Object.DestroyImmediate(target);
            }
        }

        static void CaptureNewVfxCandidates()
        {
            string folder = NewVfxReview + "/Candidates";
            Directory.CreateDirectory(folder);
            var scene = EditorSceneManager.NewPreviewScene();
            var cameraRoot = new GameObject("New pack particle preview");
            SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            var camera = cameraRoot.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.orthographic = true;
            camera.orthographicSize = 1.3f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.065f, .07f, .09f);
            var target = new RenderTexture(480, 360, 24);
            var texture = new Texture2D(480, 360, TextureFormat.RGB24, false);
            var previousActive = RenderTexture.active;
            var report = new StringBuilder();
            try
            {
                camera.targetTexture = target;
                for (int i = 0; i < NewVfxCandidates.Length; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NewVfxCandidates[i]);
                    if (!prefab) throw new Exception("Missing new VFX " + NewVfxCandidates[i]);
                    var root = Object.Instantiate(prefab);
                    SceneManager.MoveGameObjectToScene(root, scene);
                    try
                    {
                        PrepareNewVfxCopy(root);
                        bool ground = i == 3 || i == 4 || i == 7 || i == 9;
                        camera.transform.position = ground ? new Vector3(0, 2, 4) : new Vector3(0, 0, 4);
                        camera.transform.LookAt(Vector3.zero);
                        root.transform.SetPositionAndRotation(Vector3.zero, ground ? prefab.transform.localRotation : camera.transform.rotation * prefab.transform.localRotation);
                        root.transform.localScale = Vector3.one * .25f;
                        foreach (float seconds in new[] { .06f, .16f, .32f })
                        {
                            foreach (var particles in root.GetComponentsInChildren<ParticleSystem>())
                            {
                                particles.useAutoRandomSeed = false;
                                particles.randomSeed = 123;
                                particles.Simulate(seconds, false, true, true);
                            }
                            camera.Render();
                            RenderTexture.active = target;
                            texture.ReadPixels(new Rect(0, 0, 480, 360), 0, 0);
                            texture.Apply();
                            string filename = i.ToString("D2") + "_" + seconds.ToString("F2", CultureInfo.InvariantCulture) + ".png";
                            File.WriteAllBytes(folder + "/" + filename, texture.EncodeToPNG());
                            int magenta = texture.GetPixels32().Count(c => c.r > 235 && c.g < 25 && c.b > 235);
                            report.AppendLine(filename + ": " + NewVfxCandidates[i] + "; magenta=" + magenta);
                        }
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                File.WriteAllText(folder + "/Index.txt", report.ToString());
            }
            finally
            {
                RenderTexture.active = previousActive;
                Object.DestroyImmediate(texture);
                Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
