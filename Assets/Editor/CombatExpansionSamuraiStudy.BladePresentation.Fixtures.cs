using System;
using System.Reflection;
using System.Text;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        sealed class BladePresentationFixture : IDisposable
        {
            readonly Scene scene;
            readonly GameObject root;
            public readonly BattleWeaponTrails trails;
            public readonly BattleWeaponTrails copy;
            public readonly BattleVfxPlayer vfx;
            public readonly Mesh otherMesh;
            public readonly Renderer blade;
            public readonly Renderer sheath;
            public readonly SkinnedMeshRenderer skin;
            public readonly Renderer legacy;
            public readonly Renderer shield;
            public readonly Renderer casing;
            public readonly Func<Renderer, bool> trailCandidate;
            public readonly Func<Renderer, bool> bladeCandidate;
            public readonly Func<Renderer, bool> isShield;

            public BladePresentationFixture(BladePresentationGeometry geometry)
            {
                scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    root = new GameObject("Isolated Samurai blade presentation validation");
                    root.hideFlags = HideFlags.HideAndDontSave;
                    SceneManager.MoveGameObjectToScene(root, scene);
                    trails = root.AddComponent<BattleWeaponTrails>();
                    vfx = root.AddComponent<BattleVfxPlayer>();
                    vfx.weaponTrails = trails;
                    copy = Child("Serialization copy").AddComponent<BattleWeaponTrails>();
                    otherMesh = new Mesh { name = geometry.sheaths[0].name, hideFlags = HideFlags.HideAndDontSave };
                    blade = StaticRenderer("BladeR", geometry.blade);
                    sheath = StaticRenderer("Sword_Hold", geometry.sheaths[0]);
                    skin = Child("Skinned sheath").AddComponent<SkinnedMeshRenderer>();
                    skin.sharedMesh = geometry.sheaths[1];
                    legacy = StaticRenderer("Sword_Hold", otherMesh);
                    shield = StaticRenderer("SHIELD", otherMesh);
                    casing = StaticRenderer("Weapon_CASE", otherMesh);
                    trailCandidate = PresentationPredicate(trails, "Usable");
                    bladeCandidate = PresentationPredicate(vfx, "IsBlade");
                    var method = typeof(BattleWeaponTrails).GetMethod("IsShield",
                        BindingFlags.NonPublic | BindingFlags.Static);
                    isShield = (Func<Renderer, bool>)method.CreateDelegate(typeof(Func<Renderer, bool>));
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            GameObject Child(string name)
            {
                var child = new GameObject(name);
                child.hideFlags = HideFlags.HideAndDontSave;
                child.transform.SetParent(root.transform, false);
                return child;
            }

            Renderer StaticRenderer(string name, Mesh mesh)
            {
                var child = Child(name);
                child.AddComponent<MeshFilter>().sharedMesh = mesh;
                return child.AddComponent<MeshRenderer>();
            }

            public void Dispose()
            {
                if (root)
                    Object.DestroyImmediate(root);
                if (otherMesh)
                    Object.DestroyImmediate(otherMesh);
                if (scene.IsValid())
                    EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static Func<Renderer, bool> PresentationPredicate(object instance, string name)
        {
            var method = instance.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
            return (Func<Renderer, bool>)method.CreateDelegate(typeof(Func<Renderer, bool>), instance);
        }

        static void ValidatePresentationPredicates(BladePresentationFixture fixture,
            BladePresentationGeometry geometry, StringBuilder report)
        {
            foreach (var exclusions in new[] { null, Array.Empty<Mesh>(), new Mesh[] { null } })
            {
                fixture.trails.excludedMeshes = exclusions;
                RequirePresentation(fixture.trailCandidate(fixture.sheath) && fixture.bladeCandidate(fixture.sheath),
                    "Null/empty configuration must preserve the previous Sword_Hold eligibility.");
                RequirePresentation(fixture.trails.IsMeshEligible(null) &&
                    !fixture.trails.IsRendererMeshEligible(null), "Null mesh/renderer contract failed.");
            }
            fixture.trails.excludedMeshes = geometry.sheaths;
            foreach (var renderer in new Renderer[] { fixture.sheath, fixture.skin })
                RequirePresentation(!fixture.trailCandidate(renderer) && !fixture.bladeCandidate(renderer),
                    "Explicitly excluded static/skinned mesh remained eligible.");
            fixture.skin.sharedMesh = geometry.blade;
            RequirePresentation(fixture.trailCandidate(fixture.skin) && fixture.bladeCandidate(fixture.skin),
                "An eligible skinned blade was rejected.");
            fixture.skin.sharedMesh = geometry.sheaths[1];
            fixture.sheath.name = "Renamed native renderer";
            RequirePresentation(!fixture.trailCandidate(fixture.sheath) && !fixture.bladeCandidate(fixture.sheath),
                "Sheath exclusion must depend on mesh identity regardless of renderer name.");
            fixture.sheath.name = "Sword_Hold";
            foreach (var renderer in new[] { fixture.blade, fixture.legacy })
                RequirePresentation(fixture.trailCandidate(renderer) && fixture.bladeCandidate(renderer),
                    "Native blade or unrelated mesh with the same sheath name became ineligible.");
            RequirePresentation(fixture.trailCandidate(fixture.shield) && fixture.isShield(fixture.shield) &&
                !fixture.bladeCandidate(fixture.shield), "Shield trail classification changed.");
            RequirePresentation(!fixture.trailCandidate(fixture.casing) && !fixture.bladeCandidate(fixture.casing),
                "Legacy case exclusion changed.");
            fixture.blade.enabled = false;
            RequirePresentation(!fixture.trailCandidate(fixture.blade) && !fixture.bladeCandidate(fixture.blade),
                "Disabled renderer became eligible.");
            fixture.blade.enabled = true;
            fixture.blade.gameObject.SetActive(false);
            RequirePresentation(!fixture.trailCandidate(fixture.blade) && !fixture.bladeCandidate(fixture.blade),
                "Inactive renderer became eligible.");
            fixture.blade.gameObject.SetActive(true);
            fixture.vfx.weaponTrails = null;
            RequirePresentation(fixture.bladeCandidate(fixture.sheath),
                "Absent trail configuration must preserve legacy blade selection.");
            fixture.vfx.weaponTrails = fixture.trails;
            report.AppendLine("PASS shared static/skinned exclusion, null/empty config, unrelated same-name mesh.");
            report.AppendLine("PASS trail and VFX predicates: case, shield, active/enabled checks.");
        }
    }
}
