using System;
using System.Collections.Generic;
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
        const string AccentAssets = "Assets/Vfx/Battle/CombatAccent";
        const string AccentReview = "GeneratedAssets/BattleCombatAccentReview";

        static void PrepareCombatAccent()
        {
            Directory.CreateDirectory(AccentAssets); Directory.CreateDirectory(AccentReview);
            if (!File.Exists(AccentReview + "/BattleSceneBefore.unity.txt")) File.Copy(SfxScene, AccentReview + "/BattleSceneBefore.unity.txt");
            AssetDatabase.Refresh();
        }

        static void SingleBurst(ParticleSystem p, float lifetime = .25f)
        {
            p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = p.main;
            main.loop = main.playOnAwake = false; main.stopAction = ParticleSystemStopAction.None;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy; main.maxParticles = Mathf.Min(main.maxParticles, 48);
            main.duration = Mathf.Min(main.duration, .15f);
            if (main.startLifetime.constantMax > lifetime) main.startLifetimeMultiplier *= lifetime / main.startLifetime.constantMax;
            if (main.startDelay.constantMax > .03f) main.startDelay = 0;
            var emission = p.emission;
            var bursts = new ParticleSystem.Burst[emission.burstCount]; emission.GetBursts(bursts);
            bool continuous = emission.rateOverTime.constantMax > 0;
            emission.rateOverTime = 0; emission.rateOverDistance = 0;
            if (bursts.Length == 0 && continuous) bursts = new[] { new ParticleSystem.Burst(0, (short)6) };
            for (int i = 0; i < bursts.Length; i++) { bursts[i].time = 0; bursts[i].cycleCount = 1; bursts[i].repeatInterval = .1f; }
            emission.SetBursts(bursts);
            var sub = p.subEmitters; sub.enabled = false;
            var lights = p.lights; lights.enabled = false;
        }

        static Mesh ContactStar(string name, Color tint, int style)
        {
            var points = new List<Vector3>(); var colors = new List<Color32>(); var triangles = new List<int>();
            Layer(.25f, .065f, new Color(.055f, .04f, .07f, 1));
            Layer(.23f, .055f, tint); Layer(.13f, .045f, Color.white);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(AccentAssets + "/" + name + ".asset");
            if (!mesh) { mesh = new Mesh { name = name }; AssetDatabase.CreateAsset(mesh, AccentAssets + "/" + name + ".asset"); }
            mesh.Clear(); mesh.SetVertices(points); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); return mesh;
            void Layer(float outer, float inner, Color color)
            {
                int center = points.Count; points.Add(Vector3.zero); colors.Add(color);
                int rays = style <= 1 ? 8 : new[] { 8, 6, 12, 4, 5, 4, 10 }[(style - 2) % 7];
                int count = rays * 2;
                for (int i = 0; i < count; i++)
                {
                    float angle = i * Mathf.PI / rays;
                    float radius = i % 2 == 0 ? outer * (i % 4 == 0 ? 1 : .75f) : inner;
                    Vector2 stretch = style == 7 ? new Vector2(1.4f, .55f) : style == 6 ? new Vector2(1.2f, .8f) : Vector2.one;
                    points.Add(new Vector3(Mathf.Cos(angle) * radius * stretch.x, Mathf.Sin(angle) * radius * stretch.y, -.001f * center)); colors.Add(color);
                }
                for (int i = 0; i < count; i++) triangles.AddRange(new[] { center, center + 1 + i, center + 1 + (i + 1) % count });
            }
        }

        static GameObject FocusedHit(string name, Color color, bool heavy, int shape)
        {
            var root = new GameObject(name);
            try
            {
                var spark = PowerBurst(root, PowerCandidates[shape == 1 ? 0 : 7], heavy ? .16f : .11f, 1.8f);
                foreach (var p in spark.GetComponentsInChildren<ParticleSystem>(true)) SingleBurst(p, heavy ? .24f : .17f);
                var child = new GameObject("Contact core"); child.transform.SetParent(root.transform, false);
                var pCore = child.AddComponent<ParticleSystem>(); pCore.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = pCore.main; main.duration = .12f; main.loop = main.playOnAwake = false;
                main.startLifetime = heavy ? .13f : .1f; main.startSpeed = 0; main.startSize = heavy ? 1 : .7f;
                main.maxParticles = 1; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.startRotation = shape * .18f;
                var emission = pCore.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)1) });
                var shapeModule = pCore.shape; shapeModule.enabled = false;
                var size = pCore.sizeOverLifetime; size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, .35f), new Keyframe(.18f, 1.2f), new Keyframe(1, .6f)));
                var fade = pCore.colorOverLifetime; fade.enabled = true;
                var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .5f), new GradientAlphaKey(0, 1) }); fade.color = gradient;
                var material = AssetDatabase.LoadAssetAtPath<Material>(ComicAssets + "/ComicParticle.mat");
                if (!material) throw new Exception("Missing comic core shader/material.");
                var renderer = pCore.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.mesh = ContactStar(name + "Core", color, shape); renderer.sharedMaterial = material;
                renderer.alignment = ParticleSystemRenderSpace.Local;
                renderer.sortingOrder = 20; renderer.sortingFudge = -1;
                renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color });
                return PrefabUtility.SaveAsPrefabAsset(root, AccentAssets + "/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [MenuItem("Tools/Battle/Combat accents/1 Focus hit and ground effects")]
        public static void InstallFocusedBattleHits()
        {
            PrepareCombatAccent();
            var light = FocusedHit("FocusedPunch", new Color(1, .66f, .12f), false, 0);
            var heavy = FocusedHit("FocusedHeavy", new Color(1, .45f, .08f), true, 1);
            ComicScene((game, camera) =>
            {
                var vfx = game.battleVfx; vfx.lightHit = light; vfx.heavyHit = heavy;
                var original = Object.Instantiate(vfx.landingDust); original.name = "SpreadingGroundSmoke";
                try
                {
                    PrepareNewVfxCopy(original); original.transform.localScale = Vector3.one;
                    foreach (var p in original.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        SingleBurst(p, .65f);
                        var main = p.main; main.simulationSpeed = 1; main.startLifetime = new ParticleSystem.MinMaxCurve(.45f, .65f);
                        main.startSize = new ParticleSystem.MinMaxCurve(.16f, .24f); main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 1.8f);
                        main.startColor = new Color(.72f, .76f, .8f, .55f); main.maxParticles = 20;
                        p.transform.localRotation = Quaternion.Euler(90, 0, 0);
                        var shape = p.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Circle;
                        shape.radius = .12f; shape.radiusThickness = 0; shape.arc = 360;
                        var emission = p.emission; emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)16) });
                        var size = p.sizeOverLifetime; size.enabled = true;
                        size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0, .4f, 1, 1.7f));
                    }
                    vfx.landingDust = PrefabUtility.SaveAsPrefabAsset(original, AccentAssets + "/SpreadingGroundSmoke.prefab");
                }
                finally { Object.DestroyImmediate(original); }
                var ground = new GameObject("FocusedGroundImpact");
                try
                {
                    AddComicParticle(ground, ComicMesh("GroundRing", true), .24f, true);
                    foreach (var p in ground.GetComponentsInChildren<ParticleSystem>())
                    { var main = p.main; main.startSize = .9f; }
                    vfx.groundImpact = PrefabUtility.SaveAsPrefabAsset(ground, AccentAssets + "/FocusedGroundImpact.prefab");
                }
                finally { Object.DestroyImmediate(ground); }
                EditorUtility.SetDirty(vfx);
            });
            File.WriteAllText(AccentReview + "/Step1.txt", "Focused short contact star/core and sparks; no hit letters or broad stacked magic waves. Ground = one spreading smoke ring, short floor shock and final SMASH.\n");
            CaptureCombatAccents("Step1");
        }

        public static void CaptureCombatAccents() => CaptureCombatAccents("Final");
        public static void CaptureCombatAccentSurfaceDiagnostic() => CaptureCombatAccents("SurfaceDiagnostic");

        public static void InstallCombatDimming()
        {
            ComicScene((game, camera) =>
            {
                var lighting = game.GetComponent<BattleLightingRig>();
                lighting.key = game.gameObject.scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Light>())
                    .First(l => l.type == LightType.Directional);
                lighting.combatDimming = true; lighting.combatExposure = .82f;
                EditorUtility.SetDirty(lighting);
            });
            File.WriteAllText(AccentReview + "/Step2.txt", "During combat: backdrop/fill exposure 82%, key 90%, rim 92%; .18s in/.3s out. Original light intensities and renderer property blocks restored on reset/disable. Impact lights retain their full contrast.\n");
        }

        public static void InstallKoSlowMotion()
        {
            ComicScene((game, camera) =>
            {
                var feedback = game.GetComponent<BattleImpactFeedback>();
                feedback.knockoutSlowMotion = true; feedback.knockoutSpeed = .32f;
                feedback.knockoutSeconds = 1.8f; feedback.knockoutRecovery = .3f;
                EditorUtility.SetDirty(game.uiManager.knockout);
                EditorUtility.SetDirty(feedback);
            });
            File.WriteAllText(AccentReview + "/Step3.txt", "One KO slow-motion pulse on the lethal move's final damage contact: 32% speed for 1.8 real seconds, .3s recovery. Hit-stop and slow motion share one clock owner; UI keeps unscaled DOTween; external pause/speed changes and cancellation preserved.\n");
        }

        public static void InstallSingleBurstGuard()
        {
            ComicScene((game, camera) =>
            {
                var vfx = game.battleVfx;
                var prefabs = new[] { vfx.lightHit, vfx.heavyHit, vfx.landingDust, vfx.groundImpact,
                    vfx.landingText, vfx.lightSwing, vfx.thrustSwing, vfx.bladeSlash }
                    .Concat(vfx.bladeSlashVariants.Select(v => v.prefab)).Distinct().Where(p => p);
                foreach (var prefab in prefabs)
                {
                    string path = AssetDatabase.GetAssetPath(prefab); var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true)) if (script) Object.DestroyImmediate(script);
                        foreach (var p in root.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                            var main = p.main; main.loop = main.playOnAwake = false; main.stopAction = ParticleSystemStopAction.None;
                            var emission = p.emission; var bursts = new ParticleSystem.Burst[emission.burstCount]; emission.GetBursts(bursts);
                            if (bursts.Length == 0 && emission.rateOverTime.constantMax > 0)
                                bursts = new[] { new ParticleSystem.Burst(0, (short)4) };
                            for (int i = 0; i < bursts.Length; i++) bursts[i].cycleCount = 1;
                            emission.rateOverTime = emission.rateOverDistance = 0; emission.SetBursts(bursts);
                            var sub = p.subEmitters; sub.enabled = false;
                        }
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
            });
            File.WriteAllText(AccentReview + "/Step4.txt", "One contact cue = one hit prefab. Repeated Begin/Advance times cannot retrigger the same playback. Pack behaviors, auto-play, loops, recurring burst cycles, continuous emission and sub-emitter echo are removed from active Battle effects.\n");
        }

        public static void InstallHeavyImpactShake()
        {
            ComicScene((game, camera) =>
            {
                var shake = camera.GetComponent<BattleCameraShake>();
                if (!shake) shake = Undo.AddComponent<BattleCameraShake>(camera.gameObject);
                shake.vfx = game.battleVfx; shake.heavyStrength = .045f; shake.groundStrength = .035f; shake.duration = .2f;
                EditorUtility.SetDirty(shake);
            });
            File.WriteAllText(AccentReview + "/Step5.txt", "Heavy contact and floor landing impulses (.045/.035m, .2s), applied after the authored camera shot and cleared without drift. Camera angle, shot library, lens and distance settings retained.\n");
        }

        public static void InstallWeaponImpactVariety()
        {
            PrepareCombatAccent();
            var shield = BattlePackSlash("BattleHovlShieldSlash", HovlSlashes + "Sword Slash 2.prefab", .24f, 1.9f);
            var spear = BattlePackSlash("BattleHovlSpearThrust", HovlSlashes + "Prick 2.prefab", .25f, 1.9f);
            var assassin = BattlePackSlash("BattleErbAssassinSlash", ErbSlashes + "New/Slash 2.prefab", .23f, 2.1f);
            var weapons = PairWeapons;
            var colors = new[] { new Color(1,.25f,.1f), new Color(.8f,.35f,1), new Color(.15f,.85f,1),
                new Color(1,.72f,.16f), new Color(1,.9f,.65f), new Color(.25f,.85f,.75f), new Color(1,.8f,.25f) };
            var impacts = weapons.Select((weapon, i) => new BattleVfxPlayer.WeaponImpactVariant
            {
                weapon = weapon, light = FocusedHit(weapon + "Contact", colors[i], false, i + 2),
                heavy = FocusedHit(weapon + "HeavyContact", colors[i], true, i + 2)
            }).ToArray();
            ComicScene((game, camera) =>
            {
                var vfx = game.battleVfx; vfx.impactVariants = impacts;
                foreach (var variant in vfx.bladeSlashVariants)
                    if (variant.weapon == TrumpWeaponManager.WeaponType.WarriorShield) { variant.prefab = shield; variant.scale = 1; }
                    else if (variant.weapon == TrumpWeaponManager.WeaponType.Spear) { variant.prefab = spear; variant.scale = 1; }
                    else if (variant.weapon == TrumpWeaponManager.WeaponType.Assassin) { variant.prefab = assassin; variant.scale = 1; }
                var smokePath = AssetDatabase.GetAssetPath(vfx.landingDust); var smoke = PrefabUtility.LoadPrefabContents(smokePath);
                try
                {
                    var material = AssetDatabase.LoadAssetAtPath<Material>(AccentAssets + "/SoftSmoke.mat");
                    if (!material) { material = new Material(Shader.Find("Battle/Soft Smoke")); AssetDatabase.CreateAsset(material, AccentAssets + "/SoftSmoke.mat"); }
                    foreach (var p in smoke.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var renderer = p.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Billboard;
                        renderer.sharedMaterial = material;
                        renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
                        var sheet = p.textureSheetAnimation; sheet.enabled = false;
                        var fade = p.colorOverLifetime; fade.enabled = true;
                        var gradient = new Gradient(); gradient.SetKeys(new[] {new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                            new[] {new GradientAlphaKey(0,0),new GradientAlphaKey(1,.12f),new GradientAlphaKey(.65f,.5f),new GradientAlphaKey(0,1)}); fade.color = gradient;
                    }
                    PrefabUtility.SaveAsPrefabAsset(smoke, smokePath);
                }
                finally { PrefabUtility.UnloadPrefabContents(smoke); }
                EditorUtility.SetDirty(vfx);
            });
            InstallSingleBurstGuard();
            File.WriteAllText(AccentReview + "/Step6.txt", "Seven separate weapon slashes and fourteen light/heavy contact prefabs. Weapon contacts use baked blade geometry, not an idle hand/foot. Ground smoke uses soft round billboards.\n");
        }
        public static void RepairCombatAccentMaterials()
        {
            ComicScene((game, camera) =>
            {
                var vfx = game.battleVfx;
                var prefabs = new[] { vfx.lightHit, vfx.heavyHit, vfx.landingDust, vfx.groundImpact, vfx.landingText,
                    vfx.lightSwing, vfx.thrustSwing, vfx.bladeSlash }.Concat(vfx.bladeSlashVariants.Select(v => v.prefab))
                    .Concat(vfx.impactVariants.SelectMany(v => new[] { v.light, v.heavy }))
                    .Concat(vfx.skillVariants.SelectMany(v => new[] { v.cast, v.projectile, v.impact })).Distinct();
                foreach (var prefab in prefabs)
                {
                    string path = AssetDatabase.GetAssetPath(prefab); var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        var renderers = root.GetComponentsInChildren<ParticleSystemRenderer>(true);
                        var fallback = renderers.SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m);
                        if (!fallback) throw new Exception("No valid material in " + path);
                        foreach (var renderer in renderers)
                        {
                            renderer.sharedMaterials = renderer.sharedMaterials.Length == 0 ? new[] { fallback } :
                                renderer.sharedMaterials.Select(m => m ? m : fallback).ToArray();
                            if (renderer.name == "Contact core") { renderer.sortingOrder = 20; renderer.sortingFudge = -1; }
                        }
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
            });
        }
        static void FillParticleMaterialSlots(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<ParticleSystemRenderer>(true);
            var fallback = renderers.SelectMany(r => r.sharedMaterials).FirstOrDefault(m => m);
            if (!fallback) throw new Exception("No valid material in " + root.name);
            foreach (var renderer in renderers)
                renderer.sharedMaterials = renderer.sharedMaterials.Length == 0 ? new[] { fallback } :
                    renderer.sharedMaterials.Select(m => m ? m : fallback).ToArray();
        }
        public static void RefineBattleCombatSurfacesAndSmoke()
        {
            InstallBattleComicStep3();
            var smoke = new GameObject("SpreadingGroundSmoke");
            try
            {
                smoke.transform.localRotation = Quaternion.Euler(90, 0, 0);
                var p = smoke.AddComponent<ParticleSystem>(); p.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = p.main; main.loop = main.playOnAwake = false; main.duration = .12f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(.5f, .65f); main.startSpeed = 0;
                main.startSize3D = false; main.startSize = new ParticleSystem.MinMaxCurve(.3f, .45f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
                main.startColor = new Color(.68f, .72f, .78f, .65f); main.maxParticles = 20;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var emission = p.emission; emission.rateOverTime = 0; emission.SetBursts(new[] { new ParticleSystem.Burst(0, (short)16) });
                var shape = p.shape; shape.shapeType = ParticleSystemShapeType.Circle; shape.radius = .13f; shape.radiusThickness = 0;
                var velocity = p.velocityOverLifetime; velocity.enabled = true; velocity.space = ParticleSystemSimulationSpace.Local;
                velocity.radial = 1.4f; velocity.z = -.1f;
                var size = p.sizeOverLifetime; size.enabled = true; size.size = new ParticleSystem.MinMaxCurve(1, AnimationCurve.Linear(0,.4f,1,1.8f));
                var fade = p.colorOverLifetime; fade.enabled = true;
                var gradient = new Gradient(); gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                    new[] { new GradientAlphaKey(0,0), new GradientAlphaKey(1,.12f), new GradientAlphaKey(.6f,.5f), new GradientAlphaKey(0,1) }); fade.color = gradient;
                var renderer = p.GetComponent<ParticleSystemRenderer>(); renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.alignment = ParticleSystemRenderSpace.View;
                renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(AccentAssets + "/SoftSmoke.mat");
                renderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
                PrefabUtility.SaveAsPrefabAsset(smoke, AccentAssets + "/SpreadingGroundSmoke.prefab");
            }
            finally { Object.DestroyImmediate(smoke); }
        }
        static void CaptureCombatAccents(string stage)
        {
            Directory.CreateDirectory(AccentReview + "/" + stage);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene); var target = new RenderTexture(1280, 720, 24);
            BattleLightingRig lighting = null; Camera camera = null;
            var diagnosticMaterials = new List<Material>();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GameManager>(true)).Single();
                camera = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                camera.scene = scene; camera.targetTexture = target;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Canvas>(true)))
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                var fighters = new[] { game.leftCombat, game.rightCombat }; foreach (var fighter in fighters) fighter.Initialize();
                if (stage == "SurfaceDiagnostic")
                    foreach (var fighter in fighters)
                    foreach (var renderer in fighter.Animator.GetComponentsInChildren<Renderer>(true))
                        renderer.sharedMaterials = renderer.sharedMaterials.Select(m =>
                        {
                            var copy = new Material(m) { shader = Shader.Find("FlatKit/Stylized Surface") };
                            diagnosticMaterials.Add(copy); return copy;
                        }).ToArray();
                lighting = game.GetComponent<BattleLightingRig>(); lighting.InitializeRig(); game.uiManager.SetTurnNumber(1);
                var cutIn = game.uiManager.comicCutIn; cutIn.PreviewAnimations = true; cutIn.Bind();
                var report = new StringBuilder();
                foreach (var move in game.leftCombat.heavyCombatMoves.Where(m => stage != "SurfaceDiagnostic" || m.moveName == "Heavy_Katana"))
                foreach (bool floor in new[] { false, true })
                {
                    foreach (var fighter in fighters) fighter.ResetCombat(); game.battleVfx.ResetForMatch();
                    game.leftCombat.transform.position = new Vector3(-.5f, 0, 0);
                    game.rightCombat.transform.position = game.leftCombat.transform.position + Vector3.right * move.attackRange;
                    var profile = game.battleVfx.timeline.FindMove(move);
                    var cue = floor ? profile.cues.Last(c => c.finalLanding) : profile.cues.First(c => c.group.EndsWith("hit", StringComparison.Ordinal));
                    var effects = new Dictionary<GameObject, float>();
                    Action<string, GameObject> observe = (id, root) => effects[root] = game.leftCombat.SourcePlayback.SampleTime;
                    game.battleVfx.EffectPlayed += observe;
                    try
                    {
                        if (!game.leftCombat.ExecuteAttack(move, game.rightCombat)) throw new Exception("Accent preview rejected.");
                        var pair = game.leftCombat.SourcePlayback; pair.EvaluateAt(cue.seconds); game.battleVfx.AdvanceSequence(pair, cue.seconds);
                        foreach (var effect in effects)
                        foreach (var p in effect.Key.GetComponentsInChildren<ParticleSystem>())
                        {
                            p.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); p.useAutoRandomSeed = false; p.randomSeed = 321;
                            p.Simulate(cue.seconds - effect.Value + (floor ? .18f : .035f), false, true, true);
                        }
                        lighting.RefreshLighting(.22f);
                        cutIn.EvaluatePreview(cue.seconds);
                        var director = camera.GetComponent<FrankCinematicCamera>(); director.ResetView(); director.Apply(0, true);
                        Canvas.ForceUpdateCanvases();
                        string path = AccentReview + "/" + stage + "/" + move.moveName + (floor ? "_Ground" : "_Hit") + ".png";
                        CaptureBattleCamera(camera, path); report.AppendLine("PASS " + path);
                        if (!floor)
                        {
                            var animator = game.rightCombat.Animator;
                            foreach (var bone in new[] { HumanBodyBones.Head, HumanBodyBones.Chest, HumanBodyBones.Hips })
                                report.AppendLine("  " + bone + "=" + camera.WorldToViewportPoint(animator.GetBoneTransform(bone).position));
                            foreach (var effect in effects.Where(e => e.Key.name.Contains("Contact") || e.Key.name.Contains("Focused")))
                                report.AppendLine("  contact=" + camera.WorldToViewportPoint(effect.Key.transform.position));
                        }
                        pair.Cancel();
                    }
                    finally { game.battleVfx.EffectPlayed -= observe; }
                }
                File.WriteAllText(AccentReview + "/" + stage + "RenderValidation.txt", report.ToString());
            }
            finally
            {
                if (lighting) lighting.ShutdownRig(); if (camera) camera.targetTexture = null;
                Object.DestroyImmediate(target); EditorSceneManager.ClosePreviewScene(scene);
                foreach (var material in diagnosticMaterials) Object.DestroyImmediate(material);
            }
        }
    }
}
