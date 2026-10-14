using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using ZeroSpace.Showcase;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseCapture
    {
        const int Width = 1920;
        const int Height = 1080;
        const int Rate = 60;
        static string Video = PepeShowcaseSetup.Output + "/PepeAttackShowcase.mp4";
        static bool recordingCombo;
        static bool recordingComplete;
        static PepeAttackShowcase playShowcase;
        static RenderTexture target;
        static Texture2D frame;
        static byte[] pixels;
        static Process encoder;
        static int index;
        static int count;
        static Stopwatch timer;
        static int previousCaptureRate;
        static int previousTargetRate;
        static bool previousBackground;
        static bool reloadLocked;

        [MenuItem("Tools/Pepe Showcase/Record 1080p 60 fps video")]
        public static void Begin()
        {
            FindEncoder();
            PepeShowcasePlayCheck.BeginRecording();
            recordingCombo = false;
            Video = PepeShowcaseSetup.Output + "/PepeAttackShowcase.mp4";
            Status("STARTING isolated Play mode recording.");
        }

        [MenuItem("Tools/Pepe Showcase/Record Assassin long combo")]
        public static void BeginAssassinCombo()
        {
            FindEncoder();
            PepeShowcasePlayCheck.BeginRecording(PepeShowcaseSetup.ComboScenePath);
            recordingCombo = true;
            Video = PepeShowcaseSetup.Output + "/PepeAssassinLongCombo.mp4";
            Status("STARTING isolated Assassin combo recording.");
        }

        [MenuItem("Tools/Pepe Showcase/Record complete combo video")]
        public static void BeginCompleteCombo()
        {
            FindEncoder();
            PepeShowcasePlayCheck.BeginRecording(PepeShowcaseSetup.CompleteScenePath);
            Status("STARTING isolated complete combo recording.");
        }

        internal static void StartPlayRecording(PepeAttackShowcase showcase)
        {
            if (encoder != null) throw new InvalidOperationException("A recording is already running.");
            // Play mode may reload the domain after Begin, so derive the destination from the loaded scene.
            recordingComplete = showcase.library.continuous;
            recordingBom = showcase.library.bomTrailer;
            recordingCombo = recordingComplete ||
                showcase.library.takes.Length == 1 && showcase.library.takes[0].attackTrack;
            Video = PepeShowcaseSetup.Output + (recordingBom ? "/BOM_Pepe_CharacterTrailer.mp4" :
                recordingComplete ? "/PepeCompleteCombo.mp4" :
                recordingCombo ? "/PepeAssassinLongCombo.mp4" : "/PepeAttackShowcase.mp4");
            string executable = FindEncoder();
            Directory.CreateDirectory(PepeShowcaseSetup.Output);
            try
            {
                EditorApplication.LockReloadAssemblies();
                AssetDatabase.DisallowAutoRefresh();
                reloadLocked = true;
                playShowcase = showcase;
                previousCaptureRate = Time.captureFramerate;
                previousTargetRate = Application.targetFrameRate;
                previousBackground = Application.runInBackground;
                Time.captureFramerate = Rate;
                Application.targetFrameRate = Rate;
                Application.runInBackground = true;
                showcase.showControls = false;
                showcase.loop = false;
                showcase.director.output.aspect = Width / (float)Height;
                showcase.Restart();
                ResetRecordedCameraCheck();
                count = Mathf.CeilToInt(showcase.library.Duration * Rate);
                index = 0;
                target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
                target.Create();
                frame = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                pixels = new byte[Width * Height * 3];
                showcase.director.output.targetTexture = target;
                WriteAudio(showcase.library);
                encoder = StartEncoder(executable, "-y -loglevel error -f rawvideo -pixel_format rgb24 " +
                    "-video_size 1920x1080 -framerate 60 -i pipe:0 -vf vflip -an " +
                    "-c:v libx264 -preset fast -crf 19 -pix_fmt yuv420p -movflags +faststart " +
                    Quote(Video + ".video.mp4"), true);
                timer = Stopwatch.StartNew();
                Status("RECORDING 0 / " + count);
                EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
                showcase.CaptureRenderedFrames(Rate, CaptureRenderedFrame);
            }
            catch
            {
                Cleanup();
                throw;
            }
        }

        static bool CaptureRenderedFrame()
        {
            try
            {
                CheckRecordedCamera();
                var previous = RenderTexture.active;
                try
                {
                    RenderTexture.active = target;
                    frame.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
                }
                finally
                {
                    RenderTexture.active = previous;
                }
                frame.GetRawTextureData<byte>().CopyTo(pixels);
                encoder.StandardInput.BaseStream.Write(pixels, 0, pixels.Length);
                if (index == 180)
                    File.WriteAllBytes(PepeShowcaseSetup.Output + "/Preview.png", frame.EncodeToPNG());
                index++;
                if (index % 120 == 0) Status("RECORDING " + index + " / " + count);
                if (index < count) return true;
                Finish();
                PepeShowcasePlayCheck.Finish("PASS completed 60 fps recording from actual rendered Play mode frames.");
                return false;
            }
            catch (Exception exception)
            {
                Status("ERROR\n" + exception);
                UnityEngine.Debug.LogException(exception);
                Cleanup();
                PepeShowcasePlayCheck.Finish("FAIL Play recording: " + exception);
                return false;
            }
        }

        static void Finish()
        {
            encoder.StandardInput.Close();
            if (!encoder.WaitForExit(60000)) throw new TimeoutException("Video encoder did not finish.");
            if (encoder.ExitCode != 0) throw new IOException(encoder.StandardError.ReadToEnd());
            encoder.Dispose();
            encoder = null;
            using (var mux = StartEncoder(FindEncoder(), "-y -loglevel error -i " + Quote(Video + ".video.mp4") +
                " -i " + Quote(PepeShowcaseSetup.Output + "/ShowcaseAudio.wav") +
                " -c:v copy -c:a aac -b:a 192k -shortest -movflags +faststart " + Quote(Video), false))
            {
                if (!mux.WaitForExit(60000)) throw new TimeoutException("Audio mux did not finish.");
                if (mux.ExitCode != 0) throw new IOException(mux.StandardError.ReadToEnd());
            }
            string reportName = recordingBom ? "/BomRecording.txt" : recordingComplete ? "/CompleteRecording.txt" :
                recordingCombo ? "/ComboRecording.txt" : "/Recording.txt";
            File.WriteAllText(PepeShowcaseSetup.Output + reportName,
                "1920x1080, 60 fps, H.264 video, AAC 48 kHz stereo audio.\n" +
                "Frames: " + count + "; duration: " + (count / (float)Rate).ToString("F3") + " s.\n" +
                "One visible Pepe; " + playShowcase.library.takes.Length +
                (recordingComplete ? " distinct connected attacks; no repeats; no receiver/hit reaction.\n" :
                recordingCombo ? " continuous combo; no receiver/hit reaction.\n" :
                    " full attacker clips; no receiver/hit reaction.\n") +
                "Captured after Unity rendering in isolated Play mode; one body/weapon pose per frame.\n" +
                "Render elapsed: " + timer.Elapsed + "\n");
            WriteRecordedCameraReport();
            if (!recordingCombo)
            {
                File.Copy(Video, PepeShowcaseSetup.Output + "/PepeAttackShowcase_Fixed.mp4", true);
                File.Copy(Video, PepeShowcaseSetup.Output + "/PepeAttackShowcase_WeaponVFX.mp4", true);
                File.Copy(Video, PepeShowcaseSetup.Output + "/PepeAttackShowcase_Assassin.mp4", true);
            }
            Cleanup();
            Status("COMPLETE\n" + Path.GetFullPath(Video));
        }

        static Process StartEncoder(string executable, string arguments, bool pipe)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo(executable, arguments)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = pipe,
                    RedirectStandardError = true
                }
            };
            process.Start();
            return process;
        }

        static string Quote(string path) => "\"" + Path.GetFullPath(path) + "\"";

        static string FindEncoder()
        {
            string path = EditorPrefs.GetString("ZeroSpace.Showcase.FFmpeg", "");
            if (string.IsNullOrEmpty(path))
                path = ".git/codex-ko/20261010/ffmpeg/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe";
            if (!File.Exists(path))
                throw new FileNotFoundException("Set EditorPrefs ZeroSpace.Showcase.FFmpeg to an FFmpeg executable.", path);
            return Path.GetFullPath(path);
        }

        static void Status(string message) => File.WriteAllText(PepeShowcaseSetup.Output + "/Status.txt", message);

        static void Cleanup()
        {
            if (encoder != null)
            {
                if (!encoder.HasExited) encoder.Kill();
                encoder.Dispose();
            }
            encoder = null;
            if (playShowcase)
            {
                playShowcase.director.output.targetTexture = null;
                Time.captureFramerate = previousCaptureRate;
                Application.targetFrameRate = previousTargetRate;
                Application.runInBackground = previousBackground;
            }
            playShowcase = null;
            if (target) Object.DestroyImmediate(target);
            if (frame) Object.DestroyImmediate(frame);
            target = null;
            frame = null;
            pixels = null;
            if (reloadLocked)
            {
                reloadLocked = false;
                AssetDatabase.AllowAutoRefresh();
                EditorApplication.UnlockReloadAssemblies();
            }
        }

        internal static void AbortPlayRecording()
        {
            Status("ERROR: Play recording interrupted before completion.");
            Cleanup();
        }
    }
}
