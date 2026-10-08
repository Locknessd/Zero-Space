using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSlapStudy
    {
        sealed class Actor : IDisposable
        {
            public readonly Animator Character;
            public readonly FrankTestDriver Driver;
            public readonly SourceRecord Source;
            public readonly string Fighter;
            public readonly List<(string rig, string bone, Transform node)> Bones =
                new List<(string, string, Transform)>();
            readonly AnimationClip clip;
            readonly Transform[] transforms;
            readonly Vector3[] positions, scales;
            readonly Quaternion[] rotations;
            PlayableGraph graph;
            float time;
            int tick;

            public Actor(Scene scene, CharacterCombat fighter, SourceRecord source)
            {
                Source = source;
                Fighter = fighter.name;
                try
                {
                    string path = DriverPath(Fighter, source);
                    var template = AssetDatabase.LoadAssetAtPath<FrankTestDriver>(path);
                    ValidateDriver(template, source, path);
                    Character = Object.Instantiate(fighter.Animator.gameObject).GetComponent<Animator>();
                    SceneManager.MoveGameObjectToScene(Character.gameObject, scene);
                    FrankRetargetBuilder.StripStudyComponents(Character.gameObject, false);
                    Character.name = source.label + "_" + Fighter;
                    Character.runtimeAnimatorController = null;
                    Character.transform.localScale = fighter.Animator.transform.lossyScale;
                    Character.gameObject.SetActive(true);
                    Driver = Object.Instantiate(template);
                    SceneManager.MoveGameObjectToScene(Driver.gameObject, scene);
                    FrankRetargetBuilder.StripStudyComponents(Driver.gameObject, true);
                    Driver.gameObject.SetActive(true);
                    Character.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Driver.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    Driver.pose.driver.runtimeAnimatorController = null;
                    Driver.pose.driver.avatar = Driver.pose.sourceHumanAvatar;
                    Driver.pose.driver.applyRootMotion = true;
                    Driver.pose.driver.enabled = true;
                    Driver.pose.driver.Rebind();
                    if (!Driver.pose.driver.isHuman)
                        throw new InvalidOperationException("Native Animator could not bind: " + path);
                    Driver.Bind(Character, true);
                    Driver.enabled = false;
                    Driver.pose.enabled = false;
                    Driver.pose.transferFingers = true;
                    clip = Object.Instantiate(source.asset);
                    clip.hideFlags = HideFlags.HideAndDontSave;
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = false;
                    settings.loopBlend = false;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
                    transforms = Driver.GetComponentsInChildren<Transform>(true);
                    positions = transforms.Select(t => t.localPosition).ToArray();
                    rotations = transforms.Select(t => t.localRotation).ToArray();
                    scales = transforms.Select(t => t.localScale).ToArray();
                    AddBones("native", Driver.pose.driver);
                    AddBones("fighter", Character);
                    foreach (var skin in Character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        skin.updateWhenOffscreen = true;
                    Reset();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            void AddBones(string rig, Animator animator)
            {
                Bones.Add((rig, "AnimatorRoot", animator.transform));
                foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftFoot,
                    HumanBodyBones.RightFoot, HumanBodyBones.LeftHand, HumanBodyBones.RightHand, HumanBodyBones.Head })
                {
                    var node = animator.GetBoneTransform(bone);
                    if (!node)
                        throw new InvalidOperationException("Missing " + rig + " " + bone + " on " + Fighter);
                    Bones.Add((rig, bone.ToString(), node));
                }
            }

            public void Reset()
            {
                if (graph.IsValid())
                    graph.Destroy();
                Driver.pose.driver.Rebind();
                for (int i = 0; i < transforms.Length; i++)
                {
                    transforms[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
                    transforms[i].localScale = scales[i];
                }
                graph = PlayableGraph.Create("Slap individual " + Source.clip + " " + Fighter);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                var output = AnimationPlayableOutput.Create(graph, "Native source", Driver.pose.driver);
                output.SetSourcePlayable(playable);
                graph.Play();
                time = 0;
                tick = 0;
                graph.Evaluate(0);
                Driver.pose.ApplyPose();
            }

            public void Evaluate(float seconds)
            {
                seconds = Mathf.Clamp(seconds, 0, clip.length);
                if (seconds < time - .000001f)
                    Reset();
                while (time < seconds)
                {
                    float next = Mathf.Min((tick + 1) / 60f, clip.length);
                    if (next > seconds + .000001f)
                        throw new InvalidOperationException("Slap evidence requires exact 60Hz sample ticks");
                    if (next <= time)
                        break;
                    graph.Evaluate(next - time);
                    time = next;
                    tick++;
                }
                Driver.pose.ApplyPose();
            }

            public void Dispose()
            {
                if (graph.IsValid())
                    graph.Destroy();
                if (clip)
                    Object.DestroyImmediate(clip);
                if (Driver)
                    Object.DestroyImmediate(Driver.gameObject);
                if (Character)
                    Object.DestroyImmediate(Character.gameObject);
            }
        }
    }
}
