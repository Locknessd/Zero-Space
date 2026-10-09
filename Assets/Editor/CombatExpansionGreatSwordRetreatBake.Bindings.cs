using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordRetreatBake
    {
        static readonly string[] Fighters = { "Mankey", "Pepe" };
        const string WeaponSocket = "root/ik_hand_root/ik_hand_gun/ik_hand_r";
        const string HandDummy = "root/pelvis/Bip001 Pelvis/spine_01/spine_02/spine_03/" +
            "clavicle_r/upperarm_r/lowerarm_r/hand_r/Dummy001/Dummy003";
        const string Footsteps = "root/pelvis/Bip001 Footsteps";
        const float DefaultTolerance = .000001f;

        static Animator NativeDriver(string fighter)
        {
            string path = "Assets/DemoSence/GreatSwordExecution/Drivers/" + fighter +
                "_GreatSword_Attack.prefab";
            var driver = AssetDatabase.LoadAssetAtPath<FrankTestDriver>(path);
            if (!driver || !driver.pose || !driver.pose.driver)
                throw new InvalidOperationException("Missing native validation driver: " + path);
            return driver.pose.driver;
        }

        static Dictionary<string, Transform> DriverBones(Animator animator)
        {
            return animator.GetComponentsInChildren<Transform>(true).ToDictionary(
                t => AnimationUtility.CalculateTransformPath(t, animator.transform), t => t);
        }

        static void ReconcileBindings(Segment[] segments, StringBuilder report)
        {
            var drivers = Fighters.Select(NativeDriver).ToArray();
            var bones = drivers.Select(DriverBones).ToArray();
            var union = segments.SelectMany(s => s.bindings).GroupBy(p => p.Key)
                .ToDictionary(g => g.Key, g => g.First().Value);
            foreach (var pair in union.ToArray())
            {
                var binding = pair.Value;
                int count = bones.Count(b => b.ContainsKey(binding.path));
                if (count == bones.Length)
                    continue;
                // This source helper is at a different hierarchy path from the driver's hand Dummy003.
                // Never map it by leaf name: it does not animate either native playback skeleton.
                if (count != 0 || binding.path != "Dummy003")
                    throw new InvalidOperationException("Unsupported driver binding coverage " + count + "/" +
                        bones.Length + " for " + pair.Key + "; only verified top-level Dummy003 is ignorable.");
                foreach (var segment in segments)
                {
                    if (!segment.curves.TryGetValue(pair.Key, out var curve))
                        continue;
                    report.AppendLine(FormattableString.Invariant(
                        $"UNBOUND_HELPER source={AssetDatabase.GetAssetPath(segment.clip)}; binding={pair.Key}; ") +
                        FormattableString.Invariant(
                        $"keys={curve.length}; first={curve.Evaluate(0):R}; last={curve.Evaluate(segment.clip.length):R}; ") +
                        "absent=Mankey,Pepe; discarded from driver-specific output; no leaf-name remapping");
                    segment.curves.Remove(pair.Key);
                    segment.bindings.Remove(pair.Key);
                }
                union.Remove(pair.Key);
            }
            foreach (var segment in segments)
            {
                var missing = union.Where(p => !segment.curves.ContainsKey(p.Key)).ToArray();
                foreach (var pair in missing)
                {
                    var binding = pair.Value;
                    // The importer may omit any constant transform channel on these known paths.
                    // VerifyDefaults proves each actual value on both drivers before materializing it.
                    bool known = binding.path == Footsteps || binding.path == HandDummy ||
                        binding.path == WeaponSocket;
                    if (!known)
                        throw new InvalidOperationException("Unsupported omitted binding: " + pair.Key +
                            " in " + AssetDatabase.GetAssetPath(segment.clip));
                }
                if (missing.Length == 0)
                    continue;
                var values = VerifyDefaults(segment, missing, drivers, bones, report);
                // Missing channels are exact constants, but native quaternion conversion also uses key spacing.
                // Match the source knot schedule instead of spanning a whole phase with two synthetic keys.
                var constantTimes = segment.curves.Values.SelectMany(c => c.keys.Select(k => k.time))
                    .Where(t => t >= 0 && t <= segment.clip.length).Concat(new[] { 0f, segment.clip.length })
                    .Distinct().OrderBy(t => t).ToArray();
                foreach (var pair in missing)
                {
                    float value = values[pair.Key];
                    var curve = new AnimationCurve(constantTimes.Select(t => new Keyframe(t, value, 0, 0)).ToArray());
                    segment.curves.Add(pair.Key, curve);
                    segment.bindings.Add(pair.Key, pair.Value);
                    segment.materializedCurves.Add(pair.Key);
                    report.AppendLine(FormattableString.Invariant(
                        $"MATERIALIZED_DEFAULT source={AssetDatabase.GetAssetPath(segment.clip)}; ") +
                        FormattableString.Invariant($"binding={pair.Key}; constant={value:R}; keys={constantTimes.Length}; ") +
                        "exact constant on native source knot schedule; zero incoming/outgoing derivatives");
                }
            }
            report.AppendLine("Binding reconciliation is specific to both GreatSword_Attack native drivers.");
            report.AppendLine("All bound source curves retained; missing channels require verified constant defaults.");
        }

        static Dictionary<string, float> VerifyDefaults(Segment segment,
            KeyValuePair<string, EditorCurveBinding>[] missing, Animator[] drivers,
            Dictionary<string, Transform>[] bones, StringBuilder report)
        {
            var result = new Dictionary<string, float>();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                for (int index = 0; index < drivers.Length; index++)
                {
                    using var sample = new NativeSample(drivers[index], segment.clip, scene);
                    sample.Evaluate(0);
                    var baseline = missing.ToDictionary(p => p.Key,
                        p => Property(sample.bones[p.Value.path], p.Value));
                    foreach (var pair in missing)
                    {
                        float value = baseline[pair.Key];
                        float reference = Property(bones[index][pair.Value.path], pair.Value);
                        if (!float.IsFinite(value) || Mathf.Abs(value - reference) > DefaultTolerance)
                            throw new InvalidOperationException(FormattableString.Invariant(
                                $"Native default differs from prefab: {Fighters[index]} {pair.Key}; ") +
                                FormattableString.Invariant($"reference={reference:R}; sampled={value:R}"));
                        if (result.TryGetValue(pair.Key, out float shared) &&
                            Mathf.Abs(value - shared) > DefaultTolerance)
                            throw new InvalidOperationException("Driver-specific defaults disagree: " + pair.Key);
                        if (index == 0)
                            result.Add(pair.Key, value);
                        report.AppendLine(FormattableString.Invariant(
                            $"DEFAULT_REFERENCE fighter={Fighters[index]}; binding={pair.Key}; ") +
                            FormattableString.Invariant($"prefab={reference:R}; native={value:R}"));
                    }
                    int samples = 0;
                    float maximum = 0;
                    foreach (float time in NativeTimes(segment))
                    {
                        sample.Evaluate(time);
                        foreach (var pair in missing)
                        {
                            float actual = Property(sample.bones[pair.Value.path], pair.Value);
                            float delta = Mathf.Abs(actual - baseline[pair.Key]);
                            if (!float.IsFinite(actual) || delta > DefaultTolerance)
                                throw new InvalidOperationException(FormattableString.Invariant(
                                    $"Omitted curve is not constant: {Fighters[index]} {pair.Key}; ") +
                                    FormattableString.Invariant($"time={time:R}; deviation={delta:R}"));
                            maximum = Mathf.Max(maximum, delta);
                        }
                        samples++;
                    }
                    report.AppendLine(FormattableString.Invariant(
                        $"DEFAULT_EQUIVALENCE fighter={Fighters[index]}; samples={samples}; maxError={maximum:R}"));
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
            return result;
        }

        static float Property(Transform transform, EditorCurveBinding binding)
        {
            int component = "xyzw".IndexOf(binding.propertyName.Last());
            if (binding.propertyName.StartsWith("m_LocalPosition."))
                return transform.localPosition[component];
            if (binding.propertyName.StartsWith("m_LocalScale."))
                return transform.localScale[component];
            if (binding.propertyName.StartsWith("m_LocalRotation."))
                return transform.localRotation[component];
            throw new InvalidOperationException("Unsupported transform property: " + Key(binding));
        }

        static IEnumerable<float> NativeTimes(Segment segment)
        {
            int frames = Mathf.CeilToInt(segment.clip.length * 240);
            return Enumerable.Range(0, frames + 1)
                .Select(i => Mathf.Min(i / 240f, segment.clip.length))
                .Concat(new[] { Mathf.Max(0, segment.clip.length - .00001f) })
                .Concat(segment.curves.Values.SelectMany(c => c.keys.Select(k => k.time)))
                .Distinct().OrderBy(t => t);
        }
    }
}
