using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FrankRetarget;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class BattleAttackReview
    {
        internal static void Capture(Scene scene, FrankBattlePairPlayback playback, float[] times, string path,
            BattleVfxPlayer vfx = null, Dictionary<GameObject, float> births = null)
        {
            const int width = 360;
            const int height = 260;
            const int columns = 4;
            int rows = Mathf.CeilToInt(times.Length / (float)columns);
            var root = new GameObject("Attack review camera");
            SceneManager.MoveGameObjectToScene(root, scene);
            var camera = root.AddComponent<Camera>();
            camera.enabled = false;
            camera.scene = scene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.1f, .15f, .22f);
            camera.orthographic = true;
            camera.orthographicSize = 1.5f;
            var lightRoot = new GameObject("Attack review light");
            SceneManager.MoveGameObjectToScene(lightRoot, scene);
            var light = lightRoot.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.5f;
            light.transform.rotation = Quaternion.Euler(35, -25, 0);
            var target = new RenderTexture(width, height, 24);
            var frame = new Texture2D(width, height, TextureFormat.RGB24, false);
            var sheet = new Texture2D(width * columns, height * rows, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            float displayed = playback.SampleTime;
            try
            {
                camera.targetTexture = target;
                for (int i = 0; i < times.Length; i++)
                {
                    playback.EvaluateAt(times[i]);
                    if (vfx)
                    {
                        vfx.AdvanceSequence(playback, times[i]);
                        foreach (var effect in births.Where(p => p.Key && p.Key.activeSelf))
                        foreach (var particles in effect.Key.GetComponentsInChildren<ParticleSystem>(true))
                            particles.Simulate(Mathf.Max(0, times[i] - effect.Value) + .045f, false, true, true);
                    }
                    var bones = new[] { playback.AttackerActor.character, playback.ReceiverActor.character }
                        .SelectMany(a => new[] { HumanBodyBones.Hips, HumanBodyBones.Head,
                            HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot })
                        .Select((bone, index) => (index < 4 ? playback.AttackerActor.character :
                            playback.ReceiverActor.character).GetBoneTransform(bone).position).ToArray();
                    var bounds = new Bounds(bones[0], Vector3.zero);
                    foreach (var bone in bones)
                        bounds.Encapsulate(bone);
                    Vector3 center = bounds.center;
                    camera.orthographicSize = Mathf.Max(1.4f,
                        bounds.extents.y + .45f, bounds.extents.x * height / width + .4f);
                    camera.transform.position = center + new Vector3(0, .25f, 6);
                    camera.transform.LookAt(center);
                    camera.Render();
                    RenderTexture.active = target;
                    frame.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    frame.Apply();
                    sheet.SetPixels((i % columns) * width, (rows - 1 - i / columns) * height,
                        width, height, frame.GetPixels());
                }
                sheet.Apply();
                File.WriteAllBytes(path, sheet.EncodeToPNG());
                File.WriteAllLines(path + ".times.txt",
                    times.Select((time, index) => index + ": " + time.ToString("R")));
            }
            finally
            {
                if (playback.Playing)
                    playback.EvaluateAt(displayed);
                RenderTexture.active = previous;
                Object.DestroyImmediate(sheet);
                Object.DestroyImmediate(frame);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(lightRoot);
            }
        }

        static void CaptureEffects(Scene scene, FrankBattlePairPlayback playback, BattleVfxPlayer vfx,
            BattleSfxBank.Move profile, string moveName)
        {
            var source = playback.AttackerActor.character.GetComponentInParent<CharacterCombat>();
            var receiver = playback.ReceiverActor.character.GetComponentInParent<CharacterCombat>();
            var births = new Dictionary<GameObject, float>();
            Action<string, GameObject> observe = (id, root) => births[root] = playback.SampleTime;
            vfx.EffectPlayed += observe;
            try
            {
                vfx.ResetForMatch();
                playback.EvaluateAt(0);
                vfx.BeginSequence(playback, source, receiver, playback.Move, false);
                var times = profile.cues.Where(c => c.group.EndsWith("swing") || IsContact(c) ||
                    c.group == "gun_shot" || c.group == "body_fall").Select(c => c.seconds).Distinct().OrderBy(t => t)
                    .ToArray();
                Capture(scene, playback, times, Output + "/" + moveName + "_vfx.png", vfx, births);
            }
            finally
            {
                vfx.EffectPlayed -= observe;
            }
        }
    }
}
