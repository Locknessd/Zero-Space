using System;
using System.Collections.Generic;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static CAContact CAAnchorFixture(Vector3 world, Matrix4x4 hips)
        {
            return new CAContact
            {
                targetHipsLocalPoint = hips.inverse.MultiplyPoint3x4(world),
                tiedTriangles = new List<CATriangle>
                {
                    new CATriangle { targetWorldPoint = world }
                }
            };
        }

        static void CAAnchorAssert(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException("Anchor units fixture failed: " + label);
        }

        static void CAAnchorReject(Action action, string label, List<string> results)
        {
            try
            {
                action();
            }
            catch (InvalidOperationException)
            {
                results.Add("PASS rejected " + label);
                return;
            }
            throw new InvalidOperationException("Anchor units fixture accepted invalid evidence: " + label);
        }

        static void CAValidateAnchorFixtures(List<string> results)
        {
            var rotation = Quaternion.Euler(31, 73, -19);
            var scales = new[] { Vector3.one * .1f, Vector3.one, Vector3.one * 10,
                new Vector3(.1f, 2, 10) };
            foreach (var scale in scales)
            foreach (var orientation in new[] { Quaternion.identity, rotation })
            foreach (var axis in new[] { Vector3.right, Vector3.up, Vector3.forward })
            foreach (float distance in new[] { .0002f, .002f })
            {
                var hips = Matrix4x4.TRS(new Vector3(.23f, -.17f, .31f), orientation, scale);
                var world = hips.MultiplyPoint3x4(new Vector3(.17f, -.08f, .11f));
                var shifted = world + orientation * axis * distance;
                var baseline = CAAnchorFixture(world, hips);
                var repeated = CAAnchorFixture(shifted, hips);
                float actual = CAContactAnchorWorldError(baseline, repeated);
                float rawLocal = Vector3.Distance(baseline.targetHipsLocalPoint, repeated.targetHipsLocalPoint);
                CAAnchorAssert(Mathf.Abs(actual - distance) <= .000001f, "physical world displacement");
                CAAnchorAssert((actual <= .001f) == (distance == .0002f), "unchanged 1mm gate");
                float axisScale = Vector3.Dot(scale, axis);
                CAAnchorAssert(Mathf.Abs(rawLocal - distance / axisScale) <= .000002f,
                    "inverse transform produces scale sensitive raw local distance");
                if (axisScale == .1f && distance == .0002f)
                    CAAnchorAssert(rawLocal > .001f, "legacy false rejection at scale 0.1");
                if (axisScale == 10 && distance == .002f)
                    CAAnchorAssert(rawLocal <= .001f, "legacy false acceptance at scale 10");
                results.Add(FormattableString.Invariant(
                    $"PASS scale={scale:R} rotation={orientation:R} axis={axis:R} ") +
                    FormattableString.Invariant(
                        $"expectedWorldM={distance:R} actualWorldM={actual:R} rawLocalDistance={rawLocal:R}"));
            }

            // Each snapshot owns its world position, even if the bone transform changed between queries.
            var firstHips = Matrix4x4.TRS(Vector3.one, rotation, new Vector3(.1f, 2, 10));
            var laterHips = Matrix4x4.TRS(Vector3.left, Quaternion.Euler(-12, 40, 21),
                new Vector3(3, .2f, .4f));
            var point = new Vector3(.2f, .4f, -.3f);
            var first = CAAnchorFixture(point, firstHips);
            var later = CAAnchorFixture(point, laterHips);
            CAAnchorAssert(CAContactAnchorWorldError(first, later) == 0, "snapshot transform independence");
            CAAnchorAssert(Vector3.Distance(first.targetHipsLocalPoint, later.targetHipsLocalPoint) > .001f,
                "same world point can have different attachment coordinates");
            results.Add("PASS same world anchor under different rotated nonuniform snapshot transforms");

            // Later tied triangles cannot replace the selected representative or create a best matching pair.
            var selectedA = CAAnchorFixture(Vector3.zero, Matrix4x4.identity);
            var selectedB = CAAnchorFixture(Vector3.right * .002f, Matrix4x4.identity);
            selectedA.tiedTriangles.Add(new CATriangle { targetWorldPoint = Vector3.right * .002f });
            selectedB.tiedTriangles.Add(new CATriangle { targetWorldPoint = Vector3.zero });
            CAAnchorAssert(CAContactAnchorWorldError(selectedA, selectedB) > .001f,
                "selected triangle zero is authoritative despite closer alternative ties");
            results.Add("PASS selected triangle zero remains authoritative");

            var valid = CAAnchorFixture(Vector3.zero, Matrix4x4.identity);
            var invalidContacts = new[]
            {
                null,
                new CAContact { tiedTriangles = null },
                new CAContact(),
                new CAContact { tiedTriangles = new List<CATriangle> { null, new CATriangle() } }
            };
            for (int index = 0; index < invalidContacts.Length; index++)
            {
                var invalid = invalidContacts[index];
                CAAnchorReject(() => CAContactAnchorWorldError(invalid, valid),
                    "missing baseline evidence " + index, results);
                CAAnchorReject(() => CAContactAnchorWorldError(valid, invalid),
                    "missing repeated evidence " + index, results);
            }
            foreach (float nonfinite in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            for (int axis = 0; axis < 3; axis++)
            {
                var badPoint = Vector3.zero;
                badPoint[axis] = nonfinite;
                var invalid = new CAContact
                {
                    tiedTriangles = new List<CATriangle> { new CATriangle { targetWorldPoint = badPoint } }
                };
                CAAnchorReject(() => CAContactAnchorWorldError(invalid, valid),
                    "nonfinite baseline axis " + axis + " value " + nonfinite, results);
                CAAnchorReject(() => CAContactAnchorWorldError(valid, invalid),
                    "nonfinite repeated axis " + axis + " value " + nonfinite, results);
            }
            var huge = CAAnchorFixture(Vector3.right * float.MaxValue, Matrix4x4.identity);
            var opposite = CAAnchorFixture(Vector3.left * float.MaxValue, Matrix4x4.identity);
            // MaxValue from zero is representable with widened intermediate arithmetic.
            // Opposite finite endpoints have a separation beyond the float return range.
            CAAnchorReject(() => CAContactAnchorWorldError(opposite, huge), "overflowed world distance", results);
        }
    }
}
