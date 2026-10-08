using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordRetreatBake
    {
        public static void DiagnoseJoins()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode())
                throw new InvalidOperationException("Join diagnosis requires idle Edit Mode without animation preview.");
            const string path = "GeneratedAssets/CombatExpansion/GreatSwordStudy/RetreatJoinDiagnostics.txt";
            var report = new StringBuilder("Native Ambush retreat join diagnostics; no output clip is written.\n");
            try
            {
                var segments = LoadSegments(report);
                Prepare(segments, report);
                for (int join = 1; join < segments.Length; join++)
                {
                    var previous = segments[join - 1];
                    var next = segments[join];
                    report.AppendLine(FormattableString.Invariant($"JOIN index={join}; seconds={next.start:R}"));
                    foreach (string key in previous.curves.Keys.OrderBy(k => k))
                    {
                        var binding = previous.bindings[key];
                        if (binding.propertyName.StartsWith("m_LocalRotation."))
                        {
                            if (binding.propertyName == "m_LocalRotation.w")
                            {
                                float angle = QuaternionJoinAngle(previous, next, binding.path);
                                report.AppendLine(FormattableString.Invariant(
                                    $"ROTATION_JOIN path={binding.path}; angle={angle:R}; limit={RotationTolerance:R}; ") +
                                    $"fails={angle > RotationTolerance}");
                            }
                            continue;
                        }
                        var left = SegmentKeys(previous, key).Last();
                        var right = SegmentKeys(next, key).First();
                        float delta = Mathf.Abs(left.value - right.value);
                        if (delta <= ValueTolerance)
                            continue;
                        report.AppendLine(FormattableString.Invariant(
                            $"CURVE_JOIN_FAIL binding={key}; previous={left.value:R}; next={right.value:R}; ") +
                            FormattableString.Invariant($"residual={delta:R}; limit={ValueTolerance:R}"));
                    }
                    foreach (string fighter in Fighters)
                        DiagnoseNativeJoin(previous, next, fighter, report);
                }
                report.AppendLine("Diagnostics complete. Bound-transform discontinuities remain bake errors.");
            }
            catch (Exception exception)
            {
                report.AppendLine("FAIL: " + exception);
                throw;
            }
            finally
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, report.ToString());
            }
        }

        static void DiagnoseNativeJoin(Segment previous, Segment next, string fighter, StringBuilder report)
        {
            var original = NativeDriver(fighter);
            var hierarchy = DriverBones(original);
            foreach (string key in new[] { "Dummy004", "Dummy003", MotionRoot, WeaponSocket })
            {
                if (!hierarchy.TryGetValue(key, out var transform))
                {
                    report.AppendLine($"DRIVER_PATH fighter={fighter}; path={key}; present=false");
                    continue;
                }
                string children = string.Join(",", transform.GetComponentsInChildren<Transform>(true)
                    .Select(t => AnimationUtility.CalculateTransformPath(t, original.transform)));
                report.AppendLine($"DRIVER_PATH fighter={fighter}; path={key}; present=true; descendants={children}");
            }
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                using var left = new NativeSample(original, previous.clip, scene);
                using var right = new NativeSample(original, next.clip, scene);
                foreach (float endpointInset in new[] { 0f, .00001f })
                {
                    left.Evaluate(previous.clip.length - endpointInset);
                    right.Evaluate(0);
                    left.bones[MotionRoot].localPosition += previous.offset;
                    right.bones[MotionRoot].localPosition += next.offset;
                    ApplyHelperOffsets(previous, left.bones);
                    ApplyHelperOffsets(next, right.bones);
                    foreach (string key in left.bones.Keys.OrderBy(k => k))
                    {
                        var a = left.bones[key];
                        var b = right.bones[key];
                        float position = Vector3.Distance(a.position, b.position);
                        float angle = Quaternion.Angle(a.rotation, b.rotation);
                        float scale = Vector3.Distance(a.localScale, b.localScale);
                        bool fails = position > .0005f || angle > .08f || scale > .0002f;
                        if (!fails && key != "Dummy004" && key != MotionRoot && key != WeaponSocket)
                            continue;
                        report.AppendLine(FormattableString.Invariant(
                            $"NATIVE_JOIN fighter={fighter}; path={key}; inset={endpointInset:R}; fails={fails}; ") +
                            FormattableString.Invariant(
                            $"worldPositionResidual={position:R}; angle={angle:R}; scale={scale:R}; ") +
                            $"previousLocal={a.localPosition.ToString("F9")}; nextLocal={b.localPosition.ToString("F9")}; " +
                            $"previousWorld={a.position.ToString("F9")}; nextWorld={b.position.ToString("F9")}");
                    }
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        static void ValidateNative(Segment[] segments, AnimationClip output, StringBuilder report)
        {
            foreach (string fighter in Fighters)
            {
                string path = "Assets/DemoSence/GreatSwordExecution/Drivers/" + fighter +
                    "_GreatSword_Attack.prefab";
                var driver = AssetDatabase.LoadAssetAtPath<FrankTestDriver>(path);
                if (!driver || !driver.pose || !driver.pose.driver)
                    throw new InvalidOperationException("Missing native validation driver: " + path);
                var scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    using var baked = new NativeSample(driver.pose.driver, output, scene);
                    float maxPosition = 0;
                    float maxAngle = 0;
                    float maxScale = 0;
                    int samples = 0;
                    foreach (var segment in segments)
                    {
                        using var source = new NativeSample(driver.pose.driver, segment.clip, scene);
                        if (!source.bones.Keys.OrderBy(k => k).SequenceEqual(baked.bones.Keys.OrderBy(k => k)))
                            throw new InvalidOperationException("Native validation skeleton paths differ: " + path);
                        foreach (string bindingPath in segment.bindings.Values.Select(b => b.path).Distinct())
                            if (!source.bones.ContainsKey(bindingPath))
                                throw new InvalidOperationException("Animated path missing from driver: " + bindingPath);
                        foreach (float time in NativeTimes(segment))
                        {
                            source.Evaluate(time);
                            source.bones[MotionRoot].localPosition += segment.offset;
                            ApplyHelperOffsets(segment, source.bones);
                            baked.Evaluate(segment.start + time);
                            foreach (var pair in source.bones)
                            {
                                var expected = pair.Value;
                                var actual = baked.bones[pair.Key];
                                float distance = Vector3.Distance(expected.position, actual.position);
                                // Quaternion.Angle can report rounding noise for identical normalized rotations.
                                float angle = Quaternion.Angle(expected.rotation, actual.rotation);
                                float scale = Vector3.Distance(expected.localScale, actual.localScale);
                                if (!float.IsFinite(distance) || !float.IsFinite(angle) || !float.IsFinite(scale))
                                    throw new InvalidOperationException("Nonfinite native pose: " + pair.Key);
                                maxPosition = Mathf.Max(maxPosition, distance);
                                maxAngle = Mathf.Max(maxAngle, angle);
                                maxScale = Mathf.Max(maxScale, scale);
                                if (distance > .0005f || angle > .08f || scale > .0002f)
                                {
                                    ReportNativeMismatch(segment, output, fighter, pair.Key, time, source, baked, report);
                                    throw new InvalidOperationException(FormattableString.Invariant(
                                        $"Native equivalence failed on {fighter}/{pair.Key}, segment={segment.start:R}, ") +
                                        FormattableString.Invariant(
                                        $"time={time:R}: position={distance:R}m, angle={angle:R}deg, scale={scale:R}"));
                                }
                            }
                            samples++;
                        }
                    }
                    report.Append(FormattableString.Invariant(
                        $"NATIVE_EQUIVALENCE driver={path}; samples={samples}; bones={baked.bones.Count}; "));
                    report.AppendLine(FormattableString.Invariant(
                        $"maxPosition={maxPosition:R}m; maxAngle={maxAngle:R}deg; maxScale={maxScale:R}"));
                }
                finally
                {
                    EditorSceneManager.ClosePreviewScene(scene);
                }
            }
            report.AppendLine("Native verification: AnimationClipPlayable on isolated copies of both native drivers;");
            report.AppendLine("240Hz plus every native key, exact endpoints and length-minus-0.00001 samples.");
            report.AppendLine("Sources are private nonlooping clones to inspect terminal keys without source wrap.");
            report.AppendLine("Expected pose applies the reported root-local translation offset.");
            report.AppendLine("Unused export helpers additionally receive their declared constant local position offsets.");
            report.AppendLine("Proven unused rotation helpers also receive their declared constant quaternion offsets.");
            report.AppendLine("Every driver transform is compared, including the complete weapon IK chain,");
            report.AppendLine("the nested hand Dummy003 and Footsteps; original clips remain comparison references.");
            report.AppendLine("Limits: 0.0005m world position, 0.08deg world rotation, 0.0002 local scale.");
        }

        sealed class NativeSample : IDisposable
        {
            public readonly Dictionary<string, Transform> bones = new Dictionary<string, Transform>();
            GameObject root;
            AnimationClip clip;
            PlayableGraph graph;
            AnimationClipPlayable playable;
            Transform[] transforms;
            Vector3[] positions;
            Quaternion[] rotations;
            Vector3[] scales;

            public NativeSample(Animator original, AnimationClip source, Scene scene)
            {
                try
                {
                    root = Object.Instantiate(original.gameObject);
                    SceneManager.MoveGameObjectToScene(root, scene);
                    root.hideFlags = HideFlags.HideAndDontSave;
                    foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
                        Object.DestroyImmediate(component);
                    foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                        renderer.enabled = false;
                    var animator = root.GetComponent<Animator>();
                    foreach (var other in root.GetComponentsInChildren<Animator>(true).Where(a => a != animator))
                        Object.DestroyImmediate(other);
                    root.SetActive(true);
                    root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    root.transform.localScale = original.transform.lossyScale;
                    animator.runtimeAnimatorController = null;
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.enabled = true;
                    animator.Rebind();
                    foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                        bones.Add(AnimationUtility.CalculateTransformPath(transform, root.transform), transform);
                    if (!bones.ContainsKey(MotionRoot))
                        throw new InvalidOperationException("Native driver has no root motion bone.");
                    transforms = bones.Values.ToArray();
                    positions = transforms.Select(t => t.localPosition).ToArray();
                    rotations = transforms.Select(t => t.localRotation).ToArray();
                    scales = transforms.Select(t => t.localScale).ToArray();
                    clip = Object.Instantiate(source);
                    clip.hideFlags = HideFlags.HideAndDontSave;
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = false;
                    settings.loopBlend = false;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    graph = PlayableGraph.Create("Ambush retreat native bake verification");
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    playable = AnimationClipPlayable.Create(graph, clip);
                    playable.SetApplyFootIK(false);
                    playable.SetApplyPlayableIK(false);
                    playable.SetSpeed(0);
                    var output = AnimationPlayableOutput.Create(graph, "Native generic pose", animator);
                    output.SetSourcePlayable(playable);
                    graph.Play();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Evaluate(float time)
            {
                for (int i = 0; i < transforms.Length; i++)
                {
                    transforms[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
                    transforms[i].localScale = scales[i];
                }
                playable.SetTime(Mathf.Clamp(time, 0, clip.length));
                graph.Evaluate(0);
            }

            public void Dispose()
            {
                if (graph.IsValid())
                    graph.Destroy();
                if (clip)
                    Object.DestroyImmediate(clip);
                if (root)
                    Object.DestroyImmediate(root);
            }
        }
    }
}
