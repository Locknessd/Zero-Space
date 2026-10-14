using System.Collections.Generic;
using UnityEngine;

namespace ZeroSpace.Showcase
{
    public sealed partial class PepeShowcasePresentation
    {
        sealed class AirCut
        {
            public PowerBlade blade;
            public Vector3[] roots;
            public PepeShowcaseLibrary.Cue cue;
            public int take;
            public float step;
            public float start;
            public float end;
            public float takeStart;
        }

        readonly List<AirCut> airCuts = new List<AirCut>();
        readonly HashSet<PepeShowcaseLibrary.Cue> lingeringCues = new HashSet<PepeShowcaseLibrary.Cue>();
        Animator airOwner;
        float airTakeStart;
        public int ActiveAirCutCount { get; private set; }
        public int ActiveLingeringCutCount { get; private set; }

        public bool IsLingeringCueVisible(PepeShowcaseLibrary.Cue cue) => lingeringCues.Contains(cue);

        void PrepareAirCuts(PepeAttackShowcase showcase)
        {
            if (airOwner != showcase.character)
            {
                airCuts.Clear();
                airOwner = showcase.character;
            }
            int take = showcase.TakeIndex;
            airCuts.RemoveAll(cut => cut.take == take);
            airTakeStart = showcase.library.TakeStart(take);
            if (powerLibrary.airCutLifetime <= 0) return;
            foreach (var cue in currentTake.cues)
            {
                if (!cue.swing) continue;
                float start = cue.effectEnd > cue.effectStart ? cue.effectStart : Mathf.Max(0, cue.seconds - .025f);
                float end = cue.effectEnd > cue.effectStart ? cue.effectEnd : cue.seconds + .15f;
                foreach (var blade in powerBlades)
                    airCuts.Add(new AirCut
                    {
                        blade = blade,
                        roots = powerRoots,
                        cue = cue,
                        take = take,
                        step = powerStep,
                        start = start,
                        end = Mathf.Min(end, currentTake.Duration),
                        takeStart = airTakeStart
                    });
            }
        }

        void DrawAirCuts(float seconds)
        {
            ActiveAirCutCount = 0;
            ActiveLingeringCutCount = 0;
            lingeringCues.Clear();
            if (!powerLibrary || !powerLibrary.bomTrailer || powerLibrary.airCutLifetime <= 0) return;
            float absolute = airTakeStart + seconds;
            foreach (var cut in airCuts)
            {
                float local = absolute - cut.takeStart;
                float age = local - cut.end;
                if (local <= cut.start || age >= powerLibrary.airCutLifetime) continue;
                float fade = Mathf.Pow(1 - Mathf.Max(0, age) / powerLibrary.airCutLifetime, 1.5f);
                float to = Mathf.Min(local, cut.end);
                int before = accentIndices.Count;
                AirPoint(cut, cut.start, out var previous, out var previousRoot, out var previousRadial);
                for (float time = cut.start + cut.step; time <= to + cut.step; time += cut.step)
                {
                    float sample = Mathf.Min(time, to);
                    AirPoint(cut, sample, out var point, out var root, out var radial);
                    float travel = Vector3.Distance(previous, point);
                    if (travel > .002f && travel < 1.1f && Vector3.Distance(previousRoot, root) < .22f)
                        AirSpan(previous, point, previousRadial, radial, fade);
                    previous = point;
                    previousRoot = root;
                    previousRadial = radial;
                    if (sample >= to) break;
                }
                if (accentIndices.Count == before) continue;
                ActiveAirCutCount++;
                if (age > .2f)
                {
                    ActiveLingeringCutCount++;
                    lingeringCues.Add(cut.cue);
                }
                DrawAirMotes(cut, to, Mathf.Max(0, age), fade);
            }
        }

        void AirPoint(AirCut cut, float seconds, out Vector3 point, out Vector3 root, out Vector3 radial)
        {
            float sample = Mathf.Clamp(seconds / cut.step, 0, cut.blade.tips.Length - 1);
            int lo = Mathf.Min(Mathf.FloorToInt(sample), cut.blade.tips.Length - 2);
            var bladeBase = Vector3.Lerp(cut.blade.bases[lo], cut.blade.bases[lo + 1], sample - lo);
            var tip = Vector3.Lerp(cut.blade.tips[lo], cut.blade.tips[lo + 1], sample - lo);
            root = Vector3.Lerp(cut.roots[lo], cut.roots[lo + 1], sample - lo);
            point = bladeBase + (tip - bladeBase) * powerLibrary.slashPower * .95f;
            radial = (tip - bladeBase).normalized;
        }
    }
}
