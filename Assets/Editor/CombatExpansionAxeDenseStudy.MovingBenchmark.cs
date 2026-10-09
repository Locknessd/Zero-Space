using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionAxeDenseStudy
    {
        public sealed class MovingKernelSample
        {
            public double referenceUpdateMs;
            public double optimizedUpdateMs;
            public double referenceMeasureMs;
            public double optimizedMeasureMs;
            public long referenceBytes;
            public long optimizedBytes;
            public int vertices;
            public int triangles;
        }

        public sealed partial class MovingBodyProbe
        {
            // Invoke only after evaluating the animation snapshot. The reference Receiver/Build/Measure
            // implementation is unchanged; both versions consume the same current skin and source transform.
            public MovingKernelSample VerifyKernelSnapshot(Renderer renderer, Vector3[] localVertices,
                int[] triangles, bool optimizedFirst)
            {
                var sample = new MovingKernelSample();
                var referenceDiagnostics = new List<string>();
                var optimizedDiagnostics = new List<string>();
                Surface reference = null;
                BodyContact referenceContact = default;
                BodyContact optimizedContact = default;
                Action referenceRun = () =>
                {
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    var timer = Stopwatch.StartNew();
                    reference = Receiver(target, referenceDiagnostics, skins);
                    sample.referenceUpdateMs = timer.Elapsed.TotalMilliseconds;
                    var saved = body;
                    try
                    {
                        body = reference;
                        timer.Restart();
                        referenceContact = Measure(renderer, localVertices, triangles);
                        sample.referenceMeasureMs = timer.Elapsed.TotalMilliseconds;
                    }
                    finally
                    {
                        body = saved;
                    }
                    sample.referenceBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                };
                Action optimizedRun = () =>
                {
                    long allocated = GC.GetAllocatedBytesForCurrentThread();
                    var timer = Stopwatch.StartNew();
                    Update(optimizedDiagnostics);
                    sample.optimizedUpdateMs = timer.Elapsed.TotalMilliseconds;
                    timer.Restart();
                    optimizedContact = Measure(renderer, localVertices, triangles);
                    sample.optimizedMeasureMs = timer.Elapsed.TotalMilliseconds;
                    sample.optimizedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                };
                if (optimizedFirst)
                {
                    optimizedRun();
                    referenceRun();
                }
                else
                {
                    referenceRun();
                    optimizedRun();
                }
                body.AssertMovingEquivalent(reference);
                if (!referenceDiagnostics.SequenceEqual(optimizedDiagnostics) ||
                    !SameFloat(referenceContact.gap, optimizedContact.gap) ||
                    !SameVector(referenceContact.sourcePoint, optimizedContact.sourcePoint) ||
                    !SameVector(referenceContact.bodyPoint, optimizedContact.bodyPoint) ||
                    !SameVector(referenceContact.boneOffset, optimizedContact.boneOffset) ||
                    referenceContact.nearestBone != optimizedContact.nearestBone)
                    throw new InvalidOperationException("Moving contact kernel differs from the untouched reference.");
                sample.vertices = body.vertices.Count;
                sample.triangles = body.triangles.Count / 3;
                return sample;
            }
        }

        static bool SameFloat(float a, float b) =>
            BitConverter.SingleToInt32Bits(a) == BitConverter.SingleToInt32Bits(b);

        static bool SameVector(Vector3 a, Vector3 b) =>
            SameFloat(a.x, b.x) && SameFloat(a.y, b.y) && SameFloat(a.z, b.z);

        sealed partial class Surface
        {
            public void AssertMovingEquivalent(Surface reference)
            {
                if (vertices.Count != reference.vertices.Count || !triangles.SequenceEqual(reference.triangles) ||
                    !order.SequenceEqual(reference.order) || nodes.Count != reference.nodes.Count)
                    throw new InvalidOperationException("Moving BVH topology differs from the reference.");
                for (int i = 0; i < vertices.Count; i++)
                    if (!SameVector(vertices[i], reference.vertices[i]))
                        throw new InvalidOperationException("Moving vertex differs from the reference: " + i);
                for (int i = 0; i < nodes.Count; i++)
                {
                    var a = nodes[i];
                    var b = reference.nodes[i];
                    if (!SameVector(a.bounds.center, b.bounds.center) ||
                        !SameVector(a.bounds.extents, b.bounds.extents) || a.start != b.start || a.count != b.count ||
                        a.left != b.left || a.right != b.right)
                        throw new InvalidOperationException("Moving BVH node differs from the reference: " + i);
                }
            }
        }
    }
}
