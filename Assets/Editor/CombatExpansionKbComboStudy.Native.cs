using System;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        // Independent single-clip source evaluation, never retargets or changes the live pair.
        sealed class NativeReference : IDisposable
        {
            readonly FrankTestActor actor;
            readonly CandidateRecord record;
            readonly FrankTestDriver driver;
            readonly Transform[] transforms;
            readonly Vector3[] positions, scales;
            readonly Quaternion[] rotations;
            readonly HumanBodyBones[] bones;
            readonly SourceRecord[] sources;
            PlayableGraph graph;
            AnimationPlayableOutput output;
            AnimationClipPlayable[] clips;

            public NativeReference(FrankTestActor actor, SourceRecord[] sources, CandidateRecord record)
            {
                this.actor = actor;
                this.sources = sources;
                this.record = record;
                try
                {
                    driver = Object.Instantiate(actor.activeDriver, actor.transform, false);
                    driver.name = "KB independent native single clip reference";
                    driver.pose.enabled = false;
                    driver.pose.character = null;
                    driver.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                    transforms = driver.GetComponentsInChildren<Transform>(true);
                    positions = transforms.Select(t => t.localPosition).ToArray();
                    rotations = transforms.Select(t => t.localRotation).ToArray();
                    scales = transforms.Select(t => t.localScale).ToArray();
                    bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone).Select(i => (HumanBodyBones)i)
                        .Where(b => actor.Pose.driver.GetBoneTransform(b) && driver.pose.driver.GetBoneTransform(b))
                        .ToArray();
                    graph = PlayableGraph.Create("KB independent single clip source");
                    graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                    clips = sources.Take(3).Select(s =>
                    {
                        var clip = AnimationClipPlayable.Create(graph, s.clip);
                        clip.SetApplyFootIK(false);
                        clip.SetApplyPlayableIK(false);
                        clip.SetSpeed(0);
                        return clip;
                    }).ToArray();
                    output = AnimationPlayableOutput.Create(graph, "Native single clip", driver.pose.driver);
                    graph.Play();
                    record.carriedHorizontalHips = new Vector3[3];
                    for (int i = 1; i < 3; i++)
                    {
                        Sample(i - 1, .4f);
                        var previous = actor.transform.InverseTransformPoint(driver.pose.sourceHips.position);
                        Sample(i, 0);
                        var next = actor.transform.InverseTransformPoint(driver.pose.sourceHips.position);
                        var carry = previous + record.carriedHorizontalHips[i - 1] - next;
                        carry.y = 0;
                        record.carriedHorizontalHips[i] = carry;
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            void Restore()
            {
                for (int i = 0; i < transforms.Length; i++)
                {
                    transforms[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
                    transforms[i].localScale = scales[i];
                }
            }

            void Sample(int index, float seconds)
            {
                Restore();
                output.SetSourcePlayable(clips[index]);
                clips[index].SetTime(0);
                graph.Evaluate(0);
                Restore();
                clips[index].SetTime(Mathf.Clamp(seconds, 0, sources[index].length - .00001f));
                graph.Evaluate(0);
            }

            public void Compare(float seconds, StringBuilder rows)
            {
                int index = seconds < .4f ? 0 : seconds < .8f ? 1 : 2;
                var step = record.attacks[index];
                if (index > 0 && seconds < step.seconds + step.blendSeconds)
                    return;
                Sample(index, Mathf.Clamp(seconds - step.seconds, 0, step.sourceEndSeconds - .00001f));
                Vector3 carry = actor.transform.TransformVector(record.carriedHorizontalHips[index]);
                foreach (var bone in bones)
                {
                    var actual = actor.Pose.driver.GetBoneTransform(bone);
                    var reference = driver.pose.driver.GetBoneTransform(bone);
                    float angle = Quaternion.Angle(actual.localRotation, reference.localRotation);
                    float distance = Vector3.Distance(actual.position, reference.position + carry);
                    if (!float.IsFinite(angle) || !float.IsFinite(distance))
                        throw new InvalidOperationException("Nonfinite KB source equivalence.");
                    record.worstNativeRotationDegrees = Mathf.Max(record.worstNativeRotationDegrees, angle);
                    record.worstNativePositionMetres = Mathf.Max(record.worstNativePositionMetres, distance);
                    rows.AppendLine(FormattableString.Invariant(
                        $"{seconds:R},{index + 1},{bone},{angle:R},{distance:R},{carry.x:R},{carry.y:R},{carry.z:R}"));
                }
                record.nativeSamples++;
                if (record.worstNativeRotationDegrees > .1f || record.worstNativePositionMetres > .001f)
                    throw new InvalidOperationException("KB native equivalence exceeded 0.001m/0.1deg at " + seconds +
                        ": " + record.worstNativePositionMetres + "m / " + record.worstNativeRotationDegrees + "deg");
            }

            public void Dispose()
            {
                if (graph.IsValid())
                    graph.Destroy();
                if (driver)
                    Object.DestroyImmediate(driver.gameObject);
            }
        }
    }
}
