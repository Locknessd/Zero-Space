using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionFrankCanonicalCheck
    {
        sealed class Snapshot
        {
            public readonly Dictionary<string, string> parts = new Dictionary<string, string>();
            public readonly Dictionary<string, AnimationCurve> curves = new Dictionary<string, AnimationCurve>();
            public readonly Dictionary<string, ObjectReferenceKeyframe[]> objects =
                new Dictionary<string, ObjectReferenceKeyframe[]>();
            public AnimationEvent[] events;
            public float length;
        }

        static string Digest(byte[] data)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "");
        }

        static string Fingerprint(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                    write(writer);
                return Digest(stream.ToArray());
            }
        }

        static string Binding(EditorCurveBinding binding)
        {
            return binding.path + "|" + binding.type.AssemblyQualifiedName + "|" + binding.propertyName
                + "|object=" + binding.isPPtrCurve + "|discrete=" + binding.isDiscreteCurve
                + "|serialize=" + binding.isSerializeReferenceCurve;
        }

        static Snapshot Read(AnimationClip clip)
        {
            if (Cache.TryGetValue(Identity(clip), out Snapshot cached))
                return cached;
            var result = new Snapshot { length = clip.length, events = AnimationUtility.GetAnimationEvents(clip) };
            foreach (var binding in AnimationUtility.GetCurveBindings(clip).OrderBy(Binding))
            {
                string key = Binding(binding);
                var curve = AnimationUtility.GetEditorCurve(clip, binding);
                result.curves.Add(key, curve);
                result.parts.Add("float:" + key, Fingerprint(w => WriteCurve(w, curve)));
            }
            foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip).OrderBy(Binding))
            {
                string key = Binding(binding);
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, binding);
                result.objects.Add(key, keys);
                result.parts.Add("object:" + key, Fingerprint(w =>
                {
                    w.Write(keys.Length);
                    foreach (var frame in keys)
                    {
                        w.Write(frame.time);
                        w.Write(Identity(frame.value));
                    }
                }));
            }
            result.parts.Add("events", Fingerprint(w =>
            {
                w.Write(result.events.Length);
                foreach (var item in result.events)
                    WriteEvent(w, item);
            }));
            result.parts.Add("settings", SettingsFingerprint(clip, false));
            result.parts.Add("settingsIgnoringBuilderOverrides", SettingsFingerprint(clip, true));
            result.parts.Add("metadata", Fingerprint(w =>
            {
                w.Write(clip.length);
                w.Write(clip.frameRate);
                w.Write(clip.legacy);
                w.Write((int)clip.wrapMode);
                w.Write(clip.humanMotion);
                w.Write(clip.hasMotionCurves);
                w.Write(clip.hasRootCurves);
            }));
            Cache.Add(Identity(clip), result);
            return result;
        }

        static string SettingsFingerprint(AnimationClip clip, bool normalized)
        {
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            // This is a detached settings value. No SetAnimationClipSettings call is made.
            if (normalized)
            {
                settings.startTime = 0;
                settings.stopTime = clip.length;
                settings.loopTime = false;
                settings.loopBlend = false;
            }
            var fields = settings.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic).OrderBy(f => f.Name).ToArray();
            if (fields.Length == 0)
                throw new InvalidOperationException("Clip settings expose no fields; cannot certify equality");
            return Fingerprint(w =>
            {
                foreach (var field in fields)
                {
                    w.Write(field.Name);
                    object value = field.GetValue(settings);
                    if (value is float number)
                        w.Write(number);
                    else if (value is bool flag)
                        w.Write(flag);
                    else if (value is int integer)
                        w.Write(integer);
                    else if (value == null || value is UnityEngine.Object)
                        w.Write(Identity(value as UnityEngine.Object));
                    else if (value is string text)
                        w.Write(text);
                    else if (value is double precise)
                        w.Write(precise);
                    else if (value.GetType().IsEnum)
                        w.Write(Convert.ToInt64(value));
                    else
                        throw new InvalidOperationException("Unsupported clip setting: " + field.Name);
                }
            });
        }

        static void WriteCurve(BinaryWriter writer, AnimationCurve curve)
        {
            writer.Write((int)curve.preWrapMode);
            writer.Write((int)curve.postWrapMode);
            writer.Write(curve.length);
            for (int i = 0; i < curve.length; i++)
                WriteKey(writer, curve, i, 0);
        }

        static void WriteKey(BinaryWriter writer, AnimationCurve curve, int index, float offset)
        {
            Keyframe key = curve[index];
            writer.Write(key.time - offset);
            writer.Write(key.value);
            writer.Write(key.inTangent);
            writer.Write(key.outTangent);
            writer.Write(key.inWeight);
            writer.Write(key.outWeight);
            writer.Write((int)key.weightedMode);
            writer.Write(AnimationUtility.GetKeyBroken(curve, index));
            writer.Write((int)AnimationUtility.GetKeyLeftTangentMode(curve, index));
            writer.Write((int)AnimationUtility.GetKeyRightTangentMode(curve, index));
        }

        static void WriteEvent(BinaryWriter writer, AnimationEvent item, float offset = 0)
        {
            writer.Write(item.time - offset);
            writer.Write(item.functionName ?? "");
            writer.Write(item.stringParameter ?? "");
            writer.Write(item.floatParameter);
            writer.Write(item.intParameter);
            writer.Write((int)item.messageOptions);
            writer.Write(Identity(item.objectReferenceParameter));
        }

        static string[] Differences(Snapshot a, Snapshot b)
        {
            return a.parts.Keys.Union(b.parts.Keys).OrderBy(k => k)
                .Where(k => !a.parts.ContainsKey(k) || !b.parts.ContainsKey(k) || a.parts[k] != b.parts[k])
                .ToArray();
        }

        static string Describe(Snapshot a, Snapshot b, string[] differences)
        {
            string Signature(Snapshot snapshot) => Fingerprint(w =>
            {
                foreach (var item in snapshot.parts.OrderBy(p => p.Key))
                {
                    w.Write(item.Key);
                    w.Write(item.Value);
                }
            });
            bool curves = !differences.Any(k => k.StartsWith("float:") || k.StartsWith("object:"));
            return "duration=" + a.length.ToString("R") + "/" + b.length.ToString("R")
                + "; floatBindings=" + a.curves.Count + "/" + b.curves.Count
                + "; objectBindings=" + a.objects.Count + "/" + b.objects.Count
                + "; curvesEqual=" + curves
                + "; eventsEqual=" + (a.parts["events"] == b.parts["events"])
                + "; settingsEqual=" + (a.parts["settings"] == b.parts["settings"])
                + "; builderNormalizedSettingsEqual="
                + (a.parts["settingsIgnoringBuilderOverrides"] == b.parts["settingsIgnoringBuilderOverrides"])
                + "; differingParts=" + differences.Length + "; firstDifference=" + differences.FirstOrDefault()
                + "; sha256=" + Signature(a) + "/" + Signature(b);
        }
    }
}
