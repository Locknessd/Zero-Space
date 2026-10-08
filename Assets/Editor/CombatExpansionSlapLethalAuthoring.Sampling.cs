using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapLethalAuthoring
    {
        sealed class MeasuredPose
        {
            public float time, scale;
            public Vector3 body, root;
            public Quaternion bodyRotation, rootRotation;
            public float[] muscles, streamMuscles;
            public string[] boneNames;
            public Vector3[] localBones;
            public Quaternion[] localRotations;
            public Vector3[] bones;
            public Quaternion[] rotations;
        }

        sealed class PoseSampler : IDisposable
        {
            public readonly Animator animator;
            readonly GameObject instance;
            readonly AnimationClip clip;
            readonly HumanPoseHandler handler;
            readonly Transform[] transforms, bones;
            readonly string[] boneNames;
            readonly MuscleStreamCapture streamCapture;
            readonly Vector3[] positions, scales;
            readonly Quaternion[] rotations;
            PlayableGraph graph;
            HumanPose pose;
            float clock;

            public PoseSampler(Scene scene, string rigPath, AnimationClip source, bool captureMuscles = false)
            {
                try
                {
                    var model = AssetDatabase.LoadAssetAtPath<GameObject>(rigPath);
                    instance = Object.Instantiate(model);
                    instance.hideFlags = HideFlags.HideAndDontSave;
                    SceneManager.MoveGameObjectToScene(instance, scene);
                    instance.SetActive(true);
                    instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    instance.transform.localScale = Vector3.one;
                    animator = instance.GetComponent<Animator>();
                    if (!animator || !animator.avatar || !animator.avatar.isHuman || !animator.avatar.isValid)
                        throw new InvalidOperationException("Invalid empirical receiver rig: " + rigPath);
                    animator.runtimeAnimatorController = null;
                    animator.enabled = true;
                    animator.applyRootMotion = true;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.fireEvents = false;
                    animator.Rebind();
                    foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
                        renderer.enabled = false;
                    transforms = instance.GetComponentsInChildren<Transform>(true);
                    positions = transforms.Select(t => t.localPosition).ToArray();
                    rotations = transforms.Select(t => t.localRotation).ToArray();
                    scales = transforms.Select(t => t.localScale).ToArray();
                    var mapped = Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                        .Where(i => animator.GetBoneTransform((HumanBodyBones)i)).ToArray();
                    bones = mapped.Select(i => animator.GetBoneTransform((HumanBodyBones)i)).ToArray();
                    boneNames = mapped.Select((id, i) => ((HumanBodyBones)id) + ":" +
                        AnimationUtility.CalculateTransformPath(bones[i], animator.transform)).ToArray();
                    if (captureMuscles)
                        streamCapture = new MuscleStreamCapture();
                    clip = Object.Instantiate(source);
                    clip.hideFlags = HideFlags.HideAndDontSave;
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = false;
                    settings.loopBlend = false;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
                    handler = new HumanPoseHandler(animator.avatar, animator.transform);
                    pose = new HumanPose { muscles = new float[HumanTrait.MuscleCount] };
                    Reset();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            void Reset()
            {
                if (graph.IsValid())
                    graph.Destroy();
                animator.Rebind();
                for (int i = 0; i < transforms.Length; i++)
                {
                    transforms[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
                    transforms[i].localScale = scales[i];
                }
                graph = PlayableGraph.Create("Slap lethal empirical pose");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                var output = AnimationPlayableOutput.Create(graph, "Humanoid source", animator);
                if (streamCapture == null)
                    output.SetSourcePlayable(playable);
                else
                    output.SetSourcePlayable(streamCapture.Connect(graph, playable));
                graph.Play();
                graph.Evaluate(0);
                clock = 0;
            }

            public MeasuredPose At(float seconds)
            {
                seconds = Mathf.Clamp(seconds, 0, clip.length);
                if (seconds < clock - .0000001f)
                    Reset();
                while (clock < seconds)
                {
                    float next = Mathf.Min(seconds, clock + 1f / 240);
                    graph.Evaluate(next - clock);
                    clock = next;
                }
                return ReadPose(seconds);
            }

            public MeasuredPose InverseRoundtrip(float seconds)
            {
                At(seconds);
                handler.SetHumanPose(ref pose);
                var result = ReadPose(seconds);
                Reset();
                return result;
            }

            MeasuredPose ReadPose(float seconds)
            {
                handler.GetHumanPose(ref pose);
                var result = new MeasuredPose
                {
                    time = seconds,
                    scale = animator.humanScale,
                    // Unity 6000.5 AnimationModule XML documents GetHumanPose as world COM / humanScale.
                    // Animator.bodyPosition is an IK-pass API; use the documented HumanPoseHandler result instead.
                    body = pose.bodyPosition * animator.humanScale,
                    bodyRotation = pose.bodyRotation,
                    root = animator.transform.position,
                    rootRotation = animator.transform.rotation,
                    muscles = (float[])pose.muscles.Clone(),
                    streamMuscles = streamCapture?.Read(),
                    boneNames = boneNames,
                    localBones = bones.Select(b => b.localPosition).ToArray(),
                    localRotations = bones.Select(b => b.localRotation).ToArray(),
                    bones = bones.Select(b => b.position).ToArray(),
                    rotations = bones.Select(b => b.rotation).ToArray()
                };
                CheckPoseFinite(result);
                return result;
            }

            public void Dispose()
            {
                if (graph.IsValid())
                    graph.Destroy();
                streamCapture?.Dispose();
                handler?.Dispose();
                if (clip)
                    Object.DestroyImmediate(clip);
                if (instance)
                    Object.DestroyImmediate(instance);
            }
        }

        static void CheckPoseFinite(MeasuredPose pose)
        {
            if (!float.IsFinite(pose.scale) || pose.scale <= 0 || !Finite(pose.body) || !Finite(pose.root) ||
                !Finite(pose.bodyRotation) || !Finite(pose.rootRotation) ||
                (pose.streamMuscles != null && pose.streamMuscles.Any(v => !float.IsFinite(v))) ||
                pose.muscles.Any(v => !float.IsFinite(v)) || pose.bones.Any(v => !Finite(v)) ||
                pose.rotations.Any(v => !Finite(v)))
                throw new InvalidOperationException("Nonfinite empirically sampled Humanoid pose at " + pose.time);
        }

        static bool Finite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        static bool Finite(Quaternion value)
        {
            float norm = Quaternion.Dot(value, value);
            return float.IsFinite(norm) && Mathf.Abs(norm - 1) < .01f;
        }
    }
}
