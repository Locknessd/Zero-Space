using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcaseCamera
    {
        void CacheReferenceShot(PepeAttackShowcase showcase, Quaternion[] rotations, float[] fits, float step)
        {
            if (!UsesReference(showcase) || referenceShots == null) return;
            int index = showcase.TakeIndex;
            if (ReferenceReady)
            {
                focusTrack = referenceShots[index].focus;
                return;
            }
            var take = showcase.library.takes[index];
            var requested = new float[fits.Length];
            for (int i = 0; i < fits.Length; i++)
            {
                if (take.lockCamera)
                {
                    requested[i] = Mathf.Max(9.6f, distance);
                    continue;
                }
                float progress = i / (float)(fits.Length - 1);
                // Dolly toward the anticipation, leaving extra room at the attack's release.
                float push = .28f * Mathf.Exp(-Mathf.Pow((progress - .18f) / .17f, 2));
                float release = .25f * Mathf.Exp(-Mathf.Pow((progress - .65f) / .2f, 2));
                requested[i] = Mathf.Max(referenceDistance - push + release, fits[i] * 1.04f);
            }
            var distances = take.lockCamera ? requested : SmoothReferenceDistances(requested, step);
            referenceShots[index] = new ReferenceShot
            {
                focus = focusTrack,
                rotation = rotations,
                distance = distances,
                step = step
            };
        }

        static float[] SmoothReferenceDistances(float[] requested, float step)
        {
            int radius = Mathf.Max(1, Mathf.CeilToInt(.18f / step));
            var envelope = new float[requested.Length];
            for (int i = 0; i < envelope.Length; i++)
                for (int j = Mathf.Max(0, i - radius); j <= Mathf.Min(envelope.Length - 1, i + radius); j++)
                    envelope[i] = Mathf.Max(envelope[i], requested[j]);
            var result = new float[requested.Length];
            for (int i = 0; i < result.Length; i++)
            {
                float total = 0;
                for (int j = Mathf.Max(0, i - radius); j <= Mathf.Min(result.Length - 1, i + radius); j++)
                {
                    float weight = Mathf.Exp(-.5f * Mathf.Pow((j - i) * step / .1f, 2));
                    result[i] += envelope[j] * weight;
                    total += weight;
                }
                // Every window in the average contains this sample, so fitting stays conservative.
                result[i] /= total;
            }
            return result;
        }

        void FinishReferenceShots(PepeAttackShowcase showcase)
        {
            if (!UsesReference(showcase)) return;
            for (int i = 1; i < referenceShots.Length; i++)
            {
                var previous = referenceShots[i - 1];
                var next = referenceShots[i];
                float seam = Mathf.Max(previous.distance[previous.distance.Length - 1], next.distance[0]);
                BlendReferenceDistanceEdge(previous, seam, true);
                BlendReferenceDistanceEdge(next, seam, false);
            }
            ReferenceNearest = float.MaxValue;
            ReferenceFarthest = 0;
            foreach (var shot in referenceShots)
                foreach (float value in shot.distance)
                {
                    ReferenceNearest = Mathf.Min(ReferenceNearest, value);
                    ReferenceFarthest = Mathf.Max(ReferenceFarthest, value);
                }
            ReferenceReady = true;
        }

        static void BlendReferenceDistanceEdge(ReferenceShot shot, float seam, bool end)
        {
            for (int i = 0; i < shot.distance.Length; i++)
            {
                float seconds = (end ? shot.distance.Length - 1 - i : i) * shot.step;
                float weight = SmoothReference(1 - seconds / .65f);
                // Seam matching may expand a frame but must never invalidate its fitted geometry.
                shot.distance[i] = Mathf.Max(shot.distance[i], Mathf.Lerp(shot.distance[i], seam, weight));
            }
        }
    }
}
