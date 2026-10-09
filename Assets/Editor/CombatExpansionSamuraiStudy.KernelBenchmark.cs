using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        const string KernelBenchmarkOutput = "Library/CombatExpansionTools/contact-kernel-benchmark";

        public static void BenchmarkMovingContactKernel()
        {
            CombatExpansionHumanoidStudy.RequireEditor();
            Directory.CreateDirectory(KernelBenchmarkOutput);
            string status = KernelBenchmarkOutput + "/Status.txt";
            File.WriteAllText(status, "RUNNING; no equivalence or speedup claim until PASS.\n");
            var rows = new StringBuilder("target,direction,seek,seconds,variant,optimizedFirst,vertices,triangles," +
                "referenceUpdateMs,optimizedUpdateMs,referenceMeasureMs,optimizedMeasureMs," +
                "referenceBytes,optimizedBytes\n");
            var notes = new List<string>
            {
                "Execution02 native source pair; both actual rigs, both lane directions, forward/backward seeks.",
                "Reference Receiver/Build and shared unchanged Measure use the same evaluated snapshot.",
                "Bitwise equality required for every world vertex, BVH order/node bounds and contact field.",
                "Topology, diagnostics and anatomical pivot must match exactly; no tolerance or gate changes.",
                "The first two snapshots of each pair include buffer warmup; later snapshots reuse both buffers.",
                "Timing excludes equality assertions and pair evaluation; allocation totals are managed-thread only.",
                "Run this benchmark after staging installation and after the active collection has finished."
            };
            try
            {
                using var session = new SourceSession();
                InitializePairStudy(session.Fighters);
                foreach (var source in session.Fighters)
                    foreach (int direction in new[] { 1, -1 })
                        BenchmarkKernelPair(session.Fighters, source, direction, rows, notes);
                File.WriteAllText(KernelBenchmarkOutput + "/Samples.csv", rows.ToString());
                File.WriteAllLines(KernelBenchmarkOutput + "/Scope.txt", notes);
                File.WriteAllText(status, "PASS exact moving-kernel equivalence on all benchmark snapshots.\n" +
                    "Inspect per-sample timings; this is not a full-capture throughput measurement.\n");
            }
            catch (Exception error)
            {
                File.WriteAllText(KernelBenchmarkOutput + "/Samples.csv", rows.ToString());
                File.WriteAllLines(KernelBenchmarkOutput + "/Scope.txt", notes);
                File.WriteAllText(status, "FAILED; partial results are not acceptance.\n" + error);
                throw;
            }
        }

        static void BenchmarkKernelPair(CharacterCombat[] fighters, CharacterCombat source, int direction,
            StringBuilder rows, List<string> notes)
        {
            var target = fighters.Single(fighter => fighter != source);
            var pair = BeginBladeStudy(fighters, source, target, 2, direction);
            try
            {
                pair.EvaluateAt(0);
                CheckPairWeapons(pair, pair.AttackerActor.Pose.weaponRenderers);
                using var sword = new SwordRegion(pair);
                var probe = new CombatExpansionAxeDenseStudy.MovingBodyProbe(target);
                float[] times = new[] { 0, .5f, 1f, 2f, pair.Duration }
                    .Select(time => Mathf.Min(time, pair.Duration)).Distinct().OrderBy(time => time).ToArray();
                int sampleIndex = 0;
                foreach (bool backward in new[] { false, true })
                {
                    foreach (float time in backward ? times.Reverse() : times)
                    {
                        pair.EvaluateAt(time);
                        AppendKernelSample(probe, sword, target, direction, backward ? "backward" : "forward",
                            time, "animated", sampleIndex++ % 2 == 0, rows);
                    }
                }
                pair.EvaluateAt(Mathf.Min(1f, pair.Duration));
                BenchmarkKernelBlendShapes(probe, sword, target, direction,
                    Mathf.Min(1f, pair.Duration), rows, notes, ref sampleIndex);
            }
            finally
            {
                pair.Cancel();
            }
            File.WriteAllText(KernelBenchmarkOutput + "/Samples.csv", rows.ToString());
        }

        static void BenchmarkKernelBlendShapes(CombatExpansionAxeDenseStudy.MovingBodyProbe probe,
            SwordRegion sword, CharacterCombat target, int direction, float time, StringBuilder rows,
            List<string> notes, ref int sampleIndex)
        {
            var skins = target.Animator.GetComponentsInChildren<SkinnedMeshRenderer>()
                .Where(skin => skin && skin.enabled && skin.gameObject.activeInHierarchy && skin.sharedMesh &&
                    skin.sharedMesh.vertexCount >= 1000 && skin.sharedMesh.blendShapeCount > 0).ToArray();
            notes.Add(target.name + " direction=" + direction + " visible body skins with blend shapes=" + skins.Length);
            foreach (var skin in skins)
            {
                float[] original = Enumerable.Range(0, skin.sharedMesh.blendShapeCount)
                    .Select(skin.GetBlendShapeWeight).ToArray();
                try
                {
                    foreach (float weight in new[] { -25f, 0f, 50f, 125f })
                    {
                        for (int shape = 0; shape < original.Length; shape++)
                            skin.SetBlendShapeWeight(shape, weight);
                        AppendKernelSample(probe, sword, target, direction, "fixed", time,
                            "blend_" + skin.name.Replace(',', '_') + "_" + weight,
                            sampleIndex++ % 2 == 0, rows);
                    }
                }
                finally
                {
                    for (int shape = 0; shape < original.Length; shape++)
                        skin.SetBlendShapeWeight(shape, original[shape]);
                }
            }
        }

        static void AppendKernelSample(CombatExpansionAxeDenseStudy.MovingBodyProbe probe, SwordRegion sword,
            CharacterCombat target, int direction, string seek, float time, string variant, bool optimizedFirst,
            StringBuilder rows)
        {
            var result = probe.VerifyKernelSnapshot(sword.renderer, sword.vertices, sword.triangles, optimizedFirst);
            rows.Append(FormattableString.Invariant(
                $"{target.name},{direction},{seek},{time:R},{variant},{optimizedFirst},"));
            rows.Append(FormattableString.Invariant(
                $"{result.vertices},{result.triangles},{result.referenceUpdateMs:R},{result.optimizedUpdateMs:R},"));
            rows.Append(FormattableString.Invariant(
                $"{result.referenceMeasureMs:R},{result.optimizedMeasureMs:R},"));
            rows.AppendLine(FormattableString.Invariant($"{result.referenceBytes},{result.optimizedBytes}"));
        }
    }
}
