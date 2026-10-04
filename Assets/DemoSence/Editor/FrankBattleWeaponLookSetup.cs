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
        const string WeaponLookReview = "GeneratedAssets/BattleWeaponReview";
        const string WeaponLookAssets = "Assets/Materials/BattleWeapons";
        const string WeaponLookDrivers = "Assets/DemoSence/BattleWeapons";

        [MenuItem("Tools/Battle/Repair and brighten weapon materials")]
        public static void RepairBattleWeaponLooks()
        {
            Directory.CreateDirectory(WeaponLookReview);
            if (!File.Exists(WeaponLookReview + "/BattleSceneBefore.unity.txt"))
                File.Copy(SfxScene, WeaponLookReview + "/BattleSceneBefore.unity.txt");
            if (!File.Exists(WeaponLookReview + "/BeforeValidation.txt")) CaptureWeaponLooks("Before");
            Directory.CreateDirectory(WeaponLookAssets);
            Directory.CreateDirectory(WeaponLookDrivers);
            AssetDatabase.Refresh();
            var shader = Shader.Find("FlatKit/Stylized Surface With Outline");
            if (!shader || !shader.isSupported || ShaderUtil.ShaderHasError(shader))
                throw new Exception("The Built-in FlatKit weapon shader is unavailable.");
            var report = new StringBuilder();
            var variants = new Dictionary<FrankTestDriver, FrankTestDriver>();
            ComicScene((game, camera) =>
            {
                foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
                {
                    var moves = fighter.lightCombatMoves.Concat(fighter.heavyCombatMoves).ToArray();
                    foreach (var move in moves.Where(m => m.weapon != TrumpWeaponManager.WeaponType.None))
                        move.sourcePair.attackerDriver = WeaponLookVariant(move.sourcePair.attackerDriver, move.weapon, shader, variants, report);
                    foreach (var binding in fighter.weaponRig.bindings)
                        binding.driver = WeaponLookVariant(binding.driver, binding.weapon, shader, variants, report);
                    EditorUtility.SetDirty(fighter);
                    EditorUtility.SetDirty(fighter.weaponRig);
                }
            });
            report.AppendLine("PASS saved Battle-only prefab variants. Source meshes, bone bindings, clips, transforms and camera settings retained.");
            File.WriteAllText(WeaponLookReview + "/Install.txt", report.ToString());
            CaptureWeaponLooks("After");
        }

        static FrankTestDriver WeaponLookVariant(FrankTestDriver input, TrumpWeaponManager.WeaponType weapon,
            Shader shader, Dictionary<FrankTestDriver, FrankTestDriver> variants, StringBuilder report)
        {
            var source = input;
            if (AssetDatabase.GetAssetPath(source).StartsWith(WeaponLookDrivers + "/", StringComparison.Ordinal))
                source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(source);
            if (!source || !source.pose) throw new Exception("Missing native weapon driver: " + weapon);
            if (variants.TryGetValue(source, out var existing)) return existing;
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source.gameObject);
            try
            {
                var driver = root.GetComponent<FrankTestDriver>();
                foreach (var renderer in driver.pose.weaponRenderers)
                {
                    var mesh = WeaponLookMesh(renderer);
                    if (!mesh || mesh.subMeshCount < 1) throw new Exception("Weapon mesh missing: " + renderer.name);
                    var oldSlots = renderer.sharedMaterials;
                    var slots = new Material[mesh.subMeshCount];
                    for (int i = 0; i < slots.Length; i++)
                    {
                        var original = i < oldSlots.Length ? oldSlots[i] : null;
                        string role = WeaponLookRole(weapon, renderer.name, i);
                        string materialPath = WeaponLookAssets + "/" + role + ".mat";
                        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                        if (!material)
                        {
                            material = new Material(shader) { name = "Battle " + role.Replace('_', ' ') };
                            AssetDatabase.CreateAsset(material, materialPath);
                        }
                        ConfigureWeaponLookMaterial(material, original, shader, role);
                        slots[i] = material;
                    }
                    renderer.sharedMaterials = slots;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    report.AppendLine(source.name + "/" + renderer.name + ": " + string.Join(", ", slots.Select(m => m.name)));
                }
                string path = WeaponLookDrivers + "/Battle_" + source.name + ".prefab";
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                if (!saved || !prefab) throw new Exception("Could not save weapon look variant: " + path);
                var result = prefab.GetComponent<FrankTestDriver>();
                // Material changes must never replace the calibrated mesh or animation.
                if (!result.pose.sourceClips.SequenceEqual(source.pose.sourceClips) ||
                    result.pose.weaponRenderers.Length != source.pose.weaponRenderers.Length)
                    throw new Exception("Weapon variant changed its authored animation: " + path);
                for (int i = 0; i < result.pose.weaponRenderers.Length; i++)
                    if (WeaponLookMesh(result.pose.weaponRenderers[i]) != WeaponLookMesh(source.pose.weaponRenderers[i]))
                        throw new Exception("Weapon variant changed its native mesh: " + path);
                variants.Add(source, result);
                return result;
            }
            finally { Object.DestroyImmediate(root); }
        }

        static Mesh WeaponLookMesh(Renderer renderer) => renderer is SkinnedMeshRenderer skin
            ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;

        public static void TuneBattleWeaponHighlights()
        {
            var shader = Shader.Find("FlatKit/Stylized Surface With Outline");
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { WeaponLookAssets }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                ConfigureWeaponLookMaterial(material, null, shader, Path.GetFileNameWithoutExtension(path));
            }
            AssetDatabase.SaveAssets();
            CaptureWeaponLooks("After");
        }

        static string WeaponLookRole(TrumpWeaponManager.WeaponType weapon, string renderer, int slot)
        {
            switch (weapon)
            {
                case TrumpWeaponManager.WeaponType.WarriorShield: return renderer == "Shield" ? "Shield_Gold" : "Warrior_Steel";
                case TrumpWeaponManager.WeaponType.GreatSword: return "GreatSword_Steel";
                case TrumpWeaponManager.WeaponType.Spear: return slot == 0 ? "Spear_Wood" : "Spear_Steel";
                case TrumpWeaponManager.WeaponType.Katana: return slot == 0 ? "Katana_WrapAndScabbard" : "Katana_ChampagneBlade";
                case TrumpWeaponManager.WeaponType.DualDaggers: return "DualDaggers_IceSteel";
                case TrumpWeaponManager.WeaponType.Assassin: return slot == 0 ? "Assassin_Grip" : "Assassin_IceBlade";
                case TrumpWeaponManager.WeaponType.TwoHandedAxe: return "Axe_Brass";
                default: throw new Exception("Unknown weapon material: " + weapon);
            }
        }

        static void ConfigureWeaponLookMaterial(Material material, Material source, Shader shader, string role)
        {
            bool grip = role.Contains("Grip") || role.Contains("Wrap") || role.Contains("Wood");
            bool gold = role.Contains("Gold") || role.Contains("Brass") || role.Contains("Champagne");
            Color tint = role.Contains("Wrap") ? new Color(.3f, .4f, .57f) :
                role.Contains("Grip") ? new Color(.38f, .52f, .65f) :
                role.Contains("Wood") ? new Color(.8f, .54f, .29f) :
                role.Contains("Champagne") ? new Color(1, .92f, .64f) :
                gold ? new Color(1, .73f, .28f) :
                role.Contains("Ice") ? new Color(.65f, .91f, 1) : new Color(.86f, .93f, 1);
            material.shader = shader;
            material.shaderKeywords = Array.Empty<string>();
            // FlatKit accumulates the Battle key/fill/rim lights. Reserve headroom
            // in each pass so the blade stays colored and its bevels remain visible.
            float exposure = grip ? .7f : .55f;
            material.SetColor("_Color", new Color(tint.r * exposure, tint.g * exposure, tint.b * exposure, 1));
            material.SetColor("_ColorDim", new Color(tint.r * exposure * .46f, tint.g * exposure * .58f, tint.b * exposure * .72f, 1));
            material.SetFloat("_CelPrimaryMode", 1); material.EnableKeyword("_CELPRIMARYMODE_SINGLE");
            material.SetFloat("_SelfShadingSize", .4f); material.SetFloat("_ShadowEdgeSize", .085f);
            material.SetFloat("_Flatness", 1); material.SetFloat("_LightContribution", .18f);
            material.SetFloat("_UnityShadowMode", 1); material.EnableKeyword("_UNITYSHADOWMODE_MULTIPLY");
            material.SetFloat("_UnityShadowPower", .18f);
            material.SetFloat("_TextureBlendingMode", 0); material.EnableKeyword("_TEXTUREBLENDINGMODE_MULTIPLY");
            material.SetFloat("_TextureImpact", 1);
            foreach (string property in new[] { "_MainTex", "_BumpMap" })
            {
                if (!source || !source.HasProperty(property) || !source.GetTexture(property)) continue;
                material.SetTexture(property, source.GetTexture(property));
                material.SetTextureScale(property, source.GetTextureScale(property));
                material.SetTextureOffset(property, source.GetTextureOffset(property));
            }
            material.SetFloat("_SpecularEnabled", grip ? 0 : 1);
            if (!grip) material.EnableKeyword("DR_SPECULAR_ON");
            material.SetColor("_FlatSpecularColor", gold ? new Color(.64f, .58f, .37f) : new Color(.55f, .65f, .72f));
            material.SetFloat("_FlatSpecularSize", .055f); material.SetFloat("_FlatSpecularEdgeSmoothness", .055f);
            material.SetFloat("_RimEnabled", 1); material.EnableKeyword("DR_RIM_ON");
            Color rim = grip ? Color.Lerp(tint, Color.white, .15f) * .7f : gold ? new Color(.7f, .58f, .28f) : new Color(.5f, .7f, .82f);
            rim.a = 1;
            material.SetColor("_FlatRimColor", rim);
            material.SetFloat("_FlatRimSize", grip ? .12f : .18f);
            material.SetFloat("_FlatRimEdgeSmoothness", .1f); material.SetFloat("_FlatRimLightAlign", .15f);
            material.SetFloat("_OutlineWidth", .9f); material.SetFloat("_OutlineDepthOffset", 0);
            material.SetColor("_OutlineColor", new Color(.075f, .095f, .14f));
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
        }

        static void CaptureWeaponLooks(string stage)
        {
            Directory.CreateDirectory(WeaponLookReview + "/" + stage);
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
                int cases = 0, slots = 0;
                foreach (var attacker in fighters)
                foreach (var move in attacker.lightCombatMoves.Concat(attacker.heavyCombatMoves).Where(m => m.weapon != TrumpWeaponManager.WeaponType.None))
                {
                    var receiver = fighters.Single(f => f != attacker);
                    foreach (var fighter in fighters) fighter.ResetCombat();
                    bool right = attacker == game.rightCombat;
                    attacker.transform.position = new Vector3(right ? .5f : -.5f, 0, 0);
                    receiver.transform.position = attacker.transform.position + Vector3.right * (right ? -move.attackRange : move.attackRange);
                    if (!attacker.ExecuteAttack(move, receiver)) throw new Exception("Weapon preview attack rejected: " + move.moveName);
                    var playback = attacker.SourcePlayback;
                    var cue = game.battleVfx.timeline.FindMove(move).cues.First(c => c.group == "blade_swing" || c.group == "heavy_swing");
                    playback.EvaluateAt(cue.seconds);
                    foreach (var renderer in playback.AttackerActor.Pose.weaponRenderers)
                    {
                        var mesh = WeaponLookMesh(renderer);
                        if (renderer.sharedMaterials.Length != mesh.subMeshCount || renderer.sharedMaterials.Any(m => !m || !m.shader || !m.shader.isSupported || ShaderUtil.ShaderHasError(m.shader)))
                            throw new Exception("Missing material or shader: " + attacker.name + "/" + renderer.name);
                        if (stage == "After" && renderer.sharedMaterials.Any(m => !AssetDatabase.GetAssetPath(m).StartsWith(WeaponLookAssets + "/", StringComparison.Ordinal)))
                            throw new Exception("Native Battle weapon retained its default material: " + renderer.name);
                        slots += renderer.sharedMaterials.Length;
                    }
                    lighting.RefreshLighting(0);
                    var director = camera.GetComponent<FrankCinematicCamera>(); director.ResetView(); director.Apply(0, true);
                    Canvas.ForceUpdateCanvases();
                    string image = WeaponLookReview + "/" + stage + "/" + attacker.name + "_" + move.moveName + ".png";
                    CaptureBattleCamera(camera, image);
                    report.AppendLine("PASS " + attacker.name + " " + move.moveName + " " + move.weapon + ": complete supported materials; native pose=" + cue.seconds + "; " + image);
                    playback.Cancel(); cases++;
                }
                report.AppendLine("PASS " + cases + " armed moves, " + slots + " material slots including duplicate/scabbard meshes; native Battle lighting and unchanged camera.");
                File.WriteAllText(WeaponLookReview + "/" + stage + "Validation.txt", report.ToString());
            }
            finally
            {
                if (lighting) lighting.ShutdownRig(); if (camera) camera.targetTexture = null;
                Object.DestroyImmediate(target); EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        public static void AuditBattleWeaponLooks()
        {
            Directory.CreateDirectory(WeaponLookReview);
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            var report = new StringBuilder();
            try
            {
                var game = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<GameManager>(true)).Single();
                foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
                {
                    var drivers = fighter.lightCombatMoves.Concat(fighter.heavyCombatMoves)
                        .Where(m => m.weapon != TrumpWeaponManager.WeaponType.None)
                        .Select(m => m.sourcePair.attackerDriver)
                        .Concat(fighter.weaponRig.bindings.Select(b => b.driver)).Distinct();
                    foreach (var driver in drivers)
                    {
                        report.AppendLine(fighter.name + " / " + AssetDatabase.GetAssetPath(driver));
                        foreach (var renderer in driver.pose.weaponRenderers)
                        {
                            var mesh = renderer is SkinnedMeshRenderer skin ? skin.sharedMesh : renderer.GetComponent<MeshFilter>()?.sharedMesh;
                            report.AppendLine("  " + AnimationUtility.CalculateTransformPath(renderer.transform, driver.transform) +
                                ": submeshes=" + (mesh ? mesh.subMeshCount : 0) + ", mesh=" + AssetDatabase.GetAssetPath(mesh));
                            var materials = renderer.sharedMaterials;
                            for (int i = 0; i < materials.Length; i++)
                            {
                                var material = materials[i];
                                report.AppendLine("    slot " + i + ": " + (material ? material.name + " / " + AssetDatabase.GetAssetPath(material) +
                                    " / shader=" + material.shader.name + " supported=" + material.shader.isSupported +
                                    " color=" + (material.HasProperty("_Color") ? material.color.ToString() : "none") +
                                    " tex=" + AssetDatabase.GetAssetPath(material.mainTexture) : "MISSING"));
                            }
                        }
                    }
                }
            }
            finally
            {
                File.WriteAllText(WeaponLookReview + "/BeforeMaterials.txt", report.ToString());
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
