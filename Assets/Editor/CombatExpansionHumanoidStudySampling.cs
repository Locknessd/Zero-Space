using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    public static partial class CombatExpansionHumanoidStudy
    {
        sealed partial class MotionPreview : IDisposable
        {
            readonly Scene scene;
            readonly FrankTestDriver driver;
            readonly Animator character;
            readonly AnimationClip clip;
            readonly Transform[] sourceTransforms;
            readonly Vector3[] restPositions, restScales;
            readonly Quaternion[] restRotations;
            readonly List<(string role, string name, Transform bone)> bones =
                new List<(string, string, Transform)>();
            PlayableGraph graph;
            float currentTime;
            Bounds motionBounds;
            bool haveBounds;
            public float Duration => clip.length;

            public MotionPreview(Animator fighter, FrankTestDriver template, AnimationClip source)
            {
                scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    character = Object.Instantiate(fighter.gameObject).GetComponent<Animator>();
                    SceneManager.MoveGameObjectToScene(character.gameObject, scene);
                    FrankRetargetBuilder.StripStudyComponents(character.gameObject, false);
                    character.runtimeAnimatorController = null;
                    character.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    character.transform.localScale = fighter.transform.lossyScale;
                    character.gameObject.SetActive(true);
                    foreach (var renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                        renderer.updateWhenOffscreen = true;
                    driver = Object.Instantiate(template);
                    SceneManager.MoveGameObjectToScene(driver.gameObject, scene);
                    driver.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    driver.gameObject.SetActive(true);
                    FrankRetargetBuilder.StripStudyComponents(driver.gameObject, true);
                    driver.pose.driver.avatar = driver.pose.sourceHumanAvatar;
                    driver.pose.driver.applyRootMotion = true;
                    driver.pose.driver.enabled = true;
                    driver.pose.driver.Rebind();
                    if (!driver.pose.driver.isHuman || !driver.pose.driver.avatar.isValid)
                        throw new InvalidOperationException("Generated source Animator is not a valid humanoid");
                    driver.Bind(character, true);
                    driver.pose.transferFingers = true;
                    clip = Object.Instantiate(source);
                    clip.hideFlags = HideFlags.HideAndDontSave;
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = false;
                    settings.loopBlend = false;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    sourceTransforms = driver.GetComponentsInChildren<Transform>(true);
                    restPositions = sourceTransforms.Select(t => t.localPosition).ToArray();
                    restRotations = sourceTransforms.Select(t => t.localRotation).ToArray();
                    restScales = sourceTransforms.Select(t => t.localScale).ToArray();
                    AddBones("source", driver.pose.driver);
                    AddBones("fighter", character);
                    foreach (var socket in driver.GetComponentsInChildren<Transform>(true)
                        .Where(t => t.name == "L_axe_wp" || t.name == "R_axe_wp"))
                        bones.Add(("weapon", socket.name, socket));
                    foreach (var renderer in driver.pose.weaponRenderers)
                    {
                        if (renderer is SkinnedMeshRenderer skin)
                            skin.updateWhenOffscreen = true;
                        bones.Add(("weaponMesh", renderer.name, renderer.transform));
                    }
                    Reset();
                    CreateCamera();
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            void AddBones(string role, Animator animator)
            {
                bones.Add((role, "AnimatorRoot", animator.transform));
                for (int index = 0; index < (int)HumanBodyBones.LastBone; index++)
                {
                    var bone = animator.GetBoneTransform((HumanBodyBones)index);
                    if (bone)
                        bones.Add((role, ((HumanBodyBones)index).ToString(), bone));
                }
            }

            void Reset()
            {
                if (graph.IsValid())
                    graph.Destroy();
                driver.pose.driver.Rebind();
                for (int i = 0; i < sourceTransforms.Length; i++)
                {
                    sourceTransforms[i].SetLocalPositionAndRotation(restPositions[i], restRotations[i]);
                    sourceTransforms[i].localScale = restScales[i];
                }
                graph = PlayableGraph.Create("Humanoid source study");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                playable.SetSpeed(1);
                var output = AnimationPlayableOutput.Create(graph, "Native humanoid source", driver.pose.driver);
                output.SetSourcePlayable(playable);
                graph.Play();
                currentTime = 0;
                graph.Evaluate(0);
                driver.pose.ApplyPose();
            }

            void Evaluate(float seconds)
            {
                if (seconds < currentTime - .000001f)
                    Reset();
                while (currentTime < seconds)
                {
                    float delta = Mathf.Min(1f / 60, seconds - currentTime);
                    if (delta <= 0)
                        break;
                    graph.Evaluate(delta);
                    currentTime = Mathf.Min(seconds, currentTime + delta);
                }
                driver.pose.ApplyPose();
            }

            public void WriteTrajectories(string path, StudyRecord record)
            {
                Reset();
                using (var writer = new StreamWriter(path))
                {
                    writer.WriteLine("guid,localId,fighter,seconds,role,bone,x,y,z,qx,qy,qz,qw");
                    int last = Mathf.CeilToInt(Duration * 60);
                    for (int frame = 0; frame <= last; frame++)
                    {
                        float seconds = Mathf.Min(frame / 60f, Duration);
                        Evaluate(seconds);
                        foreach (var entry in bones)
                        {
                            var p = entry.bone.position;
                            var q = entry.bone.rotation;
                            if (!float.IsFinite(p.x) || !float.IsFinite(p.y) || !float.IsFinite(p.z))
                                throw new InvalidOperationException(
                                    "Nonfinite pose: " + record.guid + ":" + record.localId);
                            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                "{0},{1},{2},{3:R},{4},{5},{6:R},{7:R},{8:R},{9:R},{10:R},{11:R},{12:R}",
                                record.guid, record.localId, record.fighter, seconds, entry.role, entry.name,
                                p.x, p.y, p.z, q.x, q.y, q.z, q.w));
                            if (!haveBounds)
                            {
                                motionBounds = new Bounds(p, Vector3.one * .1f);
                                haveBounds = true;
                            }
                            else
                                motionBounds.Encapsulate(p);
                        }
                        foreach (var renderer in character.GetComponentsInChildren<Renderer>(true)
                            .Concat(driver.pose.weaponRenderers))
                            if (renderer.enabled)
                                motionBounds.Encapsulate(renderer.bounds);
                        if (frame == 0)
                        {
                            record.rootStart = driver.transform.position;
                            record.hipsStart = driver.pose.sourceHips.position;
                        }
                        record.rootEnd = driver.transform.position;
                        record.hipsEnd = driver.pose.sourceHips.position;
                        record.trajectorySamples++;
                    }
                }
            }

            public void Dispose()
            {
                if (graph.IsValid())
                    graph.Destroy();
                if (clip)
                    Object.DestroyImmediate(clip);
                if (scene.IsValid())
                    EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
