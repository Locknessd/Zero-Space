using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FrankRetarget.Editor
{
    public static partial class BattleComboAuthoring
    {
        internal static void ConfigureGunSwordPresentation(BattleVfxPlayer vfx)
        {
            vfx.gunMuzzleFlash = CreateMuzzle(vfx);
            InstallTrailStyle(vfx.weaponTrails);
            if (!vfx.impactVariants.Any(v => v.weapon == TrumpWeaponManager.WeaponType.GunSword))
            {
                var reference = vfx.impactVariants.First(v => v.weapon == TrumpWeaponManager.WeaponType.Katana);
                vfx.impactVariants = vfx.impactVariants.Append(new BattleVfxPlayer.WeaponImpactVariant
                {
                    weapon = TrumpWeaponManager.WeaponType.GunSword,
                    light = reference.light,
                    heavy = reference.heavy,
                    stab = reference.stab,
                    scale = reference.scale
                }).ToArray();
            }
            if (!vfx.bladeSlashVariants.Any(v => v.weapon == TrumpWeaponManager.WeaponType.GunSword))
            {
                var reference = vfx.bladeSlashVariants.First(v => v.weapon == TrumpWeaponManager.WeaponType.Katana);
                vfx.bladeSlashVariants = vfx.bladeSlashVariants.Append(new BattleVfxPlayer.WeaponSlashVariant
                {
                    weapon = TrumpWeaponManager.WeaponType.GunSword,
                    prefab = reference.prefab,
                    scale = reference.scale
                }).ToArray();
            }
        }

        public static void FinalizePresentation()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Configure presentation in Edit Mode.");
            var previous = SceneManager.GetActiveScene();
            var scene = SceneManager.GetSceneByPath(Battle);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened)
                scene = EditorSceneManager.OpenScene(Battle, OpenSceneMode.Additive);
            try
            {
                var game = scene.GetRootGameObjects()
                    .SelectMany(r => r.GetComponentsInChildren<GameManager>(true)).Single();
                var fighters = new[] { game.leftCombat, game.rightCombat };
                foreach (var fighter in fighters)
                foreach (var move in fighter.heavyCombatMoves.Where(m => m.moveName.StartsWith("combo_")))
                {
                    move.sourcePair.receiverFloorLift = Array.Empty<float>();
                    move.grounding = AssetDatabase.LoadAssetAtPath<FrankRetarget.FrankPairGrounding>(
                        Root + move.moveName + "_Grounding.asset");
                    EditorUtility.SetDirty(fighter);
                }
                CombatExpansionSceneActionSave.Save(scene, fighters, new[] { "combo_01", "combo_02", "combo_03" });
                var bank = game.battleVfx.timeline;
                foreach (var profile in bank.moves.Where(p => p.label == "combo_01"))
                {
                    var hit = profile.cues.First(c => c.hasContactPoint && c.contactSource == "Weapon");
                    hit.group = "stab_hit";
                    profile.cues.Last(c => c.seconds < hit.seconds && c.group.EndsWith("swing"))
                        .group = "thrust_swing";
                }
                EditorUtility.SetDirty(bank);
                AssetDatabase.SaveAssetIfDirty(bank);
                ConfigureGunSwordPresentation(game.battleVfx);
                SavePresentationFields(scene, game.battleVfx);
            }
            finally
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid() && previous.isLoaded)
                    SceneManager.SetActiveScene(previous);
            }
        }
    }
}
