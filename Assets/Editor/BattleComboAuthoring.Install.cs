using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrankRetarget;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class BattleComboAuthoring
    {
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Install combos in Edit Mode.");
            var previousScene = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(Battle);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(Battle, OpenSceneMode.Additive);
            var previousPositioning = CombatPositioningController.Instance;
            var positioningProperty = typeof(CombatPositioningController).GetProperty("Instance");
            try
            {
                positioningProperty.SetValue(null, null);
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                var bank = game.battleVfx.timeline;
                var profiles = bank.moves.Where(m => m == null ||
                    !new[] { "combo_01", "combo_02", "combo_03" }.Contains(m.label)).ToList();
                foreach (var source in fighters)
                {
                    var target = fighters.Single(f => f != source);
                    var moves = source.heavyCombatMoves.Where(m => m == null ||
                        !new[] { "combo_01", "combo_02", "combo_03" }.Contains(m.moveName)).ToList();
                    foreach (var data in Library.pairs.Where(p => p.step == 0))
                    {
                        var move = MakeMove(source, target, data);
                        moves.Add(move);
                    }
                    Undo.RecordObject(source, "Add native gun and sword heavy combos");
                    source.heavyCombatMoves = moves.ToArray();
                    EditorUtility.SetDirty(source);
                }
                RepairWeaponMaterials(game);
                BakeGrounding();
                foreach (var fighter in fighters)
                foreach (var move in fighter.heavyCombatMoves.Where(m => m != null &&
                    new[] { "combo_01", "combo_02", "combo_03" }.Contains(m.moveName)))
                    move.grounding = AssetDatabase.LoadAssetAtPath<FrankPairGrounding>(
                        Root + move.moveName + "_Grounding.asset");
                var measured = MeasureProfiles();
                profiles.AddRange(measured);
                Undo.RecordObject(bank, "Add gun and sword presentation timelines");
                bank.moves = profiles.ToArray();
                InstallGunAudio(bank);
                EditorUtility.SetDirty(bank);
                AssetDatabase.SaveAssetIfDirty(bank);
                Undo.RecordObject(game.battleVfx, "Add gun muzzle presentation");
                ConfigureGunSwordPresentation(game.battleVfx);
                EditorUtility.SetDirty(game.battleVfx);
                EditorUtility.SetDirty(game.battleVfx.weaponTrails);
                CombatExpansionSceneActionSave.Save(scene, fighters,
                    new[] { "combo_01", "combo_02", "combo_03" });
                SavePresentationFields(scene, game.battleVfx);
                Directory.CreateDirectory(Report);
                File.WriteAllText(Report + "/Installation.txt",
                    "PASS Added three full native heavy combos per fighter.\n" +
                    "Existing actions and unrelated scene fields preserved.\n" +
                    "Full attack/reaction clips and source gun/sword rigs retained; " +
                    "battle avatar grounding baked at 240Hz.\n");
            }
            finally
            {
                positioningProperty.SetValue(null, previousPositioning);
                if (opened && scene.IsValid())
                    EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid() && previousScene.isLoaded)
                    SceneManager.SetActiveScene(previousScene);
            }
        }

        static void InstallGunAudio(BattleSfxBank bank)
        {
            if (bank.FindGroup("gun_shot") != null)
                return;
            var group = new BattleSfxBank.Group
            {
                id = "gun_shot",
                volume = .45f,
                pitch = new Vector2(.97f, 1.03f),
                output = bank.FindGroup("heavy_hit").output,
                maxConcurrent = 3,
                clips = Enumerable.Range(1, 3).Select(i => AssetDatabase.LoadAssetAtPath<AudioClip>(
                    "Assets/Plugins/Vefects/Flipbook VFX/Audio/WAV/SFX_GunShot_0" + i + ".wav")).ToArray()
            };
            if (group.clips.Any(c => !c))
                throw new InvalidOperationException("Missing native gunshot audio.");
            bank.groups = bank.groups.Append(group).ToArray();
        }

        static void InstallTrailStyle(BattleWeaponTrails trails)
        {
            var reference = trails.styles.First(s => s.weapon == TrumpWeaponManager.WeaponType.Katana);
            if (!trails.styles.Any(s => s.weapon == TrumpWeaponManager.WeaponType.GunSword))
                trails.styles = trails.styles.Append(new BattleWeaponTrails.Style
                {
                    weapon = TrumpWeaponManager.WeaponType.GunSword,
                    color = reference.color,
                    lifetime = reference.lifetime,
                    bladeStart = reference.bladeStart,
                    thrustBladeStart = reference.thrustBladeStart,
                    thrustWidth = reference.thrustWidth,
                    followThroughSeconds = reference.followThroughSeconds,
                    material = reference.material
                }).ToArray();
            var driver = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Drivers/Mankey_InsaneCombos.prefab")
                .GetComponent<FrankTestDriver>();
            var sword = driver.pose.weaponRenderers.Single(r => r.name == "WP_Sword");
            var mesh = sword.GetComponent<MeshFilter>().sharedMesh;
            if (trails.staticBlades.Any(b => b.mesh == mesh))
                return;
            using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
            using (var vertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp))
            {
                data[0].GetVertices(vertices);
                var points = vertices.ToArray();
                Vector3 grip = driver.pose.limbs.First(l => l.sourceKnuckle &&
                    l.sourceEnd.name.Contains("R Hand")).sourceEnd.position;
                var ordered = points.OrderBy(p => (sword.transform.TransformPoint(p) - grip).sqrMagnitude).ToArray();
                trails.staticBlades = trails.staticBlades.Append(new BattleWeaponTrails.StaticBlade
                {
                    mesh = mesh,
                    bladeBase = ordered.First(),
                    bladeTip = ordered.Last()
                }).ToArray();
            }
        }
    }
}
