using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        internal static void ValidateExecution10Presentation(BattleVfxPlayer vfx)
        {
            var geometry = DiscoverBladePresentationGeometry();
            var trails = vfx ? vfx.weaponTrails : null;
            if (!trails || !trails.material || !trails.material.shader ||
                !trails.material.shader.isSupported || trails.styles == null ||
                !trails.styles.Any(s => s != null && s.weapon == TrumpWeaponManager.WeaponType.Katana) ||
                trails.staticBlades == null || trails.excludedMeshes == null ||
                !trails.IsMeshEligible(geometry.blade) || geometry.sheaths.Any(trails.IsMeshEligible))
                throw new InvalidOperationException("Existing calibrated Samurai weapon trail configuration required.");
            var blade = trails.staticBlades.Single(b => b != null && b.mesh == geometry.blade);
            if (Vector3.Distance(blade.bladeBase, geometry.bladeBase) > .00001f ||
                Vector3.Distance(blade.bladeTip, geometry.bladeTip) > .00001f ||
                blade.baseBones == null || blade.baseBones.Length != 0 ||
                blade.tipBones == null || blade.tipBones.Length != 0)
                throw new InvalidOperationException("Existing native Samurai blade endpoints differ from source geometry.");
            var impact = vfx.impactVariants.Single(v => v != null && v.weapon == TrumpWeaponManager.WeaponType.Katana);
            // BattleVfxPlayer.ContactPrefab uses the configured Katana light impact
            // for stab_hit when that weapon has no separate stab override.
            // Require that explicit weapon asset rather than accepting the shared fallback.
            var stabImpact = impact.stab ? impact.stab : impact.light;
            var assets = new UnityEngine.Object[]
                { vfx.landingDust, vfx.groundImpact, vfx.thrustSwing, stabImpact, trails.material };
            string[] labels = { "landingDust", "groundImpact", "thrustSwing", "Katana resolved stab", "trailMaterial" };
            for (int index = 0; index < assets.Length; index++)
            {
                var asset = assets[index];
                if (!asset || !EditorUtility.IsPersistent(asset) || EditorUtility.IsDirty(asset))
                    throw new InvalidOperationException("Missing or dirty existing Execution10 presentation asset: " +
                        labels[index] + (asset ? "; name=" + asset.name + "; path=" + AssetDatabase.GetAssetPath(asset) +
                            "; persistent=" + EditorUtility.IsPersistent(asset) + "; dirty=" +
                            EditorUtility.IsDirty(asset) : "; missing"));
            }
        }
    }
}
