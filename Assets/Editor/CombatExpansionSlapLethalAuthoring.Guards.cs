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
        static void WarmSource(Source source)
        {
            foreach (var binding in AnimationUtility.GetCurveBindings(source.clip))
                _ = AnimationUtility.GetEditorCurve(source.clip, binding)?.keys;
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(source.clip))
                _ = AnimationUtility.GetObjectReferenceCurve(source.clip, binding);
            _ = AnimationUtility.GetAnimationEvents(source.clip);
            _ = AnimationUtility.GetAnimationClipSettings(source.clip);
            _ = source.clip.humanMotion;
            _ = source.clip.length;
            var importer = AssetImporter.GetAtPath(source.path) as ModelImporter;
            if (importer)
            {
                _ = importer.clipAnimations;
                _ = importer.defaultClipAnimations;
                _ = importer.humanDescription;
            }
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(source.path);
            var animator = model ? model.GetComponent<Animator>() : null;
            if (animator && animator.avatar && animator.avatar.isHuman && animator.avatar.isValid)
                _ = animator.humanScale;
        }

        static string StableClipHash(Source source)
        {
            string previous = TextHash(EditorJsonUtility.ToJson(source.clip));
            for (int attempt = 0; attempt < 3; attempt++)
            {
                WarmSource(source);
                string current = TextHash(EditorJsonUtility.ToJson(source.clip));
                if (current == previous)
                    return current;
                previous = current;
            }
            throw new InvalidOperationException("Read-only source serialization did not stabilize: " + source.path);
        }

        static void AssertFileHashes(Source source)
        {
            var failures = new List<string>();
            CompareHash(failures, source, "file", source.byteHash, () => FileHash(source.path));
            CompareHash(failures, source, "metadata", source.metaHash, () => FileHash(source.path + ".meta"));
            if (failures.Count != 0)
                throw new InvalidOperationException("SOURCE GUARD FAILED during warm-up:\n" + string.Join("\n", failures));
        }

        static void CompareHash(List<string> failures, Source source, string component, string expected,
            Func<string> read)
        {
            string actual;
            try
            {
                actual = read();
            }
            catch (Exception exception)
            {
                actual = "UNREADABLE " + exception.Message;
            }
            if (expected != actual)
                failures.Add(source.guid + ":" + source.localId + " " + component +
                    " expected=" + expected + " actual=" + actual + " path=" + source.path);
        }

        static List<string> SourceChanges(IEnumerable<Source> sources)
        {
            var failures = new List<string>();
            foreach (var source in sources)
            {
                CompareHash(failures, source, "file", source.byteHash, () => FileHash(source.path));
                CompareHash(failures, source, "metadata", source.metaHash, () => FileHash(source.path + ".meta"));
                CompareHash(failures, source, "clip", source.clipHash,
                    () => TextHash(EditorJsonUtility.ToJson(source.clip)));
                CompareHash(failures, source, "importer", source.importerHash,
                    () => TextHash(EditorJsonUtility.ToJson(AssetImporter.GetAtPath(source.path))));
            }
            return failures;
        }

        static void AssertSourcesUnchanged(IEnumerable<Source> sources)
        {
            var failures = SourceChanges(sources);
            if (failures.Count != 0)
                throw new InvalidOperationException("SOURCE GUARD FAILED:\n" + string.Join("\n", failures));
        }

        static void FinishGuard(Source[] sources, StringBuilder report, Exception original)
        {
            var failures = SourceChanges(sources);
            string detail = string.Join("\n", failures);
            report.AppendLine(failures.Count == 0 ? "FINAL SOURCE GUARD: unchanged" : "SOURCE GUARD FAILED:\n" + detail);
            try
            {
                File.WriteAllText(Output + "/BakeProvenance.txt", report.ToString());
            }
            catch (Exception writeFailure)
            {
                if (original == null)
                    throw;
                original.Data["ProvenanceWriteFailure"] = writeFailure.ToString();
                Debug.LogError("Could not persist provenance; original authoring exception is preserved: " + writeFailure);
            }
            if (failures.Count == 0)
                return;
            if (original != null)
            {
                original.Data["SourceGuardFailures"] = detail;
                Debug.LogError("Additional source guard failure; original authoring exception is preserved:\n" + detail);
                return;
            }
            throw new InvalidOperationException("SOURCE GUARD FAILED:\n" + detail);
        }

        static string MuscleBindingName(int index)
        {
            string name = HumanTrait.MuscleName[index];
            foreach (string side in new[] { "Left", "Right" })
                foreach (string finger in new[] { "Thumb", "Index", "Middle", "Ring", "Little" })
                {
                    string prefix = side + " " + finger + " ";
                    if (name.StartsWith(prefix, StringComparison.Ordinal))
                        return side + "Hand." + finger + "." + name.Substring(prefix.Length);
                }
            return name;
        }

        static bool IsObservedChannel(string name)
        {
            if (Enumerable.Range(0, HumanTrait.MuscleCount).Any(i => MuscleBindingName(i) == name))
                return true;
            foreach (string group in new[] { "Root", "Motion", "LeftFoot", "RightFoot", "LeftHand", "RightHand" })
                if ((name.StartsWith(group + "T.", StringComparison.Ordinal) &&
                    name.Length == group.Length + 3 && "xyz".Contains(name.Last())) ||
                    (name.StartsWith(group + "Q.", StringComparison.Ordinal) &&
                    name.Length == group.Length + 3 && "xyzw".Contains(name.Last())))
                    return true;
            return false;
        }
    }
}
