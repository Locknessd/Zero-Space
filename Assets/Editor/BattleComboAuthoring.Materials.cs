using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using FrankRetarget;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class BattleComboAuthoring
    {
        internal static void RepairWeaponMaterials(GameManager game)
        {
            var report = new StringBuilder();
            var moves = game.leftCombat.lightCombatMoves.Concat(game.leftCombat.heavyCombatMoves)
                .Concat(game.rightCombat.lightCombatMoves).Concat(game.rightCombat.heavyCombatMoves).ToArray();
            var paths = moves.Where(m => m?.sourcePair != null).SelectMany(m => new UnityEngine.Object[]
                { m.sourcePair.attackerDriver, m.sourcePair.receiverDriver, m.sourcePair.attackerWeaponPrefab })
                .Where(o => o).Select(AssetDatabase.GetAssetPath).Distinct().ToArray();
            foreach (string path in paths)
            {
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                    continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var driver = root.GetComponent<FrankTestDriver>();
                    var renderers = driver ? driver.pose.weaponRenderers : root.GetComponentsInChildren<Renderer>(true);
                    bool changed = false;
                    foreach (var renderer in renderers)
                        changed |= RepairWeaponRenderer(renderer, report);
                    if (changed)
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }
            foreach (var fighter in new[] { game.leftCombat, game.rightCombat })
            foreach (var manager in fighter.GetComponentsInChildren<TrumpWeaponManager>(true))
            foreach (var weapon in new[] { manager.warriorShieldSet, manager.greatSwordSet, manager.spearSet,
                manager.katanaSet, manager.dualDaggersSet, manager.assassinSet, manager.twoHandedAxeSet })
            {
                if (!weapon)
                    continue;
                foreach (var renderer in weapon.GetComponentsInChildren<Renderer>(true))
                    RepairWeaponRenderer(renderer, report);
            }
            Directory.CreateDirectory(Report);
            File.WriteAllText(Report + "/WeaponMaterials.txt", report.ToString());
        }

        static bool RepairWeaponRenderer(Renderer renderer, StringBuilder report)
        {
            if (!renderer)
                throw new InvalidOperationException("Missing native weapon renderer.");
            var slots = renderer.sharedMaterials;
            var skin = renderer as SkinnedMeshRenderer;
            var filter = renderer.GetComponent<MeshFilter>();
            var mesh = skin ? skin.sharedMesh : filter ? filter.sharedMesh : null;
            int required = mesh ? mesh.subMeshCount : slots.Length;
            if (slots.Length < required || slots.Any(m => !m))
                throw new InvalidOperationException("Missing weapon material slot: " + renderer.name);
            bool changed = false;
            for (int i = 0; i < slots.Length; i++)
            {
                var material = slots[i];
                string path = AssetDatabase.GetAssetPath(material);
                string shader = material.shader ? material.shader.name : "MISSING";
                if (!shader.StartsWith("Battle/URP/", StringComparison.Ordinal) &&
                    !shader.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal))
                {
                    var target = Shader.Find("Battle/URP/Toon Surface");
                    if (!target)
                        throw new InvalidOperationException("Missing battle weapon shader.");
                    Texture texture = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                    Color tint = material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
                    if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    {
                        string owned = "Assets/DemoSence/Materials/Weapon_" + material.name + "_" +
                            AssetDatabase.AssetPathToGUID(path).Substring(0, 8) + ".mat";
                        var copy = AssetDatabase.LoadAssetAtPath<Material>(owned);
                        if (!copy)
                        {
                            copy = new Material(material);
                            AssetDatabase.CreateAsset(copy, owned);
                        }
                        slots[i] = material = copy;
                        path = owned;
                        changed = true;
                    }
                    material.shader = target;
                    material.shaderKeywords = Array.Empty<string>();
                    material.SetColor("_Color", tint);
                    material.SetColor("_BaseColor", tint);
                    material.SetTexture("_MainTex", texture);
                    material.SetTexture("_BaseMap", texture);
                    material.SetColor("_ShadowTint", new Color(.3f, .39f, .53f));
                    material.SetFloat("_SpecularStrength", .3f);
                    material.SetFloat("_Smoothness", .7f);
                    EditorUtility.SetDirty(material);
                    AssetDatabase.SaveAssetIfDirty(material);
                    report.AppendLine("REPAIRED " + path + ": " + shader + " -> " + target.name);
                }
                if (!material.shader || !material.shader.isSupported || ShaderUtil.ShaderHasError(material.shader))
                    throw new InvalidOperationException("Unsupported weapon shader: " + path);
                report.AppendLine("PASS " + renderer.name + " slot=" + i + " material=" + path +
                    " shader=" + material.shader.name);
            }
            if (changed)
            {
                renderer.sharedMaterials = slots;
                EditorUtility.SetDirty(renderer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
            return changed;
        }

        internal static GameObject CreateMuzzle(BattleVfxPlayer vfx)
        {
            string path = Root + "GunMuzzleFlash.prefab";
            var root = new GameObject("Gun muzzle flash");
            try
            {
                var particles = root.AddComponent<ParticleSystem>();
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particles.main;
                main.playOnAwake = false;
                main.loop = false;
                main.duration = .12f;
                main.startLifetime = .065f;
                main.startSpeed = .8f;
                main.startSize = .09f;
                main.startColor = new Color(1, .82f, .25f, 1);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var emission = particles.emission;
                emission.rateOverTime = 0;
                emission.SetBursts(new[] { new ParticleSystem.Burst(0, 5) });
                var shape = particles.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 15;
                shape.radius = .01f;
                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                renderer.sharedMaterial = vfx.lightHit.GetComponentInChildren<ParticleSystemRenderer>(true)
                    .sharedMaterial;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
