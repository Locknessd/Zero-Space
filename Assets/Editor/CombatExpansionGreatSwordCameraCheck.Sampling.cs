using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordCameraCheck
    {
        static int Sample(CharacterCombat source, CharacterCombat target, FrankBattlePairPlayback pair,
            Camera camera, FrankCinematicCamera framing, int direction, StringBuilder report)
        {
            string label = source.name + "/" + pair.Move.moveName + "/" + direction;
            var sourceBodies = Bodies(source);
            var targetBodies = Bodies(target);
            var weaponSkins = pair.AttackerActor.Pose.weaponRenderers.OfType<SkinnedMeshRenderer>()
                .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.sharedMesh && r.bones.Length > 0)
                .Select(r => new BodyGeometry(r));
            var bodies = sourceBodies.Concat(targetBodies).Concat(weaponSkins).ToArray();
            foreach (var renderer in pair.AttackerActor.Pose.weaponRenderers)
            {
                string binding = renderer is SkinnedMeshRenderer skin ? $"bones={skin.bones.Length}" : "static mesh";
                report.AppendLine($"WEAPON {label}: {renderer.name}; {renderer.GetType().Name}; {binding}");
            }
            if (sourceBodies.Length == 0 || targetBodies.Length == 0)
                throw new InvalidOperationException("Both rendered bodies are required: " + label);
            var times = new SortedSet<float> { .12f, pair.Duration, 1.7273f };
            for (int frame = 8; frame / 60f < pair.Duration; frame++)
                times.Add(frame / 60f);
            for (int frame = 1; frame < 12; frame++)
                times.Add(pair.Duration * frame / 11);
            int failures = 0;
            Vector3 minimum = Vector3.one * float.PositiveInfinity;
            Vector3 maximum = Vector3.one * float.NegativeInfinity;
            float worstBake = 0;
            float worstBounds = 0;
            float maximumDolly = 0;
            float maximumDistance = 0;
            try
            {
                pair.EvaluateAt(0);
                if (!framing.Apply(0, true))
                    throw new InvalidOperationException("Camera declined source pair: " + label);
                foreach (float seconds in times)
                {
                    if (seconds < .12f || seconds > pair.Duration)
                        continue;
                    pair.EvaluateAt(seconds);
                    if (!framing.Apply(0, true) || framing.ActiveShot == null)
                        throw new InvalidOperationException("Missing execution camera take: " + label);
                    Vector3 low = Vector3.one * float.PositiveInfinity;
                    Vector3 high = Vector3.one * float.NegativeInfinity;
                    float bakeError = 0;
                    float boundsMiss = 0;
                    var points = new List<Vector3>();
                    var tightPoints = new List<Vector3>();
                    foreach (var body in bodies)
                    {
                        body.Sample();
                        bakeError = Mathf.Max(bakeError, body.BakeError);
                        boundsMiss = Mathf.Max(boundsMiss, body.BoundsMiss);
                        foreach (var vertex in body.world)
                        {
                            var viewport = camera.WorldToViewportPoint(vertex);
                            if (!Finite(viewport))
                                throw new InvalidOperationException("Nonfinite projected vertex: " + label);
                            low = Vector3.Min(low, viewport);
                            high = Vector3.Max(high, viewport);
                        }
                        // Test the same envelope policy as runtime, using independently reconstructed geometry.
                        points.AddRange(body.framingEnvelope);
                        AppendCorners(tightPoints, body.WorldBounds);
                    }
                    AppendStaticWeaponBounds(points, pair.AttackerActor.Pose.weaponRenderers);
                    AppendStaticWeaponBounds(tightPoints, pair.AttackerActor.Pose.weaponRenderers);
                    float actualDepth;
                    float permittedDepth = PermittedDepth(camera, framing, points, out actualDepth);
                    float tightDepth;
                    float tightPermitted = PermittedDepth(camera, framing, tightPoints, out tightDepth);
                    bool contained = low.x >= Margin && low.y >= Margin && high.x <= 1 - Margin &&
                        high.y <= 1 - Margin && low.z >= camera.nearClipPlane;
                    bool scaleCorrect = bakeError <= .005f;
                    bool reasonableDistance = actualDepth <= permittedDepth;
                    minimum = Vector3.Min(minimum, low);
                    maximum = Vector3.Max(maximum, high);
                    worstBake = Mathf.Max(worstBake, bakeError);
                    worstBounds = Mathf.Max(worstBounds, boundsMiss);
                    maximumDolly = Mathf.Max(maximumDolly, framing.SafetyDolly);
                    maximumDistance = Mathf.Max(maximumDistance, actualDepth);
                    bool critical = Mathf.Abs(seconds - pair.Duration * 5 / 11) < .00001f ||
                        Mathf.Abs(seconds - 1.7273f) < .000001f ||
                        source.name == "Pepe" && pair.Move.sourcePair.cameraKey == "execution/2" &&
                        (Mathf.Abs(seconds - 2f) < .000001f || Mathf.Abs(seconds - 121 / 60f) < .000001f);
                    if (!contained || !scaleCorrect || !reasonableDistance)
                    {
                        failures++;
                        report.AppendLine(FormattableString.Invariant(
                            $"FAIL {label} t={seconds:R}: viewport={low:R}..{high:R}; ") +
                            FormattableString.Invariant($"contained={contained}; bakeError={bakeError:R}m; ") +
                            FormattableString.Invariant($"depth={actualDepth:R}m; allowed={permittedDepth:R}m"));
                    }
                    if (critical || !contained || !scaleCorrect || !reasonableDistance)
                    {
                        report.AppendLine(FormattableString.Invariant(
                            $"DIAGNOSTIC {label} t={seconds:R}: camera={camera.transform.position:R}; ") +
                            FormattableString.Invariant($"viewport={low:R}..{high:R}; rawBoundsMiss={boundsMiss:R}m"));
                        report.AppendLine(FormattableString.Invariant(
                            $"  rendererLocalEnvelope: depth={actualDepth:R}m; allowed={permittedDepth:R}m; ") +
                            FormattableString.Invariant($"tightWorldEnvelope: depth={tightDepth:R}m; ") +
                            FormattableString.Invariant($"allowed={tightPermitted:R}m; ") +
                            FormattableString.Invariant($"safetyDolly={framing.SafetyDolly:R}m"));
                        foreach (var body in bodies)
                            report.AppendLine(FormattableString.Invariant(
                                $"  {body.Name}: currentBoneBounds={body.WorldBounds}; ") +
                                FormattableString.Invariant($"rawBakeBounds={body.RawBakeBounds}; ") +
                                FormattableString.Invariant($"bakeError={body.BakeError:R}m; ") +
                                FormattableString.Invariant($"rawBoundsMiss={body.BoundsMiss:R}m"));
                        AppendBoneExtrema(report, source, camera);
                        AppendBoneExtrema(report, target, camera);
                    }
                }
                report.AppendLine(FormattableString.Invariant(
                    $"CASE {label}: samples={times.Count}; viewport={minimum:R}..{maximum:R}; ") +
                    FormattableString.Invariant($"maxBakeError={worstBake:R}m; rawBoundsMiss={worstBounds:R}m; ") +
                    FormattableString.Invariant($"maxDolly={maximumDolly:R}m; maxDepth={maximumDistance:R}m; ") +
                    $"failures={failures}");
                return failures;
            }
            finally
            {
                foreach (var body in bodies)
                    body.Dispose();
            }
        }

        static void AppendStaticWeaponBounds(List<Vector3> points, Renderer[] renderers)
        {
            foreach (var renderer in renderers)
            {
                if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy ||
                    !(renderer is MeshRenderer))
                    continue;
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh)
                    continue;
                var local = new List<Vector3>();
                AppendCorners(local, filter.sharedMesh.bounds);
                foreach (var point in local)
                    points.Add(renderer.localToWorldMatrix.MultiplyPoint3x4(point));
            }
        }

        static void AppendCorners(List<Vector3> points, Bounds bounds)
        {
            for (int i = 0; i < 8; i++)
                points.Add(bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
        }

        static float PermittedDepth(Camera camera, FrankCinematicCamera framing, List<Vector3> points,
            out float actualDepth)
        {
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (var point in points)
                bounds.Encapsulate(point);
            Vector3 center = camera.transform.InverseTransformPoint(bounds.center);
            actualDepth = center.z;
            float vertical = Mathf.Tan(camera.fieldOfView * Mathf.Deg2Rad * .5f) *
                (1 - Mathf.Clamp(framing.viewportMargin, .05f, .15f) * 2);
            float horizontal = vertical * camera.aspect;
            float requiredDepth = 0;
            foreach (var point in points)
            {
                Vector3 local = camera.transform.InverseTransformPoint(point);
                float offset = local.z - center.z;
                requiredDepth = Mathf.Max(requiredDepth, Mathf.Abs(local.x) / horizontal - offset,
                    Mathf.Abs(local.y) / vertical - offset, .08f - offset);
            }
            // Allow the authored battle radius or the exact fit required by the renderer-local envelope,
            // whichever is larger, plus 25 percent for the authored focus offset and floor correction.
            // This fails a giant camera retreat even if every vertex remains inside the viewport.
            float authored = framing.battleMaximumAuthoredDistance * framing.battleDistanceScale *
                Mathf.Clamp(framing.Zoom, .65f, 3f) * Mathf.Max(1, 1.3f / camera.aspect);
            return Mathf.Max(authored, requiredDepth) * 1.25f;
        }

        static void AppendBoneExtrema(StringBuilder report, CharacterCombat actor, Camera camera)
        {
            Vector3 low = Vector3.one * float.PositiveInfinity;
            Vector3 high = Vector3.one * float.NegativeInfinity;
            foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone)
                    continue;
                var transform = actor.Animator.GetBoneTransform(bone);
                if (!transform)
                    continue;
                var viewport = camera.WorldToViewportPoint(transform.position);
                low = Vector3.Min(low, viewport);
                high = Vector3.Max(high, viewport);
            }
            report.AppendLine(FormattableString.Invariant($"  {actor.name} currentBonesViewport={low:R}..{high:R}"));
        }
    }
}
