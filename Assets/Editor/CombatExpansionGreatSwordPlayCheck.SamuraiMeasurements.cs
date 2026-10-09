using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionGreatSwordPlayCheck
    {
        static readonly List<double> samuraiFrameMilliseconds = new List<double>();
        static long samuraiAllocatedBefore, samuraiManagedBefore;
        static int samuraiObjectsBefore, samuraiActorsPeak, samuraiVoicesBefore, samuraiEffectsBefore;
        static double samuraiFrameAt;
        static bool? samuraiAllocationCounterAvailable;

        static void BeginSamuraiMeasurements()
        {
            if (!samuraiAllocationCounterAvailable.HasValue)
            {
                long beforeProbe = GC.GetAllocatedBytesForCurrentThread();
                var probe = new byte[4096];
                samuraiAllocationCounterAvailable = GC.GetAllocatedBytesForCurrentThread() > beforeProbe;
                GC.KeepAlive(probe);
            }
            samuraiFrameMilliseconds.Clear();
            samuraiActorsPeak = nativeBefore;
            samuraiObjectsBefore = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length;
            samuraiVoicesBefore = game.battleSfx.PooledVoiceCount;
            samuraiEffectsBefore = game.battleVfx.PooledEffectCount;
            samuraiManagedBefore = Profiler.GetMonoUsedSizeLong();
            samuraiAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            samuraiFrameAt = Now;
        }

        static void MeasureSamuraiFrame()
        {
            if (!samuraiSuite || !activeCase)
                return;
            samuraiFrameMilliseconds.Add((Now - samuraiFrameAt) * 1000);
            samuraiFrameAt = Now;
            samuraiActorsPeak = Math.Max(samuraiActorsPeak,
                Object.FindObjectsByType<FrankTestActor>(FindObjectsInactive.Include).Length);
        }

        static void ReportSamuraiMeasurements()
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread() - samuraiAllocatedBefore;
            string allocation = samuraiAllocationCounterAvailable == true
                ? allocated.ToString() : "unavailable_counter_did_not_observe_probe_allocation";
            long managed = Profiler.GetMonoUsedSizeLong() - samuraiManagedBefore;
            int objects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include).Length;
            int actors = Object.FindObjectsByType<FrankTestActor>(FindObjectsInactive.Include).Length;
            var frames = samuraiFrameMilliseconds.OrderBy(value => value).ToArray();
            double median = frames.Length == 0 ? 0 : frames[(frames.Length - 1) / 2];
            double p95 = frames.Length == 0 ? 0 : frames[(int)((frames.Length - 1) * .95)];
            report.AppendLine($"MEASURE case={step + 1} play={samuraiRepeat + 1} frames={frames.Length} " +
                $"wallFrameMedianMs={median:F3} p95Ms={p95:F3} maxMs={frames.DefaultIfEmpty(0).Max():F3} " +
                $"mainThreadAllocatedBytes={allocation} managedHeapDeltaBytes={managed} " +
                $"objects={samuraiObjectsBefore}->{objects} " +
                $"nativeActors={nativeBefore}/{samuraiActorsPeak}/{actors} " +
                $"pooledVoices={samuraiVoicesBefore}->{game.battleSfx.PooledVoiceCount} " +
                $"pooledEffects={samuraiEffectsBefore}->{game.battleVfx.PooledEffectCount}");
            report.AppendLine("MEASURE scope=Editor main thread including harness, screenshots, logging, " +
                "queue movement, " +
                "pause and settle frames; heap delta is not allocation; pooled objects may be retained. " +
                "Small repeated samples are raw evidence, not a performance target verdict " +
                "or standalone build benchmark.");
        }
    }
}
