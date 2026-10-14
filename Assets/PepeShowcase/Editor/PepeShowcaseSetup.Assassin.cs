using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FrankRetarget;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using ZeroSpace.Showcase;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseSetup
    {
        const string AssassinRoot = "Assets/Frank_Slash_Pack/Assets/Animations/Frank_SlashPack_Assassin/";
        const string AssassinModel = "Assets/Frank_Slash_Pack/Assets/Meshes/Frank_Assassin_Skin.FBX";
        static readonly string[] AssassinControllers =
        {
            "Frank_Assassin_Combo04", "Frank_Assassin_Skill05", "Frank_Assassin_Combo03"
        };

        [MenuItem("Tools/Pepe Showcase/Update Assassin attacks")]
        public static void UpdateAttackList()
        {
            var library = AssetDatabase.LoadAssetAtPath<PepeShowcaseLibrary>(Root + "/ShowcaseLibrary.asset");
            if (!library) throw new InvalidOperationException("Create the showcase first.");
            var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/BattleScene.unity");
            try
            {
                var fighters = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<CharacterCombat>(true)).ToArray();
                ConfigureAttacks(library, fighters.Single(f => f.name == "Pepe"),
                    fighters.Single(f => f.name == "Mankey"));
                EditorUtility.SetDirty(library);
                AssetDatabase.SaveAssetIfDirty(library);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
            InstallWeaponVfx();
        }

        static void ConfigureAttacks(PepeShowcaseLibrary library, CharacterCombat pepe, CharacterCombat mankey)
        {
            var entries = new List<PepeShowcaseLibrary.Take>();
            foreach (string id in new[] { "Critical_Dual", "combo_03" })
            {
                var a = FindExistingMove(pepe, id);
                var b = FindExistingMove(mankey, id);
                entries.Add(new PepeShowcaseLibrary.Take
                {
                    title = id == "combo_03" ? "COMBO 03" : "CRITICAL DUAL",
                    animation = a.sourcePair.attack,
                    pepeDriver = a.sourcePair.attackerDriver,
                    mankeyDriver = b.sourcePair.attackerDriver,
                    grounding = a.grounding,
                    cues = AttackCues(pepe.battleSfx.bank, a, pepe.battleSfx.bank.FindMove(a)),
                    yawStart = -16,
                    yawEnd = -4,
                    cameraDistance = 3.4f
                });
            }
            var report = new StringBuilder();
            foreach (string name in AssassinControllers)
            {
                var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(
                    AssassinRoot + name + ".controller");
                if (!controller) throw new InvalidOperationException("Missing Animator: " + name);
                var clip = controller.animationClips.Distinct().Single();
                // The controller clips are animation-only FBXs. The skin contains the
                // matching skeleton, sword and authored slash meshes they animate.
                string modelPath = AssassinModel;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
                if (!model) throw new InvalidOperationException("Missing Assassin model: " + modelPath);
                var retained = model.GetComponentsInChildren<Renderer>(true)
                    .Where(r => IsNativeEffect(r.name) || r.name.IndexOf("sword", StringComparison.OrdinalIgnoreCase) >= 0)
                    .Select(r => r.name).Distinct().ToArray();
                if (retained.Length == 0) throw new InvalidOperationException("Missing native weapon: " + name);
                if (!AssetDatabase.IsValidFolder(Root + "/Drivers")) AssetDatabase.CreateFolder(Root, "Drivers");
                var drivers = new List<FrankTestDriver>();
                foreach (var fighter in new[] { pepe, mankey })
                {
                    string path = Root + "/Drivers/" + fighter.name + "_" + name + ".prefab";
                    FrankRetargetBuilder.BuildHumanoidStudyDriver(fighter.Animator, fighter.name,
                        modelPath, new[] { clip }, path, true, retained);
                    var contents = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        foreach (var renderer in contents.GetComponentsInChildren<Renderer>(true))
                            renderer.sharedMaterials = renderer.sharedMaterials
                                .Select(material => AssassinMaterial(material, IsNativeEffect(renderer.name))).ToArray();
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                    }
                    finally
                    {
                        PrefabUtility.UnloadPrefabContents(contents);
                    }
                    drivers.Add(AssetDatabase.LoadAssetAtPath<FrankTestDriver>(path));
                }
                var take = new PepeShowcaseLibrary.Take
                {
                    title = "ASSASSIN / " + name.Replace("Frank_Assassin_", "").ToUpperInvariant(),
                    animatorName = name,
                    nativeWeaponEffects = true,
                    animation = clip,
                    pepeDriver = drivers[0],
                    mankeyDriver = drivers[1],
                    yawStart = -16,
                    yawEnd = -4,
                    cameraDistance = 3.4f
                };
                take.cues = NativeCues(pepe, take, report);
                entries.Add(take);
                report.AppendLine(name + " -> " + AssetDatabase.GetAssetPath(clip) + " / " +
                    modelPath + " / " + clip.length.ToString("F3") +
                    "s; native renderers=" + string.Join(",", retained));
            }
            library.takes = entries.ToArray();
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/AssassinSetup.txt", report.ToString());
        }

        static CombatTripletData FindExistingMove(CharacterCombat fighter, string id) =>
            fighter.heavyCombatMoves.Single(move => move.sourcePair != null && move.sourcePair.attack &&
                (id == "combo_03" ? move.moveName == id : move.sourcePair.attack.name.Contains(id)));

        internal static bool IsNativeEffect(string name)
        {
            name = name.ToLowerInvariant();
            return name.Contains("fx") || name.Contains("treak") || name.Contains("trail");
        }

        static Material AssassinMaterial(Material source, bool effect)
        {
            if (!effect)
                return AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/BattleWeapons/Assassin_IceBlade.mat");
            string path = Root + "/Materials/Native_" + (source ? source.name : "AssassinGlow") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", source ? source.mainTexture : null);
            material.SetColor("_BaseColor", new Color(.35f, .85f, 1, .9f));
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 2);
            material.SetFloat("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", (int)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        static PepeShowcaseLibrary.Cue[] NativeCues(CharacterCombat fighter, PepeShowcaseLibrary.Take take,
            StringBuilder report)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var baked = new Mesh();
            try
            {
                var character = Object.Instantiate(fighter.Animator.gameObject).GetComponent<Animator>();
                SceneManager.MoveGameObjectToScene(character.gameObject, scene);
                FrankRetargetBuilder.StripStudyComponents(character.gameObject, false);
                character.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                var root = new GameObject("Native Assassin timing probe");
                SceneManager.MoveGameObjectToScene(root, scene);
                var actor = root.AddComponent<FrankTestActor>();
                actor.character = character;
                actor.ConfigureSource(take.pepeDriver, take.animation, true, true, false,
                    preserveCharacterScale: true);
                actor.activeDriver.gameObject.SetActive(true);
                var effects = actor.Pose.weaponRenderers.Where(r => IsNativeEffect(r.name)).ToArray();
                var cues = new List<PepeShowcaseLibrary.Cue>();
                bool previous = false;
                for (int sample = 0; sample <= Mathf.CeilToInt(take.animation.length * 60); sample++)
                {
                    float seconds = Mathf.Min(sample / 60f, take.animation.length);
                    actor.Evaluate(seconds);
                    bool visible = effects.Any(renderer => NativeEffectVisible(renderer, baked));
                    if (visible && !previous && seconds > .05f)
                    {
                        var sounds = new List<PepeShowcaseLibrary.Sound>();
                        AddSound(fighter.battleSfx.bank, "steel_swing", cues.Count, sounds, true);
                        cues.Add(new PepeShowcaseLibrary.Cue
                        {
                            seconds = seconds,
                            swing = true,
                            group = "steel_swing",
                            sounds = sounds.ToArray()
                        });
                        report.AppendLine(take.animatorName + " native slash at " + seconds.ToString("F3"));
                    }
                    previous = visible;
                }
                actor.Clear();
                if (cues.Count == 0) throw new InvalidOperationException("No animated slash windows: " + take.animatorName);
                return cues.ToArray();
            }
            finally
            {
                Object.DestroyImmediate(baked);
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        internal static bool NativeEffectVisible(Renderer renderer, Mesh baked)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                renderer.transform.lossyScale.sqrMagnitude < .000001f) return false;
            if (renderer is SkinnedMeshRenderer skin)
            {
                skin.BakeMesh(baked, false);
                var vertices = baked.vertices;
                var triangles = baked.triangles;
                for (int i = 0; i < triangles.Length; i += 3)
                    if (Vector3.Cross(vertices[triangles[i + 1]] - vertices[triangles[i]],
                        vertices[triangles[i + 2]] - vertices[triangles[i]]).sqrMagnitude > .00000001f)
                        return true;
                return false;
            }
            return renderer.bounds.size.sqrMagnitude > .000001f;
        }
    }

    public static partial class FrankRetargetBuilder
    {
        public static void UpdatePepeShowcaseAssassinAttacks() => PepeShowcaseSetup.UpdateAttackList();
    }
}
