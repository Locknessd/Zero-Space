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
            var legacy = new BattleWeaponTrails.StaticBlade
            {
                mesh = fixture.otherMesh,
                bladeBase = Vector3.one,
                bladeTip = Vector3.up * 3,
                baseBones = new[] { new BattleWeaponTrails.BonePoint { bone = 2, point = Vector3.left, weight = 1 } }
            };
            trails.staticBlades = new[] { legacy, null, new BattleWeaponTrails.StaticBlade { mesh = geometry.blade } };
            trails.excludedMeshes = new[] { fixture.otherMesh, null, geometry.sheaths[0] };
            ConfigureBladePresentation(trails);
            RequirePresentation(trails.staticBlades.Length == 3 && trails.staticBlades[0] == legacy &&
                trails.staticBlades[1] == null, "Unrelated calibrated blades and null entries must remain intact.");
            RequirePresentation(trails.excludedMeshes.Length == 4 && trails.excludedMeshes[0] == fixture.otherMesh &&
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
            RequirePresentation(fixture.copy.excludedMeshes.SequenceEqual(trails.excludedMeshes),
                "Excluded mesh identities failed serialization round trip.");
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
            report.AppendLine("PASS EditorJsonUtility identity/endpoints round trip and serialized array properties.");
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

        static void RequirePresentation(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
