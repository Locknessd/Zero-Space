using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordRetreatBake
    {
        const string Output = "Assets/CombatExpansion/Animations/GreatSword_Ambush_Retreat.anim";
        const string ReportPath = "GeneratedAssets/CombatExpansion/GreatSwordStudy/RetreatBake.txt";
        const string MotionRoot = "root";
        const float TimeTolerance = .000002f;
        const float ValueTolerance = .0001f;
        const float RotationTolerance = .08f;

        sealed class Segment
        {
            public AnimationClip clip;
            public float start;
            public Vector3 offset;
            public readonly Dictionary<string, AnimationCurve> curves = new Dictionary<string, AnimationCurve>();
            public readonly Dictionary<string, EditorCurveBinding> bindings =
                new Dictionary<string, EditorCurveBinding>();
            public readonly Dictionary<string, float> quaternionSigns = new Dictionary<string, float>();
            public readonly Dictionary<string, Vector3> helperOffsets = new Dictionary<string, Vector3>();
            public readonly Dictionary<string, Quaternion> helperRotations = new Dictionary<string, Quaternion>();
            public readonly HashSet<string> materializedCurves = new HashSet<string>();
        }

        [MenuItem("Tools/Combat Expansion/Bake GreatSword Ambush Retreat")]
        public static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("Bake requires idle Edit Mode with animation preview stopped.");
            var report = new StringBuilder("GreatSword Ambush native generic retreat concatenation.\n");
            AnimationClip output = null;
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            try
            {
                var segments = LoadSegments(report);
                Prepare(segments, report);
                output = Build(segments, report);
                ValidateNative(segments, output, report);
                EnsureFolder("Assets/CombatExpansion/Animations");
                var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(Output);
                if (!existing && AssetDatabase.LoadMainAssetAtPath(Output))
                    throw new InvalidOperationException("Output path contains a non-animation asset: " + Output);
                string previousGuid = AssetDatabase.AssetPathToGUID(Output);
                if (existing)
                {
                    EditorUtility.CopySerialized(output, existing);
                    EditorUtility.SetDirty(existing);
                }
                else
                {
                    AssetDatabase.CreateAsset(output, Output);
                    existing = output;
                }
                AssetDatabase.SaveAssetIfDirty(existing);
                string guid = AssetDatabase.AssetPathToGUID(Output);
                if (!ValidGuid(guid) || (!string.IsNullOrEmpty(previousGuid) && previousGuid != guid))
                    throw new InvalidOperationException("Output GUID was invalid or changed during rebake.");
                report.AppendLine($"OUTPUT path={Output}; guid={guid}; length={existing.length:R}s");
                report.AppendLine("PASS: native comparison completed before output asset creation or replacement.");
                report.AppendLine("Visual grounding, retargeted avatars and gameplay remain separate validations.");
                Debug.Log("GreatSword Ambush retreat baked: " + Output);
            }
            catch (Exception exception)
            {
                report.AppendLine("FAIL: " + exception);
                throw;
            }
            finally
            {
                File.WriteAllText(ReportPath, report.ToString());
                if (output && !EditorUtility.IsPersistent(output))
                    Object.DestroyImmediate(output);
            }
        }

        static Segment[] LoadSegments(StringBuilder report)
        {
            return new[]
            {
                Load("Assets/GreatSword_Animset/Animation/Execution_Sample/GreatSword_Attack_Ambush.FBX",
                    "GreatSword_Attack_Ambush", "610fba6d346a64f4583237c3ef4f7e46", report),
                Load("Assets/GreatSword_Animset/Animation/Root/Movement/GreatSword_Strafe_Walk_B_Start.FBX",
                    "Take 001", "12987da400bbe864aae42fdf303bc647", report),
                Load("Assets/GreatSword_Animset/Animation/Root/Movement/GreatSword_Strafe_Walk_B_End.FBX",
                    "Take 001", "ed2751f54f474b74397a144408a8fe03", report)
            };
        }

        static Segment Load(string path, string name, string expectedGuid, StringBuilder report)
        {
            var importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (!importer || importer.animationType != ModelImporterAnimationType.Generic)
                throw new InvalidOperationException("Expected an imported Generic rig: " + path);
            var matches = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => c.name == name).ToArray();
            if (matches.Length != 1)
                throw new InvalidOperationException("Expected exact unique clip " + name + " at " + path);
            var clip = matches[0];
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long fileId);
            report.Append(FormattableString.Invariant(
                $"SOURCE path={path}; clip={name}; guid={guid}; fileID={fileId}; length={clip.length:R}s; "));
            report.AppendLine(FormattableString.Invariant(
                $"fps={clip.frameRate:R}; rig={importer.animationType}; motionNode={importer.motionNodeName}"));
            if (guid != expectedGuid || fileId != 7400000 || !ValidGuid(guid) || clip.humanMotion ||
                clip.legacy || clip.length <= TimeTolerance || !string.IsNullOrEmpty(importer.motionNodeName))
                throw new InvalidOperationException("Unexpected native clip identity, rig or motion node: " + path);
            if (AnimationUtility.GetObjectReferenceCurveBindings(clip).Length != 0)
                throw new InvalidOperationException("Object-reference animation cannot be safely stitched: " + path);
            var segment = new Segment { clip = clip };
            foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                string key = Key(binding);
                report.AppendLine("BINDING " + path + " | " + key);
                if (binding.type != typeof(Transform) || binding.isDiscreteCurve || binding.isPPtrCurve ||
                    binding.isSerializeReferenceCurve || !SupportedProperty(binding.propertyName))
                    throw new InvalidOperationException("Unsupported imported binding: " + key + " at " + path);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0)
                    throw new InvalidOperationException("Missing decompressed editor curve: " + key + " at " + path);
                segment.bindings.Add(key, binding);
                segment.curves.Add(key, curve);
            }
            foreach (string axis in new[] { "x", "y", "z" })
                if (!segment.curves.ContainsKey(MotionRoot + "|m_LocalPosition." + axis))
                    throw new InvalidOperationException("Missing critical authored root translation " + axis + ": " + path);
            foreach (var group in segment.bindings.Values.GroupBy(b => b.path))
            {
                var rotations = group.Where(b => b.propertyName.StartsWith("m_LocalRotation.")).ToArray();
                if (rotations.Length != 0 && rotations.Length != 4)
                    throw new InvalidOperationException("Incomplete quaternion binding at " + group.Key + ": " + path);
            }
            return segment;
        }

        static bool SupportedProperty(string property)
        {
            return new[] { "m_LocalPosition.", "m_LocalScale.", "m_LocalRotation." }
                .Any(prefix => property.StartsWith(prefix, StringComparison.Ordinal) &&
                    property.Length == prefix.Length + 1 &&
                    (prefix.EndsWith("Rotation.") ? "xyzw" : "xyz").Contains(property[property.Length - 1]));
        }

        static string Key(EditorCurveBinding binding) => binding.path + "|" + binding.propertyName;

        static bool ValidGuid(string guid) => guid != null && guid.Length == 32 && guid.All(Uri.IsHexDigit);

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            string guid = AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
            if (!ValidGuid(guid))
                throw new InvalidOperationException("Invalid folder GUID: " + path);
        }
    }
}
