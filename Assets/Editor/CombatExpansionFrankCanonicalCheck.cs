using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionFrankCanonicalCheck
    {
        const string ReportFolder = "GeneratedAssets/CombatExpansion";
        const long FbxId = 1827226128182048838;
        static readonly List<string> Rows = new List<string>();
        static readonly Dictionary<string, Snapshot> Cache = new Dictionary<string, Snapshot>();
        static readonly Dictionary<string, int> Counts = new Dictionary<string, int>();

        [MenuItem("Tools/Frank Retarget/Validate canonical clip relationships")]
        public static void Validate()
        {
            Rows.Clear();
            Cache.Clear();
            Counts.Clear();
            Rows.Add("scope\tsource_identity\ttarget_identity\tsource_path\ttarget_path\tclassification\tdetail");
            try
            {
                CheckFrank();
                CheckInsane();
            }
            catch (Exception exception)
            {
                Row("validation", null, null, "ERROR", exception.ToString());
            }
            Directory.CreateDirectory(ReportFolder);
            File.WriteAllLines(ReportFolder + "/FrankCanonicalValidation.tsv", Rows);
            var summary = new List<string>
            {
                "Frank canonical read-only validation; UTC " + DateTime.UtcNow.ToString("O"),
                "Unity " + Application.unityVersion,
                "Comparisons inspect stable GUID/local IDs and current imported assets, not clip names.",
                "Equality means exposed curve/key/event/settings content; no pose or gameplay equivalence claim.",
                "Slice checks use the library recorded offset; other offsets are not certified.",
                "No scene, importer, controller, clip, registry or library mutation is performed."
            };
            summary.AddRange(Counts.OrderBy(p => p.Key).Select(p => p.Key + ": " + p.Value));
            File.WriteAllLines(ReportFolder + "/FrankCanonicalValidation.txt", summary);
            Debug.Log(string.Join("\n", summary));
        }

        static string Identity(Object item)
        {
            if (!item)
                return "missing";
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(item, out string guid, out long id))
                throw new InvalidOperationException("Nonpersistent comparison reference: " + item.GetType());
            return guid + ":" + id.ToString(CultureInfo.InvariantCulture);
        }

        static AnimationClip Clip(string guid, long id = 7400000)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path))
                return null;
            return AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .SingleOrDefault(c => Identity(c) == guid + ":" + id.ToString(CultureInfo.InvariantCulture));
        }

        static void Row(string scope, Object source, Object target, string result, string detail)
        {
            string Clean(string value) => (value ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            Rows.Add(string.Join("\t", new[]
            {
                scope, Identity(source), Identity(target), AssetDatabase.GetAssetPath(source),
                AssetDatabase.GetAssetPath(target), result, detail
            }.Select(Clean)));
            Counts[result] = Counts.TryGetValue(result, out int count) ? count + 1 : 1;
        }

        static void Compare(string scope, AnimationClip source, AnimationClip target, string evidence = "")
        {
            if (!source || !target)
            {
                Row(scope, source, target, "UNRESOLVED_MISSING_ID", evidence);
                return;
            }
            Snapshot a = Read(source);
            Snapshot b = Read(target);
            string[] changes = Differences(a, b);
            string result = changes.Length == 0 ? "EXPOSED_CONTENT_EQUAL" : "CONTENT_DIFFERS";
            if (source.humanMotion != target.humanMotion)
                result = "REPRESENTATION_DIFFERS_NO_EQUIVALENCE";
            Row(scope, source, target, result, evidence + "; " + Describe(a, b, changes));
        }

        static void CheckFrank()
        {
            foreach (var item in FrankLinks)
            {
                AnimationClip rig = Clip(item[1], FbxId);
                AnimationClip original = Clip(item[2], FbxId);
                Compare("Frank/" + item[0] + "/rig-to-controller-source", rig, original,
                    "Prepare copies the selected mesh FBX then configures a Humanoid rig; " + FileEvidence(rig, original));
                for (int character = 0; character < 2; character++)
                {
                    AnimationClip extracted = Clip(item[3 + character]);
                    string label = "Frank/" + item[0] + "/" + (character == 0 ? "Mankey" : "Pepe");
                    Compare(label + "/rig-to-saved", rig, extracted);
                    Compare(label + "/controller-source-to-saved", original, extracted,
                        "ReplaceMotions uses existing saved assets without regenerating them");
                    CheckController(item[5], original, label + "/original-controller");
                    CheckController(item[6 + character], extracted, label + "/derived-controller");
                }
            }
            var selected = Clip("0650c1aa78a175348929f66c5076a6fa", FbxId);
            var raw = Clip("6df2ef8fe0d0d2e6cb763cef597ca470", FbxId);
            var defaultSource = Clip("7de125855c1d397418fdcfc954ba3fa3");
            var alternateRaw = Clip("e64b262abd6656593aeeb26d8e275590", FbxId);
            const string originalWarriorController = "c6349909da1f24c499267270030f7bda";
            CheckController(originalWarriorController, selected, "Warrior/original-selected-take-reference");
            CheckController(originalWarriorController, defaultSource, "Warrior/original-default-reference");
            Compare("Warrior/selected-to-generic", selected, raw, FileEvidence(selected, raw));
            Compare("Warrior/selected-to-original-default", selected, defaultSource,
                "Original default state references hit_combo_weapon(7).anim; selected FBX is disconnected Hit2 motion");
            Compare("Warrior/generic-to-original-default", raw, defaultSource);
            Compare("Warrior/generic-take1-to-take2", raw, alternateRaw);
            foreach (var item in WarriorLinks)
            {
                var first = Clip(item[1]);
                var second = Clip(item[2]);
                Compare(item[0] + "/selected-to-saved-default", selected, first);
                Compare(item[0] + "/generic-to-saved-default", raw, first);
                Compare(item[0] + "/original-default-to-saved", defaultSource, first);
                Compare(item[0] + "/corrected-generic-take2-to-saved", alternateRaw, second);
                Compare(item[0] + "/saved-default-to-alternate", first, second);
                CheckController(item[3], first, item[0] + "/default-controller");
                CheckController(item[3], second, item[0] + "/alternate-controller");
            }
        }

        static void CheckController(string guid, AnimationClip expected, string scope)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(AssetDatabase.GUIDToAssetPath(guid));
            if (!controller || !expected)
            {
                Row(scope, controller, expected, "UNRESOLVED_CONTROLLER", "Missing controller or expected clip");
                return;
            }
            var references = controller.layers.SelectMany(l => States(l.stateMachine))
                .Where(s => s.motion == expected).Select(s => Identity(s)).ToArray();
            var serialized = AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GetAssetPath(controller))
                .OfType<AnimatorState>().Where(s => s.motion == expected).Select(Identity).ToArray();
            string result = references.Length > 0 ? "ACTIVE_STATE_REFERENCE"
                : serialized.Length > 0 ? "DISCONNECTED_STATE_REFERENCE" : "REFERENCE_MISSING";
            Row(scope, controller, expected, result,
                "State machine membership IDs=" + string.Join(",", references)
                + "; serialized state IDs=" + string.Join(",", serialized)
                + "; membership alone does not prove a transition or gameplay route");
        }

        static IEnumerable<AnimatorState> States(AnimatorStateMachine machine)
        {
            foreach (var child in machine.states)
                yield return child.state;
            foreach (var child in machine.stateMachines)
                foreach (var state in States(child.stateMachine))
                    yield return state;
        }

        static string FileEvidence(AnimationClip a, AnimationClip b)
        {
            if (!a || !b)
                return "Missing FBX identity";
            string first = AssetDatabase.GetAssetPath(a);
            string second = AssetDatabase.GetAssetPath(b);
            string Hash(string path) => Digest(File.ReadAllBytes(path));
            return "FBX bytes equal=" + (Hash(first) == Hash(second))
                + "; source import=" + ImportDescription(first) + "; target import=" + ImportDescription(second)
                + "; equal FBX bytes establish authored-file provenance, not imported motion equality";
        }

        static string ImportDescription(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            return importer == null ? "not-model" : importer.animationType + "/" + importer.animationCompression;
        }
    }
}
