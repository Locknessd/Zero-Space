using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class FrankRetargetBuilder
    {
        public static void CaptureBattleAccentReference()
        {
            if (!Application.isPlaying)
            {
                SessionState.SetBool("BattleAccentReference.Pending", true);
                File.WriteAllText(AccentReview + "/Reference/Decode.txt", "Reference extraction queued for isolated Play validation.");
                return;
            }
            const string video = @"C:\Users\LNc\Documents\ShareX\Screenshots\2026-10\chrome_z7qjVQqdHf.mp4";
            Directory.CreateDirectory(AccentReview + "/Reference");
            if (!File.Exists(video)) throw new Exception("Previously supplied reference video is unavailable.");
            var root = new GameObject("Battle reference decoder") { hideFlags = HideFlags.HideAndDontSave };
            var player = root.AddComponent<VideoPlayer>(); player.url = video;
            player.audioOutputMode = VideoAudioOutputMode.None; player.sendFrameReadyEvents = true;
            player.isLooping = player.playOnAwake = false; player.renderMode = VideoRenderMode.RenderTexture;
            RenderTexture target = null; int index = 0; long next = 15; double started = EditorApplication.timeSinceStartup;
            bool ended = false;
            Action finish = () =>
            {
                if (ended) return; ended = true; player.Stop();
                EditorApplication.delayCall += () => { if (root) Object.DestroyImmediate(root); if (target) Object.DestroyImmediate(target); };
            };
            player.errorReceived += (p, error) => { File.WriteAllText(AccentReview + "/Reference/Decode.txt", error); finish(); };
            player.prepareCompleted += p =>
            {
                target = new RenderTexture((int)p.width, (int)p.height, 0); p.targetTexture = target;
                next = (long)(p.frameRate * .5); p.frame = next; p.Play();
            };
            player.frameReady += (p, frame) =>
            {
                if (ended || frame < next) return;
                var previous = RenderTexture.active; RenderTexture.active = target;
                var texture = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); texture.Apply();
                File.WriteAllBytes(AccentReview + "/Reference/Frame" + index + ".png", texture.EncodeToPNG());
                Object.DestroyImmediate(texture); RenderTexture.active = previous;
                if (++index == 5) { File.WriteAllText(AccentReview + "/Reference/Decode.txt", "Decoded five frames from the user's supplied local video."); finish(); }
                else { next = (long)(p.frameRate * (.5 + index * 2)); p.frame = next; }
            };
            EditorApplication.CallbackFunction timeout = null;
            timeout = () => { if (ended) EditorApplication.update -= timeout; else if (EditorApplication.timeSinceStartup - started > 45) { File.WriteAllText(AccentReview + "/Reference/Decode.txt", "Decoder timeout; reference frames " + index); finish(); } };
            EditorApplication.update += timeout; player.Prepare();
        }
    }
    [InitializeOnLoad]
    static class BattleAccentReferenceBootstrap
    {
        static BattleAccentReferenceBootstrap()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool("BattleAccentReference.Pending", false)) return;
                SessionState.SetBool("BattleAccentReference.Pending", false);
                FrankRetargetBuilder.CaptureBattleAccentReference();
            };
        }
    }
}
