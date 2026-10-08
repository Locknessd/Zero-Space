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
        const string CCHeader = "sourceSeconds,coarse30Hz,bladeBodyGapM,nearestPivot," +
            "bladeX,bladeY,bladeZ,bodyX,bodyY,bodyZ,tipX,tipY,tipZ,receiverMinimumY,attackerMinimumY," +
            "torsoMinimumY,nonLegSupportMinimumY,torsoFacingUp,supportBone,supportX,supportY,supportZ";

        static IEnumerable<float> CCTimes(float start, float end, int rate)
        {
            yield return start;
            for (int frame = Mathf.FloorToInt(start * rate) + 1; frame / (float)rate < end; frame++)
                yield return frame / (float)rate;
            if (end > start)
                yield return end;
        }

        static CCFrame CCMeasure(FrankBattlePairPlayback pair, SwordRegion sword, CASkin[] skins,
            CASkin[] attackerSkins, CharacterCombat target, CombatExpansionAxeDenseStudy.MovingBodyProbe probe,
            float seconds, bool coarse, List<string> diagnostics, ref Bounds bounds)
        {
            var landing = CALandingFrame(pair, sword, skins, attackerSkins, target, seconds, ref bounds);
            diagnostics.Clear();
            probe.Update(diagnostics);
            var contact = probe.Measure(sword.renderer, sword.vertices, sword.triangles);
            bounds.Encapsulate(sword.renderer.bounds);
            return new CCFrame
            {
                blade = new BladeRecord(seconds, contact, sword.Tip, probe.MinimumY),
                landing = landing,
                coarse = coarse
            };
        }

        static void CCWriteFrame(TextWriter writer, CCFrame frame)
        {
            var blade = frame.blade;
            var row = new StringBuilder(FormattableString.Invariant(
                $"{blade.seconds:R},{frame.coarse},{blade.contact.gap:R},{blade.contact.nearestBone}"));
            CAVector(row, blade.contact.sourcePoint);
            CAVector(row, blade.contact.bodyPoint);
            CAVector(row, blade.tip);
            var landing = frame.landing;
            row.Append(FormattableString.Invariant(
                $",{landing.victimMinimumY:R},{landing.attackerMinimumY:R},{landing.torsoMinimumY:R}"));
            row.Append(FormattableString.Invariant(
                $",{landing.bodySupportMinimumY:R},{landing.torsoFacingUp:R},{landing.supportBone}"));
            CAVector(row, landing.supportWorldPoint);
            writer.WriteLine(row);
        }

        static List<CCWindow> CCDiscover(CCFrame[] frames, float duration)
        {
            var windows = new List<CCWindow>();
            Action<int, string> add = (index, reason) => windows.Add(new CCWindow
            {
                startSeconds = Mathf.Max(0, frames[index].blade.seconds - 2f / 30),
                endSeconds = Mathf.Min(duration, frames[index].blade.seconds + 2f / 30),
                reason = reason
            });
            int closest = Array.IndexOf(frames, frames.OrderBy(f => f.blade.contact.gap).First());
            add(closest, "globalBladeMinimum");
            add(Array.IndexOf(frames, frames.OrderBy(f => f.landing.bodySupportMinimumY).First()),
                "globalNonLegSupportMinimum");
            add(Array.IndexOf(frames, frames.OrderBy(f => f.landing.torsoMinimumY).First()), "globalTorsoMinimum");
            for (int index = 0; index < frames.Length; index++)
            {
                float gap = frames[index].blade.contact.gap;
                if (gap <= CCNear)
                    add(index, "bladeProximity<=0.03m");
                if (index == 0 || index + 1 == frames.Length)
                    continue;
                float previous = frames[index - 1].blade.contact.gap;
                float next = frames[index + 1].blade.contact.gap;
                float dt = frames[index].blade.seconds - frames[index - 1].blade.seconds;
                if (gap < previous && gap <= next)
                    add(index, "localBladeMinimum");
                if (gap <= .15f && (gap - previous) / dt <= -.25f)
                    add(index, "measuredBladeApproach");
                float support = frames[index].landing.bodySupportMinimumY;
                if (support < frames[index - 1].landing.bodySupportMinimumY &&
                    support <= frames[index + 1].landing.bodySupportMinimumY)
                    add(index, "localNonLegSupportMinimum");
                if (support <= .02f && frames[index - 1].landing.bodySupportMinimumY > .02f)
                    add(index, "firstNearGroundSupportCrossing");
            }
            var merged = new List<CCWindow>();
            foreach (var window in windows.OrderBy(w => w.startSeconds))
            {
                var last = merged.LastOrDefault();
                if (last == null || window.startSeconds > last.endSeconds + .00001f)
                    merged.Add(window);
                else
                {
                    last.endSeconds = Mathf.Max(last.endSeconds, window.endSeconds);
                    if (!last.reason.Split(';').Contains(window.reason))
                        last.reason += ";" + window.reason;
                }
            }
            return merged;
        }

        static List<CCInterval> CCIntervals(SortedDictionary<float, CCFrame> samples, List<CCWindow> windows)
        {
            var frames = samples.Values.ToArray();
            var intervals = new List<CCInterval>();
            for (int index = 0; index < frames.Length; index++)
            {
                if (frames[index].blade.contact.gap > CCNear)
                    continue;
                int start = index;
                while (index + 1 < frames.Length && frames[index + 1].blade.contact.gap <= CCNear)
                    index++;
                intervals.Add(CCIntervalFor(frames, start, index, "Proximity candidate; not an approved contact."));
            }
            // Retain closest missed approaches too; these must not disappear from the evidence.
            foreach (var window in windows.Where(w => w.reason.Contains("Blade")))
            {
                int start = Array.FindIndex(frames, f => f.blade.seconds >= window.startSeconds);
                int end = Array.FindLastIndex(frames, f => f.blade.seconds <= window.endSeconds);
                if (start < 0 || end < start || frames.Skip(start).Take(end - start + 1)
                    .Any(f => f.blade.contact.gap <= CCNear))
                    continue;
                intervals.Add(CCIntervalFor(frames, start, end,
                    "Measured minimum/approach miss above 0.03m; no contact inferred."));
            }
            return intervals.OrderBy(i => i.startSeconds).ToList();
        }

        static CCInterval CCIntervalFor(CCFrame[] frames, int start, int end, string assessment)
        {
            var rows = frames.Skip(start).Take(end - start + 1).ToArray();
            var best = rows.OrderBy(f => f.blade.contact.gap).ThenBy(f => f.blade.seconds).First();
            float time = best.blade.seconds;
            float entry = rows[0].blade.seconds;
            float exit = rows.Last().blade.seconds;
            var before = frames.LastOrDefault(f => f.blade.seconds <= entry - 1f / 30) ?? frames[0];
            var after = frames.FirstOrDefault(f => f.blade.seconds >= exit + 1f / 30) ?? frames.Last();
            var touch = rows.Where(f => f.blade.contact.gap <= CATie).ToArray();
            return new CCInterval
            {
                startSeconds = rows[0].blade.seconds,
                endSeconds = rows.Last().blade.seconds,
                representativeSeconds = time,
                minimumGapM = best.blade.contact.gap,
                firstTouchIntersectionSeconds = touch.Length == 0 ? -1 : touch[0].blade.seconds,
                lastTouchIntersectionSeconds = touch.Length == 0 ? -1 : touch.Last().blade.seconds,
                beforeSeconds = before.blade.seconds,
                afterSeconds = after.blade.seconds,
                approachGapRateMps = entry > before.blade.seconds
                    ? (rows[0].blade.contact.gap - before.blade.contact.gap) / (entry - before.blade.seconds) : 0,
                recoilGapRateMps = after.blade.seconds > exit
                    ? (after.blade.contact.gap - rows.Last().blade.contact.gap) / (after.blade.seconds - exit) : 0,
                minimumTiedSamples = rows.Count(f => f.blade.contact.gap <= best.blade.contact.gap + CATie),
                assessment = assessment + " Negative gap slope approaches; positive recedes; zero may be a " +
                    "flat tie or unavailable endpoint derivative (inspect before/after times). " +
                    "Rates bracket interval entry/exit, not the arbitrary first tied minimum. " +
                    "Touch/intersection timestamps use 0.00001m numerical tolerance."
            };
        }

        static CAContact CCContact(FrankBattlePairPlayback pair, SwordRegion sword, CASkin[] skins,
            CASkin[] attackerSkins, CharacterCombat target, float seconds)
        {
            var contact = CAMeasureContact(pair, sword, skins, attackerSkins, target, seconds);
            // Correct the inherited central difference at clip boundaries without changing the shared helper.
            float before = Mathf.Max(0, seconds - CADelta);
            float after = Mathf.Min(pair.Duration, seconds + CADelta);
            var anchor = target.Animator.GetBoneTransform((HumanBodyBones)Enum.Parse(
                typeof(HumanBodyBones), contact.chosenBone));
            var rotation = target.Animator.transform.rotation;
            pair.EvaluateAt(before);
            var bladeBefore = sword.renderer.transform.TransformPoint(contact.sourceBladeLocalPoint);
            var targetBefore = anchor.TransformPoint(contact.targetBoneLocalPoint);
            pair.EvaluateAt(after);
            contact.bladeVelocityWorldMps =
                (sword.renderer.transform.TransformPoint(contact.sourceBladeLocalPoint) - bladeBefore) /
                (after - before);
            contact.targetAnchorVelocityWorldMps =
                (anchor.TransformPoint(contact.targetBoneLocalPoint) - targetBefore) / (after - before);
            contact.bladeVelocityVictimMps = Quaternion.Inverse(rotation) * contact.bladeVelocityWorldMps;
            contact.relativeBladeVelocityVictimMps = Quaternion.Inverse(rotation) *
                (contact.bladeVelocityWorldMps - contact.targetAnchorVelocityWorldMps);
            contact.assessment += FormattableString.Invariant(
                $" Derivative endpoints in source seconds: [{before:R},{after:R}]; denominator={after - before:R}s.");
            pair.EvaluateAt(seconds);
            return contact;
        }
    }
}
