using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        static void SearchSequentialStrike(SequentialPlayer player, SequentialCase record, int index)
        {
            player.Rebuild(index);
            var strike = new SequentialStrike
            {
                strike = index + 1, start = SequentialStarts[index], end = SequentialEnds[index]
            };
            record.strikes.Add(strike);
            record.phase = "search_strike" + strike.strike;
            using var probe = SequentialProbe(player, record, index);
            var times = new SortedSet<float> { strike.start, strike.end };
            for (int frame = 0; strike.start + frame / 120f < strike.end; frame++)
                times.Add(strike.start + frame / 120f);
            foreach (float seconds in times)
            {
                strike.samples.Add(MeasureSequential(player, probe, index, seconds));
                SaveSequentialCase(record);
            }
            // Refine every coarse interval: this also retains narrow approach/recoil minima on misses.
            float[] coarse = times.ToArray();
            for (int frame = 1; frame < coarse.Length; frame++)
            {
                float seconds = (coarse[frame - 1] + coarse[frame]) / 2;
                var sample = MeasureSequential(player, probe, index, seconds);
                sample.refined = true;
                strike.samples.Add(sample);
                SaveSequentialCase(record);
            }
            strike.samples = strike.samples.OrderBy(s => s.contact.seconds).ToList();
            SequentialSample previous = MeasureSequential(player, probe, index, strike.start - 1 / 240f);
            foreach (var sample in strike.samples)
            {
                ClassifySequential(sample, previous, index, record.lane);
                previous = sample;
            }
            strike.minimum = strike.samples.OrderBy(s => s.contact.gap).ThenBy(s => s.contact.seconds).First();
            for (int row = 0; row < strike.samples.Count; row++)
            {
                float gap = strike.samples[row].contact.gap;
                if ((row == 0 || gap <= strike.samples[row - 1].contact.gap) &&
                    (row == strike.samples.Count - 1 || gap <= strike.samples[row + 1].contact.gap))
                    strike.localMinima.Add(strike.samples[row]);
            }
            bool priorComplete = record.onsets.Take(index).All(onset => onset >= 0);
            var candidate = strike.samples.FirstOrDefault(s => s.eligible);
            if (candidate != null && priorComplete)
            {
                candidate.selected = true;
                candidate.contact.status = "PROVISIONAL_ADVANCING_NEAR_SURFACE_NOT_ACCEPTED";
                strike.onset = candidate.contact.seconds;
                record.onsets[index] = strike.onset;
                strike.status = "FOUND_PROVISIONAL";
            }
            else
                strike.status = priorComplete ? "MISS_NO_LEGAL_ADVANCING_CONTACT" :
                    "DIAGNOSTIC_ONLY_PRIOR_CONTACT_MISSED";
            File.WriteAllText(SequentialOutput + "/" + record.name + "_Strike" + strike.strike + ".csv",
                ContactCsv(strike.samples.Select(s => s.contact)));
            SaveSequentialCase(record);
        }

        static CombatExpansionAxeDenseStudy.SkinRegionProbe SequentialProbe(SequentialPlayer player,
            SequentialCase record, int index)
        {
            var probe = new CombatExpansionAxeDenseStudy.SkinRegionProbe(player.Source,
                index == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand,
                player.Target, SequentialTargetBone(player, index));
            if (!record.geometry.Contains(probe.SelectionSummary))
                record.geometry.Add(probe.SelectionSummary);
            return probe;
        }

        static HumanBodyBones SequentialTargetBone(SequentialPlayer player, int index) =>
            bodyJabVariants && player.Source.name == "Pepe" && index < 2
                ? HumanBodyBones.Chest : HumanBodyBones.Head;

        static SequentialSample MeasureSequential(SequentialPlayer player,
            CombatExpansionAxeDenseStudy.SkinRegionProbe probe, int index, float seconds)
        {
            var floor = player.Sample(seconds);
            var contact = probe.Measure(new List<string>());
            var hand = player.Source.Animator.GetBoneTransform(
                index == 0 ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var targetBone = SequentialTargetBone(player, index);
            var head = player.Target.Animator.GetBoneTransform(targetBone);
            return new SequentialSample
            {
                floor = floor,
                contact = ContactRow(seconds, player.Pair.SampleTime, index + 1, targetBone.ToString(), contact,
                    hand, head, floor.attackerAfter, floor.receiverAfter, null)
            };
        }

        static void ClassifySequential(SequentialSample sample, SequentialSample previous, int index, int lane)
        {
            var row = sample.contact;
            var before = previous.contact;
            float interval = row.seconds - before.seconds;
            if (interval <= 0)
                throw new InvalidOperationException("Nonpositive contact velocity interval.");
            row.velocityAvailable = true;
            row.movementInterval = interval;
            row.handVelocity = (row.handPivot - before.handPivot) / interval;
            row.handDirection = row.handVelocity.normalized;
            sample.targetVelocity = (row.targetPivot - before.targetPivot) / interval;
            row.relativeVelocity = row.handVelocity - sample.targetVelocity;
            row.closingSpeed = Vector3.Dot(row.relativeVelocity, (row.targetPivot - row.handPivot).normalized);
            sample.forwardSpeed = Vector3.Dot(row.handVelocity, Vector3.right * lane);
            sample.gapClosingSpeed = (before.gap - row.gap) / interval;
            float blendEnd = index == 0 ? SequentialEntryBlend : index * .4f + SequentialLinkBlend;
            sample.inBlend = before.seconds < blendEnd - .000001f;
            sample.windowEntry = row.seconds <= SequentialStarts[index] + .000001f;
            sample.nearSurface = row.gap <= .001f;
            sample.advancing = sample.forwardSpeed > .001f && row.closingSpeed > .001f &&
                sample.gapClosingSpeed > .001f;
            sample.eligible = sample.nearSurface && sample.advancing && !sample.inBlend && !sample.windowEntry;
            var flags = new List<string>();
            if (sample.inBlend)
                flags.Add("ENTRY_OR_SOURCE_BLEND");
            if (sample.windowEntry)
                flags.Add("WINDOW_ENTRY");
            if (!sample.advancing)
                flags.Add("NOT_FORWARD_CLOSING_OR_RECOIL");
            if (!sample.nearSurface)
                flags.Add("GAP_OVER_1MM");
            if (row.gap <= .000001f)
                flags.Add("UNSIGNED_ZERO_TOUCH_OR_PENETRATION");
            sample.flags = string.Join(";", flags);
            row.status = sample.eligible ? "PROVISIONAL_ELIGIBLE_NOT_ACCEPTED" : "EXCLUDED_" + sample.flags;
        }
    }
}
