using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        sealed class NativeReferenceActor : IDisposable
        {
            public readonly Animator Animator;
            public readonly SourceRecord Source;
            public readonly Transform Blade;
            public readonly Vector3 PrefabScale;
            readonly AnimationClip clip;
            readonly Transform[] nodes;
            readonly Vector3[] positions, scales;
            readonly Quaternion[] rotations;
            PlayableGraph graph;
            float time;

            public NativeReferenceActor(Scene scene, SourceRecord source, int role, int direction,
                AnimationClip clipOverride = null)
            {
                if (clipOverride && (!clipOverride.humanMotion || clipOverride.legacy ||
                    clipOverride.name != source.clip || !float.IsFinite(clipOverride.length) ||
                    Mathf.Abs(clipOverride.length - source.durationSeconds) > .00001f))
                    throw new InvalidOperationException("Native clip override type, name or length differs from source.");
                Source = source;
                try
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(source.path);
                    if (!prefab || CombatExpansionInventory.Identity(prefab.GetComponent<Animator>().avatar) !=
                        source.avatar)
                        throw new InvalidOperationException("Native source prefab/Avatar identity changed.");
                    PrefabScale = prefab.transform.localScale;
                    Animator = Object.Instantiate(prefab).GetComponent<Animator>();
                    SceneManager.MoveGameObjectToScene(Animator.gameObject, scene);
                    Animator.gameObject.name = "NativeReference_" + Roles[role];
                    Animator.gameObject.SetActive(true);
                    Animator.runtimeAnimatorController = null;
                    Animator.applyRootMotion = true;
                    Animator.enabled = true;
                    Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    var lane = Quaternion.Euler(0, direction * 90, 0);
                    Animator.transform.SetPositionAndRotation(
                        lane * (role == 0 ? Vector3.zero : new Vector3(0, 0, 1.7f)),
                        lane * Quaternion.Euler(0, role * 180, 0));
                    Animator.Rebind();
                    if (!Animator.isHuman || !Animator.avatar.isValid || Animator.transform.localScale != PrefabScale)
                        throw new InvalidOperationException("Native humanoid bind or prefab scale failed.");
                    nodes = Animator.GetComponentsInChildren<Transform>(true);
                    Blade = nodes.Single(t => t.name == "BladeR");
                    foreach (string weapon in Swords)
                    {
                        var socket = nodes.Single(t => t.name == weapon);
                        var renderers = socket.GetComponentsInChildren<Renderer>(true);
                        if (renderers.Length == 0)
                            throw new InvalidOperationException("Missing native weapon renderer " + weapon);
                        if (role == 1)
                            foreach (var renderer in renderers)
                                renderer.enabled = false;
                    }
                    foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Head,
                        HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                        if (!Animator.GetBoneTransform(bone))
                            throw new InvalidOperationException("Missing native bone " + bone);
                    foreach (var skin in Animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        skin.updateWhenOffscreen = true;
                    positions = nodes.Select(t => t.localPosition).ToArray();
                    rotations = nodes.Select(t => t.localRotation).ToArray();
                    scales = nodes.Select(t => t.localScale).ToArray();
                    clip = Object.Instantiate(clipOverride ? clipOverride : source.asset);
                    clip.hideFlags = HideFlags.HideAndDontSave;
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = false;
                    settings.loopBlend = false;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
                    Reset();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Reset()
            {
                if (graph.IsValid())
                    graph.Destroy();
                Animator.Rebind();
                for (int i = 0; i < nodes.Length; i++)
                {
                    nodes[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
                    nodes[i].localScale = scales[i];
                }
                graph = PlayableGraph.Create("Native reference " + Source.clip);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                AnimationPlayableOutput.Create(graph, "Original source Avatar", Animator).SetSourcePlayable(playable);
                graph.Play();
                time = 0;
                graph.Evaluate(0);
            }

            public void Evaluate(float seconds)
            {
                if (!float.IsFinite(seconds))
                    throw new InvalidOperationException("Nonfinite native sample time.");
                seconds = Mathf.Clamp(seconds, 0, clip.length);
                // A non-grid diagnostic sample must not split the next canonical root-motion step.
                bool partialStep = Mathf.Abs(time * 60 - Mathf.Round(time * 60)) > .0001f;
                if (seconds < time || (seconds > time && partialStep))
                    Reset();
                // Advance the global 60 Hz grid; only the exact endpoint uses a partial final step.
                while (time < seconds)
                {
                    float next = Mathf.Min(seconds, (Mathf.Floor(time * 60 + .0001f) + 1) / 60);
                    if (next <= time)
                        throw new InvalidOperationException("Native playback failed to advance.");
                    graph.Evaluate(next - time);
                    time = next;
                }
            }

            public void Dispose()
            {
                if (graph.IsValid())
                    graph.Destroy();
                if (clip)
                    Object.DestroyImmediate(clip);
                if (Animator)
                    Object.DestroyImmediate(Animator.gameObject);
            }
        }
    }
}
