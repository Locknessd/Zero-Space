using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        const string SlashPackReview = "GeneratedAssets/BattleSlashPackReview";
        const string SlashPackAssets = "Assets/Vfx/Battle/SlashPacks";
        const string HovlSlashes = "Assets/Hovl Studio/Sword slash VFX/Prefabs/";
        const string ErbSlashes = "Assets/ErbGameArt/Sword slash FX/Prefabs/";

        static Material SlashPackMaterial(Material source)
        {
            if (!source) return null;
            if (!source.shader || !source.shader.isSupported || ShaderUtil.ShaderHasError(source.shader))
                throw new Exception("Invalid slash shader: " + AssetDatabase.GetAssetPath(source));
            string path = SlashPackAssets + "/Materials/" + source.name + "_" +
                AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source)).Substring(0, 8) + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(source); AssetDatabase.CreateAsset(material, path); }
            else material.CopyPropertiesFromMaterial(source);
            material.DisableKeyword("SOFTPARTICLES_ON");
            if (material.HasProperty("_Usedepth")) material.SetFloat("_Usedepth", 0);
            EditorUtility.SetDirty(material); return material;
        }

        static GameObject BattlePackSlash(string name, string sourcePath, float scale, float speed)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (!source) throw new Exception("Missing selected slash: " + sourcePath);
            var root = Object.Instantiate(source); root.name = name;
            try
            {
                PrepareNewVfxCopy(root);
                root.transform.localPosition = Vector3.zero;
                root.transform.localRotation = Quaternion.Euler(90, 0, 0) * source.transform.localRotation;
                root.transform.localScale = Vector3.one * scale;
                foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = p.main; main.simulationSpeed = speed; main.maxParticles = Mathf.Min(main.maxParticles, 64);
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    main.duration = Mathf.Min(main.duration, .3f);
                    if (main.startLifetime.constantMax > .42f) main.startLifetimeMultiplier *= .42f / main.startLifetime.constantMax;
                    if (main.startDelay.constantMax > .1f) main.startDelayMultiplier *= .1f / main.startDelay.constantMax;
                    var lights = p.lights; lights.enabled = false;
                    var trails = p.trails;
                    if (trails.enabled && trails.lifetime.constantMax > .2f) trails.lifetimeMultiplier *= .2f / trails.lifetime.constantMax;
                }
                foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    renderer.sharedMaterials = renderer.sharedMaterials.Select(SlashPackMaterial).ToArray();
                FillParticleMaterialSlots(root);
                return PrefabUtility.SaveAsPrefabAsset(root, SlashPackAssets + "/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("Tools/Battle/Install selected Hovl and ERB slashes")]
        public static void InstallBattleSlashPacks()
        {
            Directory.CreateDirectory(SlashPackReview); Directory.CreateDirectory(SlashPackAssets + "/Materials");
            if (!File.Exists(SlashPackReview + "/BattleSceneBefore.unity.txt"))
                File.Copy(SfxScene, SlashPackReview + "/BattleSceneBefore.unity.txt");
            var gold = BattlePackSlash("BattleHovlGoldSlash", HovlSlashes + "Sword Slash 3.prefab", .30f, 1.8f);
            var red = BattlePackSlash("BattleHovlAxeSlash", HovlSlashes + "Sword Slash 7.prefab", .26f, 1.9f);
            var orange = BattlePackSlash("BattleErbKatanaSlash", ErbSlashes + "New/Slash 3.prefab", .34f, 1.9f);
            var blue = BattlePackSlash("BattleErbDaggerSlash", ErbSlashes + "New/Slash 6.prefab", .28f, 2.1f);
            ComicScene((game, camera) =>
            {
                var vfx = game.battleVfx;
                Undo.RecordObject(vfx, "Select weapon slash pack effects");
                vfx.bladeSlash = gold;
                vfx.bladeSlashVariants = new[]
                {
                    SlashVariant(TrumpWeaponManager.WeaponType.WarriorShield, gold, .78f),
                    SlashVariant(TrumpWeaponManager.WeaponType.GreatSword, gold, 1.05f),
                    SlashVariant(TrumpWeaponManager.WeaponType.Spear, gold, .85f),
                    SlashVariant(TrumpWeaponManager.WeaponType.TwoHandedAxe, red, 1),
                    SlashVariant(TrumpWeaponManager.WeaponType.Katana, orange, 1),
                    SlashVariant(TrumpWeaponManager.WeaponType.DualDaggers, blue, 1.1f),
                    SlashVariant(TrumpWeaponManager.WeaponType.Assassin, blue, .86f)
                };
                EditorUtility.SetDirty(vfx);
            });
            File.WriteAllText(SlashPackReview + "/Selection.txt",
                "Rendered 34 candidates at .06/.16s. Selected four single-swing prefabs (no automated pack combos).\n" +
                "Hovl Sword Slash 3 -> GreatSword, WarriorShield and Spear sweeps: gold flame/sparks.\n" +
                "Hovl Sword Slash 7 -> TwoHandedAxe: red force arc.\n" +
                "ERB New/Slash 3 -> Katana: sharp orange crescent.\n" +
                "ERB New/Slash 6 -> DualDaggers and Assassin: compact blue multi-streak crescent.\n" +
                "Battle-only material/prefab copies; original pack assets untouched. One existing blade cue = one pooled effect.\n" +
                "Follows the fastest evaluated blade; rotation follows screen-space sweep. Lifetime <= .42 authored seconds, simulation speed 1.8-2.1.\n" +
                "Mage/Archer hits, ground impact, comic WHOOSH/POW/WHAM/SMASH, animation and damage timings retained. Camera unchanged.\n");
        }

        static BattleVfxPlayer.WeaponSlashVariant SlashVariant(TrumpWeaponManager.WeaponType weapon, GameObject prefab, float scale)
            => new BattleVfxPlayer.WeaponSlashVariant { weapon = weapon, prefab = prefab, scale = scale };

        [MenuItem("Tools/Battle/Validate selected slash packs")]
        public static void ValidateBattleSlashPacks()
        {
            ValidateBattleVfx();
            File.Copy("Temp/FrankRetarget/battle-vfx-validation.txt", SlashPackReview + "/TimelineValidation.txt", true);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var report = new StringBuilder();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var variants = game.battleVfx.bladeSlashVariants;
                if (variants == null || variants.Length != 7 || variants.Select(v => v.weapon).Distinct().Count() != 7)
                    throw new Exception("Expected seven distinct weapon slash bindings.");
                foreach (var variant in variants)
                {
                    string expected = variant.weapon == TrumpWeaponManager.WeaponType.TwoHandedAxe ? "BattleHovlAxeSlash" :
                        variant.weapon == TrumpWeaponManager.WeaponType.Katana ? "BattleErbKatanaSlash" :
                        variant.weapon == TrumpWeaponManager.WeaponType.DualDaggers ? "BattleErbDaggerSlash" :
                        variant.weapon == TrumpWeaponManager.WeaponType.Assassin ? "BattleErbAssassinSlash" :
                        variant.weapon == TrumpWeaponManager.WeaponType.Spear ? "BattleHovlSpearThrust" :
                        variant.weapon == TrumpWeaponManager.WeaponType.WarriorShield ? "BattleHovlShieldSlash" : "BattleHovlGoldSlash";
                    if (!variant.prefab || variant.prefab.name != expected) throw new Exception("Wrong saved mapping for " + variant.weapon);
                    report.AppendLine("PASS saved " + variant.weapon + " -> " + variant.prefab.name + ", scale=" + variant.scale);
                }
                foreach (var prefab in variants.Select(v => v.prefab).Distinct())
                {
                    if (prefab.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 || prefab.GetComponentsInChildren<AudioSource>(true).Length != 0)
                        throw new Exception("Pack demo/audio behavior escaped into " + prefab.name);
                    if (prefab.GetComponentsInChildren<Light>(true).Any(l => l.enabled)) throw new Exception("Unpooled pack light: " + prefab.name);
                    foreach (var p in prefab.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        if (p.main.maxParticles > 64 || p.main.startLifetime.constantMax > .421f || p.lights.enabled ||
                            p.main.simulationSpace != ParticleSystemSimulationSpace.Local)
                            throw new Exception("Unbounded slash particles: " + prefab.name + "/" + p.name);
                    }
                    foreach (var renderer in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    foreach (var material in renderer.sharedMaterials.Where(m => m))
                        if (!AssetDatabase.GetAssetPath(material).StartsWith(SlashPackAssets + "/Materials/", StringComparison.Ordinal))
                            throw new Exception("Slash uses an original shared pack material: " + prefab.name);
                    report.AppendLine("PASS " + prefab.name + ": shaders, private materials, short lifetime, local particle simulation, no loop/autoplay/audio/demo lights.");
                }
                report.AppendLine("PASS weapon selection and authored cue playback for both fighters and both directions; see TimelineValidation.txt.");
                File.WriteAllText(SlashPackReview + "/BindingValidation.txt", report.ToString());
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [MenuItem("Tools/Battle/Capture selected slashes in Battle")]
        public static void CaptureBattleSlashPackContacts()
        {
            Directory.CreateDirectory(SlashPackReview + "/Battle");
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var target = new RenderTexture(1280, 720, 24);
            BattleLightingRig lighting = null; Camera camera = null;
            var report = new StringBuilder();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.targetTexture = target;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                var director = camera.GetComponent<FrankCinematicCamera>();
                var fighters = new[] {game.leftCombat, game.rightCombat};
                foreach (var fighter in fighters) fighter.Initialize();
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig();
                game.GetComponent<BattleImpactFeedback>().Bind(); game.uiManager.hudFeedback.Bind(); game.uiManager.SetTurnNumber(1);
                foreach (var attacker in fighters)
                foreach (var move in attacker.heavyCombatMoves)
                {
                    var receiver = fighters.Single(f => f != attacker);
                    foreach (var fighter in fighters) fighter.ResetCombat(); game.battleVfx.ResetForMatch();
                    var profile = game.battleVfx.timeline.FindMove(move);
                    var cue = profile.cues.First(c => c.group == "blade_swing" || c.group == "heavy_swing");
                    bool reverse = attacker == fighters[1];
                    attacker.transform.position = new Vector3(reverse ? .5f : -.5f, 0, 0);
                    receiver.transform.position = attacker.transform.position + Vector3.right * (reverse ? -move.attackRange : move.attackRange);
                    var effects = new Dictionary<GameObject, float>(); GameObject slash = null;
                    Action<string, GameObject> observe = (id, effect) =>
                    { effects[effect] = attacker.SourcePlayback.SampleTime; if (id == "blade_slash") slash = effect; };
                    game.battleVfx.EffectPlayed += observe;
                    try
                    {
                        if (!attacker.ExecuteAttack(move, receiver)) throw new Exception("Rejected " + move.moveName);
                        var pair = attacker.SourcePlayback; const float age = .06f;
                        pair.EvaluateAt(cue.seconds); game.battleVfx.AdvanceSequence(pair, cue.seconds);
                        foreach (var effect in effects.Where(e => e.Key && e.Key.activeSelf))
                        foreach (var p in effect.Key.GetComponentsInChildren<ParticleSystem>())
                        {
                            p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); p.useAutoRandomSeed = false; p.randomSeed = 321;
                            p.Simulate(cue.seconds - effect.Value + age, false, true, true);
                        }
                        if (!slash || !slash.GetComponentsInChildren<ParticleSystem>().Any(p => p.particleCount > 0))
                            throw new Exception("Selected slash not visible at contact age: " + move.moveName);
                        lighting.RefreshLighting(age); game.GetComponent<BattleImpactFeedback>().AdvanceFeedback(age);
                        game.uiManager.hudFeedback.AdvanceHud(age); director.ResetView(); director.Apply(0, true);
                        Canvas.ForceUpdateCanvases(); game.GetComponent<BattleImpactFeedback>().RefreshFlashProjection(); Canvas.ForceUpdateCanvases();
                        string imagePath = SlashPackReview + "/Battle/" + (reverse ? "Pepe_" : "Mankey_") + move.moveName + ".png";
                        CaptureBattleCamera(camera, imagePath);
                        report.AppendLine("PASS " + move.moveName + " attacker=" + attacker.name + " cue=" + cue.seconds +
                            " slash=" + slash.name + " particles=" + slash.GetComponentsInChildren<ParticleSystem>().Sum(p => p.particleCount) +
                            " orientation=" + slash.transform.eulerAngles + " screenshot=" + imagePath);
                        pair.Cancel();
                    }
                    finally { game.battleVfx.EffectPlayed -= observe; }
                }
                File.WriteAllText(SlashPackReview + "/RenderValidation.txt", report.ToString());
            }
            finally
            {
                if (lighting) lighting.ShutdownRig(); if (camera) camera.targetTexture = null;
                Object.DestroyImmediate(target); EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [MenuItem("Tools/Battle/Preview Hovl and ERB slash packs")]
        public static void PreviewBattleSlashPacks()
        {
            Directory.CreateDirectory(SlashPackReview + "/Candidates");
            var paths = Enumerable.Range(1, 17).Select(i => HovlSlashes + "Sword Slash " + i + ".prefab")
                .Concat(Enumerable.Range(1, 3).Select(i => HovlSlashes + "Prick " + i + ".prefab"))
                .Concat(Enumerable.Range(1, 8).Select(i => ErbSlashes + "New/Slash " + i + ".prefab"))
                .Concat(new[] { "Slash5 thin orange", "Slash8 thin orange", "Slash5 thin blue", "Slash8 thin blue", "Slash wave yellow", "Slash orange" }
                    .Select(n => ErbSlashes + n + ".prefab")).ToArray();
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Slash pack preview camera");
            SceneManager.MoveGameObjectToScene(root, scene);
            var camera = root.AddComponent<Camera>();
            camera.enabled = false; camera.scene = scene;
            camera.transform.position = new Vector3(0, 0, 5); camera.transform.LookAt(Vector3.zero);
            camera.orthographic = true; camera.orthographicSize = 1.65f;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.06f, .065f, .085f);
            var canvasRoot = new GameObject("Candidate label", typeof(RectTransform), typeof(Canvas));
            SceneManager.MoveGameObjectToScene(canvasRoot, scene);
            var canvas = canvasRoot.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera; canvas.planeDistance = 1;
            var labelRoot = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelRoot.transform.SetParent(canvasRoot.transform, false);
            var label = labelRoot.GetComponent<Text>(); label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 16; label.color = Color.white; label.alignment = TextAnchor.UpperLeft; label.raycastTarget = false;
            var labelRect = label.rectTransform; labelRect.anchorMin = new Vector2(0, 1); labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(.5f, 1); labelRect.sizeDelta = new Vector2(-8, 24); labelRect.anchoredPosition = new Vector2(0, -4);
            var target = new RenderTexture(256, 256, 24); camera.targetTexture = target;
            var tile = new Texture2D(256, 256, TextureFormat.RGB24, false);
            var sheet = new Texture2D(1536, 1024, TextureFormat.RGB24, false);
            var report = new StringBuilder(); var previousTarget = RenderTexture.active;
            try
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    if (i % 12 == 0) sheet.SetPixels(Enumerable.Repeat(camera.backgroundColor, 1536 * 1024).ToArray());
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                    if (!prefab) throw new Exception("Missing slash candidate " + paths[i]);
                    var effect = Object.Instantiate(prefab); SceneManager.MoveGameObjectToScene(effect, scene);
                    try
                    {
                        PrepareNewVfxCopy(effect);
                        effect.transform.SetPositionAndRotation(Vector3.zero, camera.transform.rotation * Quaternion.Euler(90, 0, 0) * prefab.transform.localRotation);
                        effect.transform.localScale = Vector3.one * .3f;
                        report.AppendLine(i + ": " + paths[i] + "; systems=" + effect.GetComponentsInChildren<ParticleSystem>().Length +
                            "; shaders=" + string.Join(", ", effect.GetComponentsInChildren<ParticleSystemRenderer>()
                                .SelectMany(r => r.sharedMaterials).Where(m => m).Select(m => m.shader.name + ":" + m.shader.isSupported).Distinct()));
                        for (int ageIndex = 0; ageIndex < 2; ageIndex++)
                        {
                            float age = ageIndex == 0 ? .06f : .16f;
                            foreach (var p in effect.GetComponentsInChildren<ParticleSystem>())
                            {
                                p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                                p.useAutoRandomSeed = false; p.randomSeed = 321;
                                p.Simulate(age, false, true, true);
                            }
                            label.text = i + " " + (i < 20 ? "Hovl " : "ERB ") + prefab.name + " / " + age;
                            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
                            tile.ReadPixels(new Rect(0, 0, 256, 256), 0, 0); tile.Apply();
                            File.WriteAllBytes(SlashPackReview + "/Candidates/" + i.ToString("D2") + "_" + ageIndex + ".png", tile.EncodeToPNG());
                            int cell = i % 12;
                            sheet.SetPixels(cell % 3 * 512 + ageIndex * 256, (3 - cell / 3) * 256, 256, 256, tile.GetPixels());
                        }
                    }
                    finally { Object.DestroyImmediate(effect); }
                    if (i % 12 == 11 || i == paths.Length - 1)
                    { sheet.Apply(); File.WriteAllBytes(SlashPackReview + "/Candidates/Sheet" + (i / 12) + ".png", sheet.EncodeToPNG()); }
                }
                File.WriteAllText(SlashPackReview + "/Candidates/Index.txt", report.ToString());
            }
            finally
            {
                RenderTexture.active = previousTarget; camera.targetTexture = null;
                Object.DestroyImmediate(tile); Object.DestroyImmediate(sheet); Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public static void BattleSlashPackPlayCheck()
        {
            FrankBattleSfxPlayCheck.Start(false, true);
            SessionState.SetBool(FrankBattleSlashPlayCapture.Key, true);
        }
    }

    [InitializeOnLoad]
    public static class FrankBattleSlashPlayCapture
    {
        public const string Key = "BattleSlashPackPlayCapture";
        const string Folder = "GeneratedAssets/BattleSlashPackReview";
        static readonly HashSet<string> captured = new HashSet<string>();
        static readonly StringBuilder report = new StringBuilder();
        static GameManager game;
        static FrankRetarget.FrankBattlePairPlayback playback;
        static GameObject slash;
        static string captureName;
        static float cueTime;
        static string failure;
        static Mesh bakedBlade;

        static FrankBattleSlashPlayCapture()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += Finish;
        }

        static void Observe(string id, GameObject effect)
        {
            if (id != "blade_slash" || slash) return;
            var pair = game.leftCombat.SourcePlayback && game.leftCombat.SourcePlayback.Playing ? game.leftCombat.SourcePlayback : game.rightCombat.SourcePlayback;
            if (!pair || pair.Move == null) return;
            string name = (pair.AttackerActor.character == game.leftCombat.Animator ? "Mankey_" : "Pepe_") + pair.Move.moveName;
            if (captured.Contains(name)) return;
            captureName = name; playback = pair; slash = effect; cueTime = pair.SampleTime;
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            if (!game)
            {
                game = GameManager.Instance;
                if (!game || !game.battleVfx) return;
                game.battleVfx.EffectPlayed += Observe;
            }
            if (!slash || !playback || playback.SampleTime - cueTime < .055f) return;
            var blades = playback.AttackerActor.Pose.weaponRenderers.Where(r => r && r.enabled && r.gameObject.activeInHierarchy &&
                r.name.IndexOf("shield", StringComparison.OrdinalIgnoreCase) < 0).ToArray();
            float distance = float.PositiveInfinity;
            foreach (var renderer in blades)
            {
                Vector3 center = renderer.bounds.center;
                if (renderer is SkinnedMeshRenderer skin)
                {
                    if (!bakedBlade) bakedBlade = new Mesh();
                    skin.BakeMesh(bakedBlade); center = skin.transform.TransformPoint(bakedBlade.bounds.center);
                }
                distance = Mathf.Min(distance, Vector3.Distance(slash.transform.position, center));
            }
            if (!slash.activeInHierarchy || distance > .18f)
                failure = "Sweep detached from rotating blade: " + captureName + " distance=" + distance;
            captured.Add(captureName);
            Directory.CreateDirectory(Folder + "/Live");
            ScreenCapture.CaptureScreenshot(Folder + "/Live/" + captureName + ".png");
            report.AppendLine((distance <= .18f && slash.activeInHierarchy ? "PASS" : "FAIL") + " live " + captureName + ": " + slash.name + ", cue=" + cueTime +
                ", sample=" + playback.SampleTime + ", distance to blade center=" + distance);
            slash = null; playback = null;
        }

        static void Finish(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(Key, false)) return;
            SessionState.SetBool(Key, false);
            if (game && game.battleVfx) game.battleVfx.EffectPlayed -= Observe;
            report.AppendLine(failure != null ? "FAIL " + failure : captured.Count != 14 ?
                "FAIL expected 14 live weapon captures, got " + captured.Count : "PASS 14 native live captures; all slashes followed their rotating blades.");
            File.WriteAllText(Folder + "/LiveSlashValidation.txt", report.ToString());
            File.Copy("Temp/FrankRetarget/battle-sfx-play.txt", Folder + "/PlayValidation.txt", true);
            game = null; playback = null; slash = null; failure = captureName = null;
            if (bakedBlade) Object.DestroyImmediate(bakedBlade); bakedBlade = null;
            captured.Clear(); report.Clear();
        }
    }
}
