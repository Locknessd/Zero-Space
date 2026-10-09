using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionSamuraiStudy
    {
        static void CCVerifyAndRender(FrankBattlePairPlayback pair, SwordRegion sword, CASkin[] skins,
            CASkin[] attackerSkins, CharacterCombat source, CharacterCombat target, CharacterCombat[] fighters,
            CombatExpansionAxeDenseStudy.MovingBodyProbe probe, SortedDictionary<float, CCFrame> frames,
            CCCase record, Bounds bounds, string output, string stem)
        {
            var times = new List<float> { 0, pair.Duration, record.minimumSupportFrame.seconds,
                record.minimumTorsoFrame.seconds };
            foreach (var interval in record.candidateIntervals)
                times.AddRange(new[] { interval.beforeSeconds, interval.startSeconds,
                    interval.representativeSeconds, interval.endSeconds, interval.afterSeconds });
            foreach (float fraction in new[] { .25f, .5f, .75f })
                times.Add(frames.Keys.OrderBy(t => Mathf.Abs(t - pair.Duration * fraction)).First());
            if (record.anatomy.firstSupportNearGroundSeconds >= 0)
                times.Add(record.anatomy.firstSupportNearGroundSeconds);
            record.anatomy.imageTimes = times.Distinct().OrderBy(t => t).ToArray();
            var diagnostics = new List<string>();
            foreach (float time in record.anatomy.imageTimes.Reverse())
            {
                pair.EvaluateAt(pair.Duration);
                var repeated = CCMeasure(pair, sword, skins, attackerSkins, target, probe,
                    time, false, diagnostics, ref bounds);
                var baseline = frames[time];
                var seek = CACompareSeek(baseline.landing, repeated.landing);
                seek.contactGapErrorM = Mathf.Abs(baseline.blade.contact.gap - repeated.blade.contact.gap);
                seek.bladeTipErrorM = Mathf.Max(seek.bladeTipErrorM,
                    Vector3.Distance(baseline.blade.tip, repeated.blade.tip));
                var original = record.anatomy.contacts.FirstOrDefault(c => c.seconds == time);
                if (original != null)
                {
                    var anatomy = CCContact(pair, sword, skins, attackerSkins, target, time);
                    seek.contactGapErrorM = Mathf.Max(seek.contactGapErrorM,
                        Mathf.Abs(original.minimumGapM - anatomy.minimumGapM));
                    seek.contactAnchorErrorM = CAContactAnchorWorldError(original, anatomy);
                }
                seek.withinOneMillimeter = Mathf.Max(seek.maxTrajectoryErrorM, seek.torsoMinimumErrorM,
                    seek.supportMinimumErrorM, seek.supportPointErrorM, seek.fullBodyMinimumErrorM,
                    seek.bladeTipErrorM, seek.contactGapErrorM, seek.contactAnchorErrorM) <= .001f &&
                    seek.torsoFacingUpError <= .0001f && seek.sameSupportBone;
                record.anatomy.backwardSeeks.Add(seek);
            }
            if (record.anatomy.backwardSeeks.Any(s => !s.withinOneMillimeter))
                throw new InvalidOperationException("Backward-seek tolerance exceeded: " + stem);
            bounds.Encapsulate(new Vector3(bounds.center.x, 0, bounds.center.z));
            bounds.Expand(.55f);
            sword.ShowOverlay();
            using var rendering = new GameplayPairRendering(source.gameObject.scene, fighters, pair);
            var sheets = new List<string>();
            for (int index = 0; index < record.anatomy.imageTimes.Length; index += 12)
            {
                string filename = stem + "_Sheet" + (index / 12 + 1).ToString("D2") + ".png";
                rendering.Write(Path.Combine(output, filename), bounds,
                    record.anatomy.imageTimes.Skip(index).Take(12).ToArray(), pair.EvaluateAt);
                sheets.Add(filename);
            }
            record.sheets = sheets.ToArray();
        }
    }
}
