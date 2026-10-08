using System;
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
    public static partial class CombatExpansionGreatSwordRecoveryStudy
    {
        sealed partial class RecoveryPreview : IDisposable
        {
            readonly Scene scene;
            readonly Animator actor;
            readonly AnimationClip clip;
            readonly string clipIdentity;
            readonly string avatarIdentity;
            readonly Transform[] transforms;
            readonly Vector3[] positions;
            readonly Quaternion[] rotations;
            readonly Vector3[] scales;
            readonly Transform[] bones;
            PlayableGraph graph;
            float time;
            Bounds motionBounds;
            bool haveBounds;
            bool haveMotion;
            public float[] FrameSeconds { get; private set; }

            public RecoveryPreview(Animator original, AnimationClip source)
            {
                scene = EditorSceneManager.NewPreviewScene();
                try
                {
                    clipIdentity = CombatExpansionInventory.Identity(source);
                    avatarIdentity = CombatExpansionInventory.Identity(original.avatar);
                    actor = Object.Instantiate(original.gameObject).GetComponent<Animator>();
                    SceneManager.MoveGameObjectToScene(actor.gameObject, scene);
                    FrankRetargetBuilder.StripStudyComponents(actor.gameObject, false);
                    foreach (var other in actor.GetComponentsInChildren<Animator>(true).Where(a => a != actor))
                        Object.DestroyImmediate(other);
                    actor.gameObject.SetActive(true);
                    actor.runtimeAnimatorController = null;
                    actor.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    actor.transform.localScale = original.transform.lossyScale;
                    actor.applyRootMotion = true;
                    actor.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    actor.enabled = true;
                    actor.Rebind();
                    bones = Bones.Select(b => actor.GetBoneTransform(b)).ToArray();
                    if (bones.Any(b => !b))
                        throw new InvalidOperationException("Required recovery bone missing on " + original.name);
                    var visible = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .Where(r => r.enabled && r.gameObject.activeInHierarchy && r.sharedMesh &&
                            r.sharedMesh.vertexCount > 0 && r.bones.Length > 0).ToArray();
                    if (visible.Length == 0)
                        throw new InvalidOperationException("No visible skinned fighter geometry.");
                    foreach (var renderer in visible)
                        renderer.updateWhenOffscreen = true;
                    clip = Object.Instantiate(source);
                    clip.hideFlags = HideFlags.HideAndDontSave;
                    var settings = AnimationUtility.GetAnimationClipSettings(clip);
                    settings.loopTime = false;
                    settings.loopBlend = false;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    transforms = actor.GetComponentsInChildren<Transform>(true);
                    positions = transforms.Select(t => t.localPosition).ToArray();
                    rotations = transforms.Select(t => t.localRotation).ToArray();
                    scales = transforms.Select(t => t.localScale).ToArray();
                    Reset();
                    CreateCamera();
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
                actor.Rebind();
                for (int i = 0; i < transforms.Length; i++)
                {
                    transforms[i].SetLocalPositionAndRotation(positions[i], rotations[i]);
                    transforms[i].localScale = scales[i];
                }
                graph = PlayableGraph.Create("Actual Avatar recovery motion");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                playable.SetSpeed(1);
                var output = AnimationPlayableOutput.Create(graph, "Recovery on actual fighter Avatar", actor);
                output.SetSourcePlayable(playable);
                graph.Play();
                time = 0;
                graph.Evaluate(0);
            }

            void Evaluate(float seconds)
            {
                if (seconds < time)
                    Reset();
                while (time < seconds)
                {
                    float delta = Mathf.Min(1f / 60, seconds - time);
                    graph.Evaluate(delta);
                    time = Mathf.Min(seconds, time + delta);
                }
                foreach (var bone in bones)
                    if (!float.IsFinite(bone.position.x) || !float.IsFinite(bone.position.y) ||
                        !float.IsFinite(bone.position.z) || !float.IsFinite(bone.rotation.w))
                        throw new InvalidOperationException("Nonfinite recovery pose for " + clipIdentity);
            }

            public void WriteBones(string path)
            {
                Reset();
                using var writer = new StreamWriter(path);
                writer.WriteLine("clipGuidLocalId,avatarGuidLocalId,seconds,bone,x,y,z," +
                    "rightX,rightY,rightZ,upX,upY,upZ,forwardX,forwardY,forwardZ");
                foreach (float seconds in new[] { 0, .1f, .25f, clip.length })
                {
                    Evaluate(seconds);
                    for (int i = 0; i < bones.Length; i++)
                    {
                        var bone = bones[i];
                        var p = bone.position;
                        var r = bone.right;
                        var u = bone.up;
                        var f = bone.forward;
                        writer.WriteLine(FormattableString.Invariant(
                            $"{clipIdentity},{avatarIdentity},{seconds:R},{Bones[i]},{p.x:R},{p.y:R},{p.z:R},") +
                            FormattableString.Invariant(
                            $"{r.x:R},{r.y:R},{r.z:R},{u.x:R},{u.y:R},{u.z:R},{f.x:R},{f.y:R},{f.z:R}"));
                    }
                }
            }

            void MeasureMotion(CombatExpansionPreviewSkin skin)
            {
                Reset();
                var initialPositions = bones.Select(b => b.position).ToArray();
                var initialRotations = bones.Select(b => b.rotation).ToArray();
                int last = Mathf.CeilToInt(clip.length * 60);
                for (int frame = 0; frame <= last; frame++)
                {
                    Evaluate(Mathf.Min(frame / 60f, clip.length));
                    skin.Sample();
                    foreach (var renderer in actor.gameObject.scene.GetRootGameObjects()
                        .SelectMany(r => r.GetComponentsInChildren<MeshRenderer>())
                        .Where(r => r.enabled && r.gameObject.activeInHierarchy))
                    {
                        if (!haveBounds)
                        {
                            motionBounds = renderer.bounds;
                            haveBounds = true;
                        }
                        else
                            motionBounds.Encapsulate(renderer.bounds);
                    }
                    for (int i = 0; i < bones.Length; i++)
                        haveMotion |= Vector3.Distance(bones[i].position, initialPositions[i]) > .005f ||
                            Quaternion.Angle(bones[i].rotation, initialRotations[i]) > 1;
                }
                if (!haveBounds || motionBounds.size.sqrMagnitude < .01f || !haveMotion)
                    throw new InvalidOperationException("No visible animated recovery pose: " + clipIdentity);
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
