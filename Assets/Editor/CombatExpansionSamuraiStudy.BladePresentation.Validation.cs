using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        public static void ValidateBladePresentationConfiguration()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            string output = BladeOutput + "/Presentation";
            Directory.CreateDirectory(output);
            string status = output + "/Status.txt";
            File.WriteAllText(status, "RUNNING isolated blade presentation configuration validation.\n");
            try
            {
                var geometry = DiscoverBladePresentationGeometry();
                var report = new StringBuilder(geometry.Describe());
                using var fixture = new BladePresentationFixture(geometry);
                ValidatePresentationPredicates(fixture, geometry, report);
                ValidatePresentationConfiguration(fixture, geometry, report);
                ValidatePresentationAllocations(fixture, geometry, report);
                report.AppendLine("Transient preview-scene fixtures; native source assets are read only.");
                report.AppendLine("No BattleScene save, gameplay playback, trail visuals or effect-direction claims.");
                File.WriteAllText(output + "/Report.txt", report.ToString());
                File.WriteAllText(status, "PASS isolated blade presentation configuration validation.\n");
                Debug.Log("Samurai blade presentation configuration validated: " + output + "/Report.txt");
            }
            catch (Exception error)
            {
                File.WriteAllText(status, "FAILED: " + error + "\n");
                throw;
            }
        }

        static void ValidatePresentationConfiguration(BladePresentationFixture fixture,
            BladePresentationGeometry geometry, StringBuilder report)
        {
            var trails = fixture.trails;
            // Serialization requires a persistent asset reference. The separate same-name
            // predicate fixture deliberately uses a transient DontSave mesh.
            var preservedMesh = PresentationSourceMesh(AssetDatabase.LoadAssetAtPath<GameObject>(
                AssetDatabase.GUIDToAssetPath(Guids[1])), "BladeR", Guids[1] + ":4300002");
            var legacy = new BattleWeaponTrails.StaticBlade
            {
                mesh = preservedMesh,
                bladeBase = Vector3.one,
                bladeTip = Vector3.up * 3,
                baseBones = new[] { new BattleWeaponTrails.BonePoint { bone = 2, point = Vector3.left, weight = 1 } }
            };
            trails.staticBlades = new[] { legacy, null, new BattleWeaponTrails.StaticBlade { mesh = geometry.blade } };
            trails.excludedMeshes = new[] { preservedMesh, null, geometry.sheaths[0] };
            ConfigureBladePresentation(trails);
            RequirePresentation(trails.staticBlades.Length == 3 && trails.staticBlades[0] == legacy &&
                trails.staticBlades[1] == null, "Unrelated calibrated blades and null entries must remain intact.");
            RequirePresentation(trails.excludedMeshes.Length == 4 && trails.excludedMeshes[0] == preservedMesh &&
                trails.excludedMeshes[1] == null, "Existing exclusions must remain intact without duplicate sheaths.");
            RequirePresentation(legacy.bladeBase == Vector3.one && legacy.baseBones[0].bone == 2,
                "Existing calibration and bone data changed.");
            var blade = trails.staticBlades.Single(entry => entry != null && entry.mesh == geometry.blade);
            RequirePresentation(blade.bladeBase == geometry.bladeBase && blade.bladeTip == geometry.bladeTip &&
                blade.baseBones.Length == 0 && blade.tipBones.Length == 0, "Native static blade calibration mismatch.");
            string serialized = EditorJsonUtility.ToJson(trails);
            ConfigureBladePresentation(trails);
            RequirePresentation(EditorJsonUtility.ToJson(trails) == serialized, "Configuration is not idempotent.");
            EditorJsonUtility.FromJsonOverwrite(serialized, fixture.copy);
            File.WriteAllText(BladeOutput + "/Presentation/Serialization.before.json", serialized);
            File.WriteAllText(BladeOutput + "/Presentation/Serialization.after.json",
                EditorJsonUtility.ToJson(fixture.copy));
            var copiedMeshes = fixture.copy.excludedMeshes;
            for (int index = 0; index < copiedMeshes.Length; index++)
                report.AppendLine("Round-trip mesh " + index + ": unityEqual=" +
                    (copiedMeshes[index] == trails.excludedMeshes[index]) + "; managedEqual=" +
                    Equals(copiedMeshes[index], trails.excludedMeshes[index]));
            RequirePresentation(copiedMeshes.Length == trails.excludedMeshes.Length &&
                Enumerable.Range(0, copiedMeshes.Length).All(index =>
                    copiedMeshes[index] == trails.excludedMeshes[index]),
                "Excluded mesh identities failed serialization round trip. Expected=" +
                DescribePresentationMeshes(trails.excludedMeshes) + "; actual=" +
                DescribePresentationMeshes(fixture.copy.excludedMeshes));
            var copy = fixture.copy.staticBlades.Single(entry => entry != null && entry.mesh == geometry.blade);
            RequirePresentation(copy.bladeBase == blade.bladeBase && copy.bladeTip == blade.bladeTip,
                "Calibrated endpoints failed serialization round trip.");
            var serializedObject = new SerializedObject(fixture.copy);
            RequirePresentation(serializedObject.FindProperty("excludedMeshes").arraySize == 4 &&
                serializedObject.FindProperty("staticBlades").arraySize == 3,
                "SerializedObject does not expose expected arrays.");
            trails.staticBlades = null;
            trails.excludedMeshes = null;
            ConfigureBladePresentation(trails);
            RequirePresentation(trails.staticBlades.Length == 1 && trails.excludedMeshes.Length == 2,
                "Null configuration did not produce exactly one calibration and two sheath exclusions.");
            trails.excludedMeshes = new[] { geometry.blade };
            var unchanged = trails.staticBlades;
            bool conflictRejected = false;
            try
            {
                ConfigureBladePresentation(trails);
            }
            catch (InvalidOperationException)
            {
                conflictRejected = true;
            }
            RequirePresentation(conflictRejected && trails.staticBlades == unchanged &&
                trails.excludedMeshes.Length == 1 && trails.excludedMeshes[0] == geometry.blade,
                "An existing blade exclusion must fail without partially changing configuration.");
            trails.excludedMeshes = geometry.sheaths;
            report.AppendLine("PASS configuration: old entries, null inputs, idempotence, conflicts.");
            report.AppendLine("PASS EditorJsonUtility persistent identity/endpoints round trip and serialized arrays.");
            report.AppendLine("Transient DontSave mesh identity is tested separately by runtime predicates.");
        }

        static void ValidatePresentationAllocations(BladePresentationFixture fixture,
            BladePresentationGeometry geometry, StringBuilder report)
        {
            fixture.trails.excludedMeshes = geometry.sheaths;
            int accepted = 0;
            for (int iteration = 0; iteration < 1024; iteration++)
                accepted += PresentationEligibilityPass(fixture, geometry);
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int iteration = 0; iteration < 10000; iteration++)
                accepted += PresentationEligibilityPass(fixture, geometry);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - start;
            RequirePresentation(allocated == 0 && accepted == 11024 * 2,
                "Warmed mesh eligibility allocated memory or returned inconsistent results: " + allocated);
            report.AppendLine("PASS 10000 warmed mesh/static/skinned passes; allocatedBytes=" + allocated);
        }

        static int PresentationEligibilityPass(BladePresentationFixture fixture, BladePresentationGeometry geometry)
        {
            int accepted = fixture.trails.IsMeshEligible(geometry.blade) ? 1 : 0;
            accepted += fixture.trails.IsMeshEligible(geometry.sheaths[0]) ? 1 : 0;
            accepted += fixture.trails.IsRendererMeshEligible(fixture.blade) ? 1 : 0;
            accepted += fixture.trails.IsRendererMeshEligible(fixture.sheath) ? 1 : 0;
            accepted += fixture.trails.IsRendererMeshEligible(fixture.skin) ? 1 : 0;
            return accepted;
        }

        static string DescribePresentationMeshes(Mesh[] meshes)
        {
            return meshes == null ? "null-array" : string.Join(", ", meshes.Select(mesh =>
                mesh ? mesh.name + ":" + mesh.GetEntityId() + ":" + AssetDatabase.GetAssetPath(mesh) : "null"));
        }

        static void RequirePresentation(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
