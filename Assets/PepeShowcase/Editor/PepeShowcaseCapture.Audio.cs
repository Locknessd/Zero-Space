using System;
using System.IO;
using UnityEngine;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseCapture
    {
        const int AudioRate = 48000;

        static void WriteAudio(PepeShowcaseLibrary library)
        {
            var mix = new float[Mathf.CeilToInt(library.Duration * AudioRate)];
            if (library.bomTrailer) MixBomScore(mix, library);
            for (int i = 0; i < library.takes.Length; i++)
                foreach (var cue in library.takes[i].cues)
                    foreach (var sound in cue.sounds)
                        Mix(mix, sound, library.TakeStart(i) + library.leadSeconds + cue.seconds);
            float peak = 0;
            foreach (float value in mix) peak = Mathf.Max(peak, Mathf.Abs(value));
            float gain = peak > .95f ? .95f / peak : 1;
            using (var writer = new BinaryWriter(File.Create(PepeShowcaseSetup.Output + "/ShowcaseAudio.wav")))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + mix.Length * 4);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)2);
                writer.Write(AudioRate);
                writer.Write(AudioRate * 4);
                writer.Write((short)4);
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(mix.Length * 4);
                foreach (float value in mix)
                {
                    short sample = (short)Mathf.RoundToInt(Mathf.Clamp(value * gain, -1, 1) * 32767);
                    writer.Write(sample);
                    writer.Write(sample);
                }
            }
        }

        static void Mix(float[] mix, PepeShowcaseLibrary.Sound sound, float time)
        {
            var clip = sound.clip;
            if (!clip) return;
            clip.LoadAudioData();
            var samples = new float[clip.samples * clip.channels];
            if (!clip.GetData(samples, 0)) throw new IOException("Cannot read sound: " + clip.name);
            int offset = Mathf.Clamp(Mathf.RoundToInt(sound.offset * clip.frequency), 0, clip.samples - 1);
            int start = Mathf.RoundToInt(time * AudioRate);
            double step = clip.frequency * sound.pitch / AudioRate;
            int length = (int)Math.Ceiling((clip.samples - offset) / step);
            for (int i = 0; i < length && start + i < mix.Length; i++)
            {
                double position = offset + i * step;
                int a = Math.Min((int)position, clip.samples - 1);
                int b = Math.Min(a + 1, clip.samples - 1);
                float value = 0;
                for (int channel = 0; channel < clip.channels; channel++)
                    value += Mathf.Lerp(samples[a * clip.channels + channel],
                        samples[b * clip.channels + channel], (float)(position - a));
                if (start + i >= 0) mix[start + i] += value / clip.channels * sound.volume;
            }
        }
    }
}
