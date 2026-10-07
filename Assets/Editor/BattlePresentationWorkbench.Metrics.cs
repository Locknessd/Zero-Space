using System;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;

public static partial class BattlePresentationWorkbench
{
    sealed class ReviewCounter
    {
        public string name;
        public ProfilerRecorder recorder;
        public long peak;
        public double sum;
        public int count;
    }

    static ReviewCounter[] counters;
    static ParticleSystem[] reviewParticles;
    static int peakParticles;

    static void BeginMetrics()
    {
        StopMetrics();
        counters = new[]
        {
            Counter(ProfilerCategory.Memory, "GC Allocated In Frame"),
            Counter(ProfilerCategory.Render, "Draw Calls Count"),
            Counter(ProfilerCategory.Render, "SetPass Calls Count"),
            Counter(ProfilerCategory.Render, "Triangles Count"),
            Counter(ProfilerCategory.Internal, "Main Thread"),
            Counter(ProfilerCategory.Render, "GPU Frame Time")
        };
        reviewParticles = Resources.FindObjectsOfTypeAll<ParticleSystem>()
            .Where(p => p.gameObject.scene == reviewGame.gameObject.scene).ToArray();
        peakParticles = 0;
    }

    static ReviewCounter Counter(ProfilerCategory category, string name)
    {
        return new ReviewCounter { name = name, recorder = ProfilerRecorder.StartNew(category, name, 1) };
    }

    static void SampleMetrics()
    {
        foreach (var counter in counters)
        {
            if (!counter.recorder.Valid || counter.recorder.Count == 0)
                continue;
            long value = counter.recorder.LastValue;
            counter.peak = Math.Max(counter.peak, value);
            counter.sum += value;
            counter.count++;
        }
        int active = 0;
        foreach (var particles in reviewParticles)
            if (particles && particles.gameObject.activeInHierarchy)
                active += particles.particleCount;
        peakParticles = Mathf.Max(peakParticles, active);
    }

    static void WriteMetrics(StringBuilder report)
    {
        foreach (var counter in counters)
        {
            if (!counter.recorder.Valid || counter.count == 0)
            {
                report.AppendLine(counter.name + ": unavailable in this Editor recorder");
                continue;
            }
            report.AppendLine($"{counter.name}: average={counter.sum / counter.count:F2}; " +
                $"peak={counter.peak}; unit={counter.recorder.UnitType}");
        }
        report.AppendLine($"Peak particles in the prewarmed battle pools={peakParticles}");
        report.AppendLine("Recorder counters include Editor work. Particle count covers cached battle pools.");
    }

    static void StopMetrics()
    {
        if (counters == null)
            return;
        foreach (var counter in counters)
            counter.recorder.Dispose();
        counters = null;
    }
}
