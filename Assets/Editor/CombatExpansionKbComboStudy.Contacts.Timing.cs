using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace FrankRetarget.Editor
{
    public static partial class CombatExpansionKbComboStudy
    {
        static IEnumerable<float> ContactsTimes(float duration, float attackEnd)
        {
            var times = new Dictionary<int, float>();
            Action<float> add = seconds =>
            {
                seconds = Mathf.Clamp(seconds, 0, duration);
                times[Mathf.RoundToInt(seconds * 1000000)] = seconds;
            };
            for (int frame = 0; frame <= Mathf.CeilToInt(duration * 60); frame++)
                add(frame / 60f);
            foreach (float center in new[] { .1f, .525f, 1.51f })
            for (int frame = 0; frame <= 48; frame++)
                add(center - .1f + frame / 240f);
            foreach (float seconds in new[] { 0, .1f, .12f, .399f, .4f, .401f, .48f, .525f,
                .799f, .8f, .801f, .88f, 1.51f, attackEnd, duration })
                add(seconds);
            return times.Values.OrderBy(seconds => seconds);
        }

        static IEnumerable<int> ContactStrikes(float seconds)
        {
            if (seconds <= .4f)
                yield return 1;
            if (seconds >= .4f && seconds <= .8f)
                yield return 2;
            if (seconds >= .8f)
                yield return 3;
        }

        static void SelectContactCandidates(ContactsCase record, List<ContactSnapshot> snapshots, float attackEnd)
        {
            var all = snapshots.SelectMany(s => s.contacts).ToArray();
            float[] starts = { 0, .4f, .8f };
            float[] ends = { .4f, .8f, attackEnd };
            var summary = new StringBuilder(ContactsScope + "\n\n" + record.name + "\n");
            summary.AppendLine("Started UTC: " + record.startedUtc);
            summary.AppendLine("Each minimum is provisional; entry or recoil minima may not be a credible strike.");
            for (int strike = 1; strike <= 3; strike++)
            foreach (string surface in new[] { "Head", "Torso" })
            {
                var window = all.Where(r => r.strike == strike && r.surface == surface &&
                    r.seconds >= starts[strike - 1] && r.seconds <= ends[strike - 1]).ToArray();
                if (window.Length == 0)
                    throw new InvalidOperationException("Missing contact window " + strike + "/" + surface);
                var best = window.OrderBy(r => r.gap).ThenBy(r => r.seconds).First();
                record.best.Add(best);
                var near = window.Where(r => r.gap <= .001f).ToArray();
                summary.AppendLine(FormattableString.Invariant(
                    $"strike{strike} {best.limb} vs {surface}: gap={best.gap:R}m at pair={best.pairClock:R}s; ") +
                    FormattableString.Invariant($"samples={window.Length}; <=1mm samples={near.Length}."));
                if (near.Length > 0)
                    summary.AppendLine(FormattableString.Invariant(
                        $"Near-surface extent: first={near.First().seconds:R}s last={near.Last().seconds:R}s; ") +
                        "not necessarily continuous and not contact acceptance.");
                AddContactContext(record, snapshots, best.seconds, "strike" + strike + " " + surface + " minimum");
                AddContactContext(record, snapshots, Mathf.Max(starts[strike - 1], best.seconds - .1f),
                    "strike" + strike + " " + surface + " approach context");
                AddContactContext(record, snapshots, Mathf.Min(record.duration, best.seconds + .1f),
                    "strike" + strike + " " + surface + " recoil context");
            }
            foreach (float seconds in new[] { 0, .12f, .399f, .4f, .401f, .48f, .799f, .8f, .801f, .88f,
                .1f, .525f, 1.51f, attackEnd, record.duration })
                AddContactContext(record, snapshots, seconds, seconds <= .12f ? "entry / first reaction context" :
                    seconds == record.duration ? "terminal reaction context" :
                    seconds == attackEnd ? "attack end / recoil context" : "reaction onset / source link context");
            foreach (var context in record.sheetContext.OrderBy(s => s.seconds))
                summary.AppendLine(FormattableString.Invariant($"Sheet T={context.seconds:R}s: {context.label}"));
            File.WriteAllText(ContactsOutput + "/" + record.name + "_Overview.txt", summary.ToString());
        }

        static void AddContactContext(ContactsCase record, List<ContactSnapshot> snapshots,
            float requested, string label)
        {
            float time = snapshots.OrderBy(s => Mathf.Abs(s.seconds - requested)).First().seconds;
            var existing = record.sheetContext.FirstOrDefault(c => c.seconds == time);
            if (existing != null)
                existing.label += "; " + label;
            else
                record.sheetContext.Add(new ContactContext { seconds = time, label = label });
        }
    }
}
