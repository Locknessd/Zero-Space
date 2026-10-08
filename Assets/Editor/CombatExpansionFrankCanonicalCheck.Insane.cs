using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionFrankCanonicalCheck
    {
        static void CheckInsane()
        {
            const string guid = "2fdca4303a3f85a1088fa56820e55f52";
            var library = AssetDatabase.LoadAssetAtPath<FrankComboLibrary>(AssetDatabase.GUIDToAssetPath(guid));
            if (!library || library.pairs == null)
            {
                Row("Insane/library", library, null, "UNRESOLVED_LIBRARY", "Expected stable library GUID=" + guid);
                return;
            }
            Row("Insane/library", library, library, library.pairs.Length == 17 ? "LIBRARY_SCOPE" : "UNEXPECTED_SCOPE",
                "pairs=" + library.pairs.Length + "; full=" + library.pairs.Count(p => p.step == 0)
                + "; steps=" + library.pairs.Count(p => p.step > 0));
            foreach (var pair in library.pairs)
            {
                string scope = "Insane/" + pair.combo + "/" + pair.step;
                var source = InsaneSources.SingleOrDefault(s => s.group == pair.combo && s.step == pair.step);
                if (source == null)
                {
                    Row(scope, library, pair.attack, "UNRESOLVED_SOURCE", "No explicit source GUID/local ID mapping");
                    continue;
                }
                Compare(scope + "/authored-source-to-copy", Clip(source.guid, source.id), pair.attack,
                    "CopyInsaneClip copies a separate authored Generic FBX; clears legacy, loopTime and loopBlend");
                if (pair.step == 0)
                {
                    var reaction = InsaneSources.Single(s => s.group == pair.combo && s.step == -1);
                    Compare(scope + "/authored-reaction-to-copy", Clip(reaction.guid, reaction.id), pair.reaction);
                    continue;
                }
                var full = library.pairs.SingleOrDefault(p => p.combo == pair.combo && p.step == 0);
                if (full == null)
                {
                    Row(scope, library, pair.attack, "UNRESOLVED_FULL_COMBO", "No unique full combo in library");
                    continue;
                }
                Compare(scope + "/full-versus-step", full.attack, pair.attack,
                    "Actual library references; metadata sourceMatchTime=" + pair.sourceMatchTime.ToString("R"));
                CheckSlice(scope + "/attack-slice", full.attack, pair.attack, pair.sourceMatchTime);
                Compare(scope + "/full-versus-generated-reaction", full.reaction, pair.reaction,
                    "GenerateStepReaction constructs idle entry, recoil blend, translated tail, smoothed root and hold; "
                    + "match=" + pair.sourceMatchTime.ToString("R") + "; impact=" + pair.impactTime.ToString("R")
                    + "; ground=" + pair.groundTime.ToString("R"));
                CheckSlice(scope + "/reaction-slice", full.reaction, pair.reaction, pair.sourceMatchTime);
            }
            CheckDuplicates("Insane/all-attack-pairs", library.pairs.Select(p => p.attack).ToArray(), library);
            CheckDuplicates("Insane/all-reaction-pairs", library.pairs.Select(p => p.reaction).ToArray(), library);
        }

        static void CheckDuplicates(string scope, AnimationClip[] clips, FrankComboLibrary library)
        {
            int compared = 0;
            int duplicates = 0;
            for (int i = 0; i < clips.Length; i++)
            {
                for (int j = i + 1; j < clips.Length; j++)
                {
                    if (!clips[i] || !clips[j])
                        continue;
                    compared++;
                    if (Differences(Read(clips[i]), Read(clips[j])).Length != 0)
                        continue;
                    duplicates++;
                    Row(scope, clips[i], clips[j], "EXPOSED_CONTENT_DUPLICATE", "No registration decision is automatic");
                }
            }
            Row(scope, library, library, "DUPLICATE_SCAN", "compared=" + compared + "; exactDuplicates=" + duplicates);
        }

        static void CheckSlice(string scope, AnimationClip full, AnimationClip step, float offset)
        {
            if (!full || !step || float.IsNaN(offset) || float.IsInfinity(offset) || offset < 0)
            {
                Row(scope, full, step, "UNRESOLVED_SLICE", "Missing reference or invalid recorded match offset");
                return;
            }
            var a = Read(full);
            var b = Read(step);
            bool within = offset + b.length <= a.length;
            bool bindings = a.curves.Count > 0 && a.curves.Keys.OrderBy(k => k)
                .SequenceEqual(b.curves.Keys.OrderBy(k => k));
            bool strict = within && bindings && StrictFloatSlice(a, b, offset) && StrictObjectSlice(a, b, offset);
            bool events = EventSlice(a, b, offset);
            float entry = bindings ? Residual(a, b, offset, 0, .2f) : float.NaN;
            float recovery = bindings ? Residual(a, b, offset, .8f, 1) : float.NaN;
            string result = strict && events ? "STRICT_KEY_SLICE_AT_RECORDED_OFFSET" : "SLICE_NOT_CERTIFIED";
            if (!within || (bindings && (entry > .00001f || recovery > .00001f)))
                result = "NOT_EXACT_SLICE_AT_RECORDED_OFFSET";
            Row(scope, full, step, result,
                "offset=" + offset.ToString("R") + "; fullDuration=" + a.length.ToString("R")
                + "; stepDuration=" + b.length.ToString("R") + "; rangeFits=" + within
                + "; sameFloatBindings=" + bindings + "; strictKeysAndObjects=" + strict + "; slicedEvents=" + events
                + "; entryMaxScalarError=" + entry.ToString("R") + "; recoveryMaxScalarError=" + recovery.ToString("R")
                + "; samples=25 per entry/recovery window; scalar representation, not bone pose; "
                + "settings assessed separately; other offsets and gameplay distinctness are not certified");
        }

        static bool StrictFloatSlice(Snapshot full, Snapshot step, float offset)
        {
            foreach (var item in step.curves)
            {
                var a = full.curves[item.Key];
                var b = item.Value;
                var indices = Enumerable.Range(0, a.length)
                    .Where(i => a[i].time >= offset && a[i].time <= offset + step.length).ToArray();
                if (indices.Length != b.length || b.length == 0 || a[indices[0]].time != offset
                    || a[indices[indices.Length - 1]].time != offset + step.length
                    || a.preWrapMode != b.preWrapMode || a.postWrapMode != b.postWrapMode)
                    return false;
                for (int i = 0; i < b.length; i++)
                {
                    int index = i;
                    if (Fingerprint(w => WriteKey(w, a, indices[index], offset))
                        != Fingerprint(w => WriteKey(w, b, index, 0)))
                        return false;
                }
            }
            return true;
        }

        static bool StrictObjectSlice(Snapshot full, Snapshot step, float offset)
        {
            if (!full.objects.Keys.OrderBy(k => k).SequenceEqual(step.objects.Keys.OrderBy(k => k)))
                return false;
            foreach (var item in step.objects)
            {
                var a = full.objects[item.Key]
                    .Where(k => k.time >= offset && k.time <= offset + step.length).ToArray();
                var b = item.Value;
                // Require an explicit boundary key rather than overlooking pre-window held object values.
                if (a.Length == 0 || a.Length != b.Length || a[0].time != offset || b[0].time != 0)
                    return false;
                for (int i = 0; i < a.Length; i++)
                    if (a[i].time - offset != b[i].time || Identity(a[i].value) != Identity(b[i].value))
                        return false;
            }
            return true;
        }

        static bool EventSlice(Snapshot full, Snapshot step, float offset)
        {
            return Fingerprint(w =>
            {
                foreach (var item in full.events.Where(e => e.time >= offset && e.time <= offset + step.length))
                    WriteEvent(w, item, offset);
            }) == Fingerprint(w =>
            {
                foreach (var item in step.events)
                    WriteEvent(w, item);
            });
        }

        static float Residual(Snapshot full, Snapshot step, float offset, float start, float end)
        {
            float error = 0;
            for (int i = 0; i <= 24; i++)
            {
                float time = step.length * Mathf.Lerp(start, end, i / 24f);
                if (time + offset > full.length)
                    return float.PositiveInfinity;
                foreach (var item in step.curves)
                {
                    float delta = Mathf.Abs(item.Value.Evaluate(time) - full.curves[item.Key].Evaluate(time + offset));
                    if (float.IsNaN(delta))
                        return float.PositiveInfinity;
                    error = Mathf.Max(error, delta);
                }
            }
            return error;
        }
    }
}
