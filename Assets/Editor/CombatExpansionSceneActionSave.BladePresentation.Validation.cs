using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSceneActionSave
    {
        // Pure YAML checks plus a read-only check of the actual saved scene. Invoked explicitly by tooling.
        public static void ValidateBladePresentationMerge()
        {
            var results = new List<string>();
            const string oldBlades = "  staticBlades: []\n";
            const string oldExcluded = "  excludedMeshes: []\n";
            const string reference = "{fileID: 4300000, guid: 0123456789abcdef0123456789abcdef, type: 2}";
            const string newBlades = "  staticBlades:\n  - mesh: " + reference +
                "\n    bladeBase: {x: 0, y: -0.1, z: 1e-3}\n    bladeTip: {x: 1, y: 2, z: 3}\n" +
                "    baseBones: []\n    tipBones:\n    - bone: 2\n" +
                "      point: {x: 0, y: 1, z: 0}\n      weight: 1\n";
            const string newExcluded = "  excludedMeshes:\n  - " + reference + "\n";
            string saved = BladeFixture(oldBlades + oldExcluded);
            string expected = BladeFixture(newBlades + newExcluded);
            string source = expected.Replace("  maxTrails: 16", "  maxTrails: 31")
                .Replace("  m_Name: Other", "  m_Name: Live unsaved object");
            CheckBlade(MergeBladePresentation(saved, source, 123) == expected, "Exact outside-field preservation");
            results.Add("PASS exact outside-field preservation with unrelated component and object edits");
            CheckBlade(MergeBladePresentation(BladeFixture(oldBlades), source, 123) == expected,
                "Missing excludedMeshes insertion");
            CheckBlade(MergeBladePresentation(expected, source, 123) == expected, "Idempotence");
            results.Add("PASS adjacent excludedMeshes insertion and idempotence");

            foreach (string newline in new[] { "\n", "\r\n" })
            foreach (bool bom in new[] { false, true })
            {
                byte[] original = Encode(saved.Replace("\n", newline), bom);
                string decoded = Decode(original, out bool actualBom);
                CheckBlade(actualBom == bom && Encode(decoded, actualBom).SequenceEqual(original),
                    "Encoding round trip");
                byte[] merged = Encode(MergeBladePresentation(decoded, source, 123), actualBom);
                CheckBlade(merged.SequenceEqual(Encode(expected.Replace("\n", newline), bom)),
                    "Merge preserves BOM and newline");
            }
            results.Add("PASS LF/CRLF and UTF8 BOM/no-BOM byte preservation");
            RejectBlade(() => MergeBladePresentation(saved, source, 999), "Missing saved component");
            RejectBlade(() => MergeBladePresentation(saved, source.Replace("&123", "&124"), 123),
                "Missing source component");
            RejectBlade(() => MergeBladePresentation(saved.Replace(oldBlades, ""), source, 123),
                "Missing staticBlades");
            RejectBlade(() => MergeBladePresentation(saved, source.Replace(newExcluded, ""), 123),
                "Missing source excludedMeshes");
            foreach (string field in new[] { oldBlades, oldExcluded })
                RejectBlade(() => MergeBladePresentation(saved.Replace(field, field + field), source, 123),
                    "Duplicate owned field");
            RejectBlade(() => MergeBladePresentation(saved, source.Replace(newBlades, newBlades + newBlades), 123),
                "Duplicate source owned field");
            RejectBlade(() => MergeBladePresentation(saved, source.Replace("    tipBones:", "    baseBones:"), 123),
                "Duplicate nested field");
            RejectBlade(() => MergeBladePresentation(saved.Replace(oldBlades, "  staticBlades: {}\n"), source, 123),
                "Unsupported collection");
            RejectBlade(() => MergeBladePresentation(saved, source.Replace("weight: 1", "weight: invalid"), 123),
                "Malformed number");
            RejectBlade(() => MergeBladePresentation(saved, source.Replace("  excludedMeshes:",
                "  excludedMeshes :"), 123), "Malformed field boundary");
            RejectBlade(() => MergeBladePresentation(saved, source.Replace("\n", "\r\n") + "\n", 123),
                "Mixed newlines");
            results.Add("PASS missing component/field, duplicate fields, malformed and unsupported YAML rejection");
            foreach (string name in new[] { "m_PrefabInstance", "m_PrefabAsset", "m_CorrespondingSourceObject" })
            {
                string before = name + ": {fileID: 0}";
                string after = name + ": {fileID: 321}";
                RejectBlade(() => MergeBladePresentation(saved.Replace(before, after), source, 123),
                    "Prefab saved document");
                RejectBlade(() => MergeBladePresentation(saved, source.Replace(before, after), 123),
                    "Prefab source document");
            }
            RejectBlade(() => MergeBladePresentation(saved.Replace("&123\n", "&123 stripped\n"), source, 123),
                "Stripped component");
            RejectBlade(() => MergeBladePresentation(saved.Replace(BladeClass, BladeClass + "Other"), source, 123),
                "Wrong component type");
            results.Add("PASS prefab, stripped and wrong component rejection");

            byte[] actualBytes = File.ReadAllBytes(BattlePath);
            string actual = Decode(actualBytes, out bool actualSceneBom);
            var ids = Documents(actual).Where(pair => pair.Value.Text.Contains(BladeClass + Newline(actual)))
                .Select(pair => pair.Key).ToArray();
            CheckBlade(ids.Length > 0, "Actual saved BattleScene contains inline BattleWeaponTrails");
            foreach (ulong id in ids)
            {
                var fields = ReadBladePresentation(BladeDocument(actual, id), false);
                string withExcluded = fields.ContainsKey("excludedMeshes") ? actual :
                    actual.Insert(fields["staticBlades"].End, oldExcluded.Replace("\n", Newline(actual)));
                CheckBlade(MergeBladePresentation(actual, withExcluded, id) == withExcluded,
                    "Actual saved BattleScene pure merge");
                CheckBlade(MergeBladePresentation(withExcluded, withExcluded, id) == withExcluded,
                    "Actual saved BattleScene idempotence");
            }
            CheckBlade(Encode(actual, actualSceneBom).SequenceEqual(actualBytes), "Actual saved encoding round trip");
            results.Add("PASS actual saved BattleScene pure merge, insertion/idempotence and encoding round trip");
            const string report = "GeneratedAssets/CombatExpansion/SamuraiStudy/BladeContacts/Presentation/" +
                "SelectiveSaveValidation.txt";
            Directory.CreateDirectory(Path.GetDirectoryName(report));
            File.WriteAllLines(report, results, StrictUtf8);
        }

        static string BladeFixture(string fields)
        {
            return "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!114 &123\nMonoBehaviour:\n" +
                "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n" +
                "  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n" +
                "  m_GameObject: {fileID: 456}\n" + BladeScript + "\n" + BladeClass + "\n" +
                "  material: {fileID: 0}\n  styles: []\n" + fields + "  maxTrails: 16\n" +
                "--- !u!1 &456\nGameObject:\n  m_Name: Other\n";
        }

        static void CheckBlade(bool condition, string check)
        {
            if (!condition)
                throw new InvalidDataException("Blade presentation validation failed: " + check);
        }

        static void RejectBlade(Action action, string check)
        {
            try
            {
                action();
            }
            catch (InvalidDataException)
            {
                return;
            }
            throw new InvalidDataException("Blade presentation validation did not reject: " + check);
        }
    }
}
