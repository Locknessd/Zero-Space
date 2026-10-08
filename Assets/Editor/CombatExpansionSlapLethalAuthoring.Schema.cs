using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapLethalAuthoring
    {
        static readonly HashSet<string> Muscles = new(HumanTrait.MuscleName, StringComparer.Ordinal);
        static readonly string[] CriticalBones =
        {
            "Spine", "Chest", "Neck", "Head", "LeftUpperArm", "RightUpperArm", "LeftLowerArm", "RightLowerArm",
            "LeftHand", "RightHand", "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg",
            "LeftFoot", "RightFoot"
        };

        static string BindingId(EditorCurveBinding binding)
        {
            return binding.type.FullName + "|" + binding.path + "|" + binding.propertyName;
        }

        static void WriteInspection(Source[] sources)
        {
            Directory.CreateDirectory(Output);
            foreach (var source in sources)
            {
                string stem = Output + "/" + source.guid + "_" + source.localId;
                var report = new StringBuilder();
                report.AppendLine("Unity=" + Application.unityVersion);
                report.AppendLine("path=" + source.path);
                report.AppendLine("identity=" + source.guid + ":" + source.localId);
                report.AppendLine("name=" + source.clip.name);
                report.AppendLine(FormattableString.Invariant(
                    $"length={source.clip.length:R}; fps={source.clip.frameRate:R}; humanMotion={source.clip.humanMotion}"));
                report.AppendLine("byteSHA256=" + source.byteHash);
                report.AppendLine("metadataSHA256=" + source.metaHash);
                report.AppendLine("clipEditorSHA256=" + source.clipHash);
                report.AppendLine("importerEditorSHA256=" + source.importerHash);
                report.AppendLine("events=" + AnimationUtility.GetAnimationEvents(source.clip).Length);
                report.AppendLine("settings=" + JsonUtility.ToJson(
                    AnimationUtility.GetAnimationClipSettings(source.clip), true));
                foreach (var binding in AnimationUtility.GetCurveBindings(source.clip).OrderBy(BindingId))
                {
                    var curve = AnimationUtility.GetEditorCurve(source.clip, binding);
                    report.AppendLine(FormattableString.Invariant(
                        $"FLOAT {BindingId(binding)} discrete={binding.isDiscreteCurve} keys={curve?.length ?? 0} pre={curve?.preWrapMode} post={curve?.postWrapMode}"));
                    if (curve == null)
                        continue;
                    foreach (var key in curve.keys)
                        report.AppendLine(FormattableString.Invariant(
                            $"  {key.time:R}\t{key.value:R}\t{key.inTangent:R}\t{key.outTangent:R}\t{key.inWeight:R}\t{key.outWeight:R}\t{key.weightedMode}"));
                }
                foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source.clip))
                {
                    report.AppendLine("OBJECT " + BindingId(binding));
                    foreach (var key in AnimationUtility.GetObjectReferenceCurve(source.clip, binding))
                    {
                        string identity = key.value ? AssetDatabase.GetAssetPath(key.value) : "null";
                        if (key.value && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(
                            key.value, out string guid, out long id))
                            identity += " " + guid + ":" + id;
                        report.AppendLine(FormattableString.Invariant($"  {key.time:R}\t{identity}"));
                    }
                }
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(source.path);
                var animator = model ? model.GetComponent<Animator>() : null;
                report.AppendLine("modelAnimator=" + (animator ? animator.name : "missing"));
                if (animator && animator.avatar && animator.avatar.isHuman && animator.avatar.isValid)
                    report.AppendLine(FormattableString.Invariant($"nativeHumanScale={animator.humanScale:R}"));
                File.WriteAllText(stem + ".schema.txt", report.ToString());
                File.WriteAllText(stem + ".clip.json", EditorJsonUtility.ToJson(source.clip, true));
                File.WriteAllText(stem + ".importer.json",
                    EditorJsonUtility.ToJson(AssetImporter.GetAtPath(source.path), true));
                File.WriteAllText(stem + ".source.meta.txt", File.ReadAllText(source.path + ".meta"));
            }
        }

        static void ValidateSource(Source source)
        {
            if (!source.clip.humanMotion || source.clip.legacy || !float.IsFinite(source.clip.length))
                throw new InvalidOperationException("Expected finite native Humanoid clip: " + source.path);
            if (EditorUtility.IsDirty(source.clip) || EditorUtility.IsDirty(AssetImporter.GetAtPath(source.path)))
                throw new InvalidOperationException("Unsaved source or importer edits: " + source.path);
            if (AnimationUtility.GetObjectReferenceCurveBindings(source.clip).Length != 0)
                throw new InvalidOperationException("Object reference curves cannot be safely authored: " + source.path);
            var importer = AssetImporter.GetAtPath(source.path) as ModelImporter;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(source.path);
            var animator = model ? model.GetComponent<Animator>() : null;
            if (!importer || importer.animationType != ModelImporterAnimationType.Human || !animator ||
                !animator.avatar || !animator.avatar.isHuman || !animator.avatar.isValid ||
                !float.IsFinite(animator.humanScale) || animator.humanScale <= 0)
                throw new InvalidOperationException("Cannot establish native Humanoid body-scale units: " + source.path);
            foreach (var binding in AnimationUtility.GetCurveBindings(source.clip))
            {
                string name = binding.propertyName;
                if (binding.type != typeof(Animator) || binding.path != "" || binding.isDiscreteCurve)
                    throw new InvalidOperationException("Unsupported binding: " + BindingId(binding));
                if (!Muscles.Contains(name) && !IsObservedChannel(name))
                    throw new InvalidOperationException("Unobserved binding outside inspected Humanoid schema: " + name);
                var curve = AnimationUtility.GetEditorCurve(source.clip, binding);
                if (source.curves.ContainsKey(name) || curve == null || curve.length == 0)
                    throw new InvalidOperationException("Missing/duplicate curve: " + name);
                if (curve.keys.Any(k => !float.IsFinite(k.time) || !float.IsFinite(k.value) ||
                    float.IsNaN(k.inTangent) || float.IsNaN(k.outTangent)))
                    throw new InvalidOperationException("Nonfinite source curve: " + name);
                if (curve.preWrapMode == WrapMode.Loop || curve.preWrapMode == WrapMode.PingPong ||
                    curve.postWrapMode == WrapMode.Loop || curve.postWrapMode == WrapMode.PingPong)
                    throw new InvalidOperationException("Wrapped source curve: " + name);
                source.curves.Add(name, curve);
                source.bindings.Add(name, binding);
            }
            foreach (string suffix in new[] { "T.x", "T.y", "T.z", "Q.x", "Q.y", "Q.z", "Q.w" })
                RequireCurve(source, "Root" + suffix);
            foreach (string boneName in CriticalBones)
            {
                var bone = (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), boneName);
                for (int axis = 0; axis < 3; axis++)
                {
                    int muscle = HumanTrait.MuscleFromBone((int)bone, axis);
                    if (muscle >= 0)
                        RequireCurve(source, MuscleBindingName(muscle));
                }
            }
        }

        static bool IsRootChannel(string name)
        {
            return name.Length == 7 && ((name.StartsWith("RootT.", StringComparison.Ordinal) &&
                "xyz".Contains(name[6])) || (name.StartsWith("RootQ.", StringComparison.Ordinal) &&
                "xyzw".Contains(name[6])));
        }

        static void RequireCurve(Source source, string name)
        {
            if (!source.curves.ContainsKey(name))
                throw new InvalidOperationException("Missing critical Humanoid channel " + name + " in " + source.path);
        }

        static void WriteIntent(StringBuilder report)
        {
            report.AppendLine("PROVISIONAL receiver-only authored candidate; no contact, floor or gameplay approval.");
            report.AppendLine("Unity=" + Application.unityVersion + "; UTC=" + DateTime.UtcNow.ToString("O"));
            report.AppendLine("Preserve seq1 through 1.52s; seq2 through 1.053333333s. Blend continues original.");
            report.AppendLine("Blend .22s with smoothstep muscle/position interpolation and Quaternion.Slerp.");
            report.AppendLine("Fall map: .45..1.3 -> .70s; 1.3..2 -> .16s; 2..2.55 -> .55s; " +
                "2.55..source endpoint 3.566667 -> .40s. Total 1.81s, then hold through original duration.");
            report.AppendLine("All sources are empirically sampled on the native receiver rig, including Motion and goals.");
            report.AppendLine("Unity 6000.5.9f1 AnimationModule XML, HumanPoseHandler.GetHumanPose: world-space COM, " +
                "position divided by avatar humanScale; exported world metres restore that measured scale.");
            report.AppendLine("Horizontal measured body displacement aligns the fall; root and body shift together.");
            report.AppendLine("No vertical offset, yaw redirection, muscle clamp, shake or carried residual is applied.");
            report.AppendLine("Canonical Root/Motion encoding is selected by independent bone/root playback equivalence. " +
                "Goal curves are omitted only after their resulting native poses are proven equivalent at 240Hz.");
            report.AppendLine("120Hz samples plus exact contact, blend, mapping boundaries and source endpoints; " +
                "linear unweighted tangents plus adaptive native samples where measured interpolation error requires them. " +
                "No timing changes. Events empty; original duration; nonlooping.");
            report.AppendLine("Retain the full 240Hz validation grid and add a .37-tick offset grid for interpolation audit.");
            report.AppendLine("240Hz native-pose limits: every bone/root position 0.002m; rotation 0.5 degrees.");
            report.AppendLine("Reject rather than hide a resampling discrepancy.");
        }
    }
}
