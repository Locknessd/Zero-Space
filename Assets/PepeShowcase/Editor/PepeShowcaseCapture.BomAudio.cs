using System;
using UnityEngine;
using ZeroSpace.Showcase;

namespace FrankRetarget.Editor
{
    public static partial class PepeShowcaseCapture
    {
        static void MixBomScore(float[] mix, PepeShowcaseLibrary library)
        {
            uint noise = 137;
            float beatLength = 60f / 112;
            double[] notes = { 110, 130.8128, 146.8324, 97.9989 };
            for (int i = 0; i < mix.Length; i++)
            {
                double time = i / (double)AudioRate;
                int beat = (int)(time / beatLength);
                double local = time - beat * beatLength;
                double note = notes[(beat / 4) % notes.Length];
                double pluck = Math.Sin(time * note * Math.PI * 2) * Math.Exp(-local * 8) * .025;
                double pad = (Math.Sin(time * 55 * Math.PI * 2) +
                    Math.Sin(time * 82.4069 * Math.PI * 2)) * .008;
                double kick = Math.Sin(local * 48 * Math.PI * 2 - 7 * Math.Exp(-local * 30)) *
                    Math.Exp(-local * 22) * .055;
                noise ^= noise << 13;
                noise ^= noise >> 17;
                noise ^= noise << 5;
                double white = (noise / (double)uint.MaxValue * 2 - 1);
                double hatTime = local % (beatLength / 2);
                double hat = white * Math.Exp(-hatTime * 95) * .01;
                float fade = Mathf.Min(Mathf.Clamp01((float)time / .7f),
                    Mathf.Clamp01((library.Duration - (float)time) / 1.5f));
                float drums = time >= 4 ? 1 : .3f;
                mix[i] += (float)(pluck + pad + (kick + hat) * drums) * fade;
            }
            BomStinger(mix, .45f, .13f);
            BomStinger(mix, BomTrailerIntro.FreezeStart, .15f);
            BomStinger(mix, library.introSeconds, .12f);
            for (int index = 0; index < library.takes.Length; index++)
                foreach (var cue in library.takes[index].cues)
                    if (cue.swing) BomStinger(mix, library.TakeStart(index) + cue.seconds, .055f);
        }

        static void BomStinger(float[] mix, float seconds, float volume)
        {
            int start = Mathf.RoundToInt(seconds * AudioRate);
            for (int i = 0; i < AudioRate * .4f && start + i < mix.Length; i++)
            {
                double time = i / (double)AudioRate;
                double bass = Math.Sin(time * 62 * Math.PI * 2 - 4 * Math.Exp(-time * 30));
                double air = Math.Sin(time * 1291 * Math.PI * 2) * Math.Sin(time * 2137 * Math.PI * 2);
                mix[start + i] += (float)((bass * Math.Exp(-time * 15) +
                    air * .3 * Math.Exp(-time * 35)) * volume);
            }
        }
    }
}
