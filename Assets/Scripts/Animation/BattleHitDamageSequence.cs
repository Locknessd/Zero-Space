using System;
using System.Collections.Generic;

/// <summary>Splits one accepted server damage total across the move's contact times.</summary>
public sealed class BattleHitDamageSequence
{
    readonly float[] contacts;
    readonly long hpBefore, hpAfter, totalDamage, hpLoss;
    readonly Action<long, long> present;
    int next;
    bool cancelled;

    public int HitCount => contacts.Length;
    public int PresentedHits => next;

    public BattleHitDamageSequence(long before, long after, long damage, float[] times, Action<long, long> onHit)
    {
        if (before < 0 || after < 0 || damage < 0 || times == null || times.Length == 0 || onHit == null)
            throw new ArgumentException("Damage presentation requires nonnegative totals and contact times.");
        hpBefore = before; hpAfter = after; totalDamage = damage; hpLoss = Math.Max(0, before - after);
        contacts = (float[])times.Clone(); Array.Sort(contacts);
        foreach (float time in contacts)
            if (!float.IsFinite(time) || time < 0) throw new ArgumentException("Invalid contact time.");
        present = onHit;
    }

    public void Advance(float seconds)
    {
        if (cancelled || !float.IsFinite(seconds)) return;
        while (next < contacts.Length && contacts[next] <= seconds)
        {
            int index = next++;
            long lost = Cumulative(hpLoss, next, contacts.Length);
            long hp = next == contacts.Length ? hpAfter : hpBefore - lost;
            present(hp, Portion(totalDamage, index, contacts.Length));
            if (cancelled) break;
        }
    }

    // Only used after successful animation completion, for profiles with a terminal contact.
    public void Complete() => Advance(float.MaxValue);
    public void Cancel() => cancelled = true;

    static long Portion(long total, int index, int count) =>
        total / count + (index >= count - total % count ? 1 : 0);

    static long Cumulative(long total, int completed, int count) =>
        total / count * completed + Math.Max(0L, completed - (count - total % count));

    public static float[] ContactTimes(BattleSfxBank.Move profile, float duration)
    {
        if (profile?.cues == null) return Array.Empty<float>();
        var times = new List<float>();
        foreach (var cue in profile.cues)
            if (cue != null && (cue.group == "light_hit" || cue.group == "heavy_hit" || cue.group == "stab_hit" ||
                cue.damageOnLanding && (cue.group == "body_fall" || cue.group == "knockout_fall"))) Add(cue);
        // A throw has no weapon/body strike cue: its final landing is the damage contact.
        if (times.Count == 0)
            foreach (var cue in profile.cues)
                if (cue != null && cue.finalLanding && (cue.group == "body_fall" || cue.group == "knockout_fall")) Add(cue);
        times.Sort(); return times.ToArray();

        void Add(BattleSfxBank.Cue cue)
        {
            if (float.IsFinite(cue.seconds) && cue.seconds >= 0) times.Add(Math.Min(duration, cue.seconds));
        }
    }
}
