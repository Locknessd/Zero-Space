using System;
using System.Collections.Generic;
using System.Globalization;
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
        const string PolishReview = "GeneratedAssets/BattlePolishReview";
        static readonly string[] PowerCandidates = {
            CfxPrefabs + "Impacts/Variants/CFXR2 Hit (Contrast) + Debris (Lit).prefab",
            CfxPrefabs + "Impacts/Variants/CFXR2 Ground Hit (Contrast) + Debris (Lit).prefab",
            WhiteMagePack + "/Prefabs/Light bomb/Light bomb.prefab",
            WhiteMagePack + "/Prefabs/Energy strike/Energy strike.prefab",
            NewVfxCandidates[1], NewVfxCandidates[8], NewVfxCandidates[7],
            CfxPrefabs + "Impacts/CFXR Impact Contrast.prefab"
        };

        public static void SurveyBattlePower()
        {
            Directory.CreateDirectory(PolishReview);
            if (!File.Exists(PolishReview + "/BattleSceneBefore.unity.txt"))
                File.Copy(SfxScene, PolishReview + "/BattleSceneBefore.unity.txt");
            var scene = EditorSceneManager.NewPreviewScene();
            var cameraRoot = new GameObject("Power VFX preview");
            SceneManager.MoveGameObjectToScene(cameraRoot, scene);
            var camera = cameraRoot.AddComponent<Camera>();
            camera.scene = scene;
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 1.4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.07f, .08f, .1f);
            var target = new RenderTexture(400, 300, 24);
            var sheet = new Texture2D(800, 1200, TextureFormat.RGB24, false);
            var tile = new Texture2D(400, 300, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            var report = new StringBuilder();
            try
            {
                camera.targetTexture = target;
                for (int i = 0; i < PowerCandidates.Length; i++)
                {
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(PowerCandidates[i]);
                    if (!source) throw new Exception("Missing power candidate " + PowerCandidates[i]);
                    var root = Object.Instantiate(source);
                    SceneManager.MoveGameObjectToScene(root, scene);
                    try
                    {
                        PrepareNewVfxCopy(root);
                        camera.transform.position = i == 1 || i == 6 ? new Vector3(0, 2, 4) : new Vector3(0, 0, 4);
                        camera.transform.LookAt(Vector3.zero);
                        root.transform.rotation = (i == 1 || i == 6 ? Quaternion.identity : camera.transform.rotation) * source.transform.localRotation;
                        root.transform.localScale = Vector3.one * .35f;
                        foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                            renderer.sharedMaterials = renderer.sharedMaterials.Select(m => m && PowerCandidates[i].StartsWith(CfxPrefabs, StringComparison.Ordinal) ? BattleEffectMaterial(m) : m).ToArray();
                        report.AppendLine(i + ": " + PowerCandidates[i] + "; systems=" + root.GetComponentsInChildren<ParticleSystem>(true).Length);
                        foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
                        {
                            var main = particles.main;
                            report.AppendLine("  " + particles.name + "; duration=" + main.duration + "; delay=" + main.startDelay.constantMax + "; size=" + main.startSize.constantMax);
                        }
                        foreach (var particles in root.GetComponentsInChildren<ParticleSystem>())
                        {
                            particles.useAutoRandomSeed = false; particles.randomSeed = 321;
                            particles.Simulate(.12f, false, true, true);
                        }
                        camera.Render(); RenderTexture.active = target;
                        tile.ReadPixels(new Rect(0, 0, 400, 300), 0, 0); tile.Apply();
                        sheet.SetPixels(i % 2 * 400, (3 - i / 2) * 300, 400, 300, tile.GetPixels());
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                sheet.Apply();
                File.WriteAllBytes(PolishReview + "/Candidates.png", sheet.EncodeToPNG());
                File.WriteAllText(PolishReview + "/Candidates.txt", report.ToString());
            }
            finally
            {
                RenderTexture.active = previous;
                Object.DestroyImmediate(target); Object.DestroyImmediate(sheet); Object.DestroyImmediate(tile);
                EditorSceneManager.ClosePreviewScene(scene);
            }
            CapturePolishedBattle("Before", false);
        }

        static GameObject PowerBurst(GameObject parent, string path, float scale, float speed)
        {
            if (!path.StartsWith(CfxPrefabs, StringComparison.Ordinal)) return AddPackBurst(parent, path, scale, speed);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!source) throw new Exception("Missing " + path);
            var root = Object.Instantiate(source, parent.transform, false);
            PrepareNewVfxCopy(root);
            root.transform.localPosition = Vector3.zero;
            root.transform.localScale = Vector3.one * scale;
            foreach (var particles in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.simulationSpeed = speed;
                main.maxParticles = Mathf.Min(main.maxParticles, 64);
                main.startLifetimeMultiplier = Mathf.Min(main.startLifetimeMultiplier, .8f);
            }
            foreach (var renderer in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
                renderer.sharedMaterials = renderer.sharedMaterials.Select(BattleEffectMaterial).ToArray();
            return root;
        }

        static GameObject BuildPowerEffect(GameObject previous, string name, int kind)
        {
            var root = Object.Instantiate(previous);
            root.name = name;
            try
            {
                if (kind == 0)
                    PowerBurst(root, PowerCandidates[7], .21f, 1.8f);
                else if (kind == 1)
                {
                    PowerBurst(root, PowerCandidates[0], .5f, 1.8f);
                    // Retain the Archer streaks and readable comic WHAM around a sharper core.
                    foreach (var child in root.GetComponentsInChildren<Transform>(true))
                        if (child.name.Contains("ArrowHit")) child.localScale *= 1.25f;
                }
                else
                {
                    PowerBurst(root, PowerCandidates[1], .45f, 1.65f);
                    foreach (var child in root.transform.Cast<Transform>().ToArray())
                        if (child.name.Contains("Magic rain")) child.localScale *= 1.15f;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, VfxFolder + "/" + name + ".prefab");
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Light PolishLight(Transform parent, string name, Color color, float intensity, float range)
        {
            var child = parent.Find(name);
            if (!child)
            {
                var root = new GameObject(name);
                Undo.RegisterCreatedObjectUndo(root, "Battle lighting");
                root.transform.SetParent(parent, false);
                child = root.transform;
            }
            var light = child.GetComponent<Light>();
            if (!light) light = Undo.AddComponent<Light>(child.gameObject);
            Undo.RecordObject(light, "Battle lighting");
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.cullingMask = ~(1 << 5);
            light.enabled = intensity > 0;
            return light;
        }

        [MenuItem("Tools/Battle/Apply closer camera and impact lighting")]
        public static void InstallBattlePolish()
        {
            Directory.CreateDirectory(PolishReview);
            var scene = SceneManager.GetSceneByPath(SfxScene);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(SfxScene, OpenSceneMode.Additive);
            var previousActive = SceneManager.GetActiveScene();
            BattleLightingRig rig = null;
            try
            {
                SceneManager.SetActiveScene(scene);
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).Single(c => c.CompareTag("MainCamera"));
                var director = camera.GetComponent<FrankCinematicCamera>();
                Undo.RecordObject(director, "Bring battle camera closer");
                director.battleDistanceScale = .90f;
                var vfx = game.battleVfx;
                Undo.RecordObject(vfx, "Stronger battle impacts");
                vfx.lightHit = BuildPowerEffect(AssetDatabase.LoadAssetAtPath<GameObject>(VfxFolder + "/MageLightHit.prefab"), "BattlePowerLightHit", 0);
                vfx.heavyHit = BuildPowerEffect(AssetDatabase.LoadAssetAtPath<GameObject>(VfxFolder + "/ArcherHeavyHit.prefab"), "BattlePowerHeavyHit", 1);
                vfx.groundImpact = BuildPowerEffect(AssetDatabase.LoadAssetAtPath<GameObject>(VfxFolder + "/MageGroundImpact.prefab"), "BattlePowerGroundImpact", 2);
                var sweep = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(VfxFolder + "/BladeSlash.prefab"));
                try
                {
                    sweep.name = "BattlePowerBladeSlash";
                    sweep.transform.localScale *= 1.2f;
                    vfx.bladeSlash = PrefabUtility.SaveAsPrefabAsset(sweep, VfxFolder + "/BattlePowerBladeSlash.prefab");
                }
                finally { Object.DestroyImmediate(sweep); }
                rig = game.GetComponent<BattleLightingRig>();
                if (!rig) rig = Undo.AddComponent<BattleLightingRig>(game.gameObject);
                Undo.RecordObject(rig, "Battle lighting rig");
                rig.battle = game; rig.battleCamera = camera; rig.vfx = vfx;
                rig.fill = PolishLight(game.transform, "Character warm fill", new Color(1, .85f, .68f), .16f, 6);
                rig.rim = PolishLight(game.transform, "Character cool rim", new Color(.4f, .65f, 1), .3f, 5);
                rig.impactLights = new[] {
                    PolishLight(game.transform, "Impact flash 1", Color.white, 0, 3.5f),
                    PolishLight(game.transform, "Impact flash 2", Color.white, 0, 3.5f)
                };
                foreach (var key in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)).Where(l => l.type == LightType.Directional))
                {
                    Undo.RecordObjects(new Object[] {key, key.transform}, "Battle key light");
                    key.transform.rotation = Quaternion.Euler(38, 205, 0);
                    key.color = new Color(1, .94f, .85f);
                    key.intensity = .95f;
                    key.shadows = LightShadows.Soft;
                    key.shadowStrength = .65f;
                    key.shadowBias = .04f;
                    key.shadowNormalBias = .15f;
                }
                foreach (var fighter in new[] {game.leftCombat, game.rightCombat})
                foreach (var material in fighter.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Where(m => m && AssetDatabase.GetAssetPath(m).StartsWith(LookMaterials, StringComparison.Ordinal)).Distinct())
                {
                    string backup = PolishReview + "/" + Path.GetFileName(AssetDatabase.GetAssetPath(material)) + ".before.txt";
                    if (!File.Exists(backup)) File.Copy(AssetDatabase.GetAssetPath(material), backup);
                    Undo.RecordObject(material, "Battle light response");
                    material.SetFloat("_LightContribution", .45f);
                    material.SetFloat("_ShadowEdgeSize", .085f);
                    material.SetFloat("_UnityShadowPower", .32f);
                    EditorUtility.SetDirty(material);
                }
                rig.InitializeRig(); rig.ClearFlashes();
                director.ResetView(); director.Apply(0, true);
                foreach (var component in new Object[] {director, vfx, rig}) EditorUtility.SetDirty(component);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new Exception("Could not save BattleScene polish.");
                File.WriteAllText(PolishReview + "/Setup.txt", "Battle camera dolly scale .82; authored orbit/lens and body/weapon framing retained.\nWarm key, warm fill, blue rim, two pooled cue-driven flashes; shadows enabled only while Battle rig is active, quality restored on disable.\nLight: contrast core + Mage + POW. Heavy: contrast shock burst/debris + Archer + WHAM. Ground: contrast ring/debris + Mage gold. Existing slash, WHOOSH and SMASH cues retained.\n");
            }
            finally
            {
                if (rig) rig.ShutdownRig();
                if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }

        public static void ValidateBattlePolish()
        {
            CapturePolishedBattle("After", true);
        }

        static void CapturePolishedBattle(string prefix, bool validate)
        {
            Directory.CreateDirectory(PolishReview);
            var scene = EditorSceneManager.OpenPreviewScene(SfxScene);
            var target = new RenderTexture(1280, 720, 24);
            var report = new StringBuilder();
            BattleLightingRig rig = null;
            Camera camera = null;
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                camera = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>()).Single(c => c.CompareTag("MainCamera"));
                var director = camera.GetComponent<FrankCinematicCamera>();
                camera.scene = scene; camera.targetTexture = target;
                foreach (var canvas in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Canvas>(true)))
                { canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
                game.leftCombat.Initialize(); game.rightCombat.Initialize();
                rig = game.GetComponent<BattleLightingRig>();
                int oldPixelCount = QualitySettings.pixelLightCount;
                var oldShadows = QualitySettings.shadows;
                float oldShadowDistance = QualitySettings.shadowDistance;
                var oldResolution = QualitySettings.shadowResolution;
                if (rig) rig.InitializeRig();
                game.roundManager.roundNumberText.text = "Turn 1";
                director.ResetView(); director.Apply(0, true);
                Canvas.ForceUpdateCanvases();
                CaptureBattleCamera(camera, PolishReview + "/" + prefix + "_Idle.png");
                var effects = new Dictionary<GameObject, float>();
                foreach (string moveName in new[] {"Light_1", "Heavy_6", "Heavy_Katana", "Heavy_Assassin"})
                foreach (bool ground in new[] {false, true})
                {
                    var attacker = game.leftCombat; var receiver = game.rightCombat;
                    var move = attacker.lightCombatMoves.Concat(attacker.heavyCombatMoves).Single(m => m.moveName == moveName);
                    var profile = game.battleVfx.timeline.FindMove(move);
                    var cue = ground ? profile.cues.LastOrDefault(c => c.finalLanding) : profile.cues.FirstOrDefault(c => c.group.EndsWith("hit", StringComparison.Ordinal));
                    if (cue == null) continue;
                    attacker.ResetCombat(); receiver.ResetCombat(); game.battleVfx.ResetForMatch(); effects.Clear();
                    attacker.transform.position = new Vector3(-.5f, 0, 0);
                    receiver.transform.position = attacker.transform.position + Vector3.right * move.attackRange;
                    Action<string, GameObject> observe = (id, root) => effects[root] = attacker.SourcePlayback.SampleTime;
                    game.battleVfx.EffectPlayed += observe;
                    try
                    {
                        if (!attacker.ExecuteAttack(move, receiver, ground)) throw new Exception("Rejected " + moveName);
                        var pair = attacker.SourcePlayback;
                        pair.EvaluateAt(cue.seconds);
                        game.battleVfx.AdvanceSequence(pair, cue.seconds);
                        if (validate && (rig.ActiveFlashCount == 0 || rig.ActiveFlashCount > 2)) throw new Exception("Missing or excessive impact lighting.");
                        foreach (float age in new[] {.045f, .12f, .28f})
                        {
                            pair.EvaluateAt(cue.seconds + age);
                            if (validate)
                            {
                                float scale = director.battleDistanceScale;
                                director.battleDistanceScale = 1; director.Apply(0, true);
                                Vector3 beforePosition = camera.transform.position;
                                Quaternion beforeAngle = camera.transform.rotation;
                                director.battleDistanceScale = scale; director.Apply(0, true);
                                float angleError = Quaternion.Angle(beforeAngle, camera.transform.rotation);
                                InspectBattleCameraFrame(camera, director);
                                if (angleError > .1f) throw new Exception("Dolly changed authored angle: " + moveName + " " + angleError);
                                float closer = Vector3.Dot(beforePosition - camera.transform.position, -camera.transform.forward);
                                if (closer < -.01f) throw new Exception("Camera moved farther away.");
                                report.AppendLine($"PASS {moveName} ground={ground} age={age}: angle error={angleError:F4}, dolly closer={closer:F3}m, safe body/weapon frame.");
                            }
                            else director.Apply(0, true);
                            foreach (var effect in effects.Where(e => e.Key && e.Key.activeSelf))
                            foreach (var particles in effect.Key.GetComponentsInChildren<ParticleSystem>())
                            {
                                particles.useAutoRandomSeed = false; particles.randomSeed = 321;
                                particles.Simulate(cue.seconds - effect.Value + age, false, true, true);
                            }
                            if (rig) rig.RefreshLighting(age == .045f ? age : age == .12f ? .075f : .16f);
                            Canvas.ForceUpdateCanvases();
                            string imagePath = PolishReview + "/" + prefix + "_" + moveName + (ground ? "_Ground_" : "_Hit_") + age.ToString("F3", CultureInfo.InvariantCulture) + ".png";
                            CaptureBattleCamera(camera, imagePath);
                            if (validate && moveName == "Heavy_6" && !ground && age == .045f)
                                CheckRenderedImpactLight(camera, rig, imagePath, report);
                        }
                        if (validate && rig.ActiveFlashCount != 0) throw new Exception("Flash did not decay.");
                        pair.Cancel();
                        if (validate && rig.ActiveFlashCount != 0) throw new Exception("Cancellation retained flashes.");
                    }
                    finally { game.battleVfx.EffectPlayed -= observe; }
                }
                if (validate)
                {
                    rig.ShutdownRig();
                    if (QualitySettings.pixelLightCount != oldPixelCount || QualitySettings.shadows != oldShadows ||
                        QualitySettings.shadowDistance != oldShadowDistance || QualitySettings.shadowResolution != oldResolution)
                        throw new Exception("Battle quality was not restored.");
                    report.AppendLine("PASS flash cues, two-light pool, decay, cancellation and quality restoration.");
                    File.WriteAllText(PolishReview + "/Validation.txt", report.ToString());
                }
            }
            catch (Exception exception) { File.WriteAllText(PolishReview + "/Validation.txt", report + "FAIL " + exception); throw; }
            finally
            {
                if (rig) rig.ShutdownRig();
                if (camera) camera.targetTexture = null;
                Object.DestroyImmediate(target);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void CheckRenderedImpactLight(Camera camera, BattleLightingRig rig, string withLightPath, StringBuilder report)
        {
            var enabled = rig.impactLights.Select(l => l.enabled).ToArray();
            string withoutPath = PolishReview + "/After_Heavy_6_Hit_WithoutFlash.png";
            try
            {
                foreach (var light in rig.impactLights) light.enabled = false;
                CaptureBattleCamera(camera, withoutPath);
            }
            finally
            {
                for (int i = 0; i < enabled.Length; i++) rig.impactLights[i].enabled = enabled[i];
            }
            var on = new Texture2D(2, 2); var off = new Texture2D(2, 2);
            try
            {
                on.LoadImage(File.ReadAllBytes(withLightPath)); off.LoadImage(File.ReadAllBytes(withoutPath));
                var bright = on.GetPixels32(); var dark = off.GetPixels32();
                int changed = 0, magenta = 0;
                for (int i = 0; i < bright.Length; i++)
                {
                    if (Mathf.Abs(bright[i].r - dark[i].r) + Mathf.Abs(bright[i].g - dark[i].g) + Mathf.Abs(bright[i].b - dark[i].b) > 8) changed++;
                    if (bright[i].r > 235 && bright[i].g < 25 && bright[i].b > 235) magenta++;
                }
                if (changed < 200) throw new Exception("Flash light has no visible contribution to battle rendering: " + changed);
                if (magenta > 0) throw new Exception("Magenta shader-error pixels in battle.");
                report.AppendLine($"PASS rendered impact lighting: {changed} pixels change when only flash lights are toggled; {magenta} error-magenta pixels.");
            }
            finally { Object.DestroyImmediate(on); Object.DestroyImmediate(off); }
        }
    }
}
