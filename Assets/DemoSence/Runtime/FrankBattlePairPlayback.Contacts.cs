using System.Collections.Generic;

namespace FrankRetarget
{
    public sealed partial class FrankBattlePairPlayback
    {
        void BuildContactTimes(BattleSfxBank.Move profile)
        {
            var contacts = new SortedSet<float>(BattleHitDamageSequence.ContactTimes(profile, Duration));
            // Swings, gunshots and grip cues need the same exact clock as hit VFX.
            // Otherwise audio crossed by a slow frame plays at the next hit/end pose.
            if (profile != null)
                foreach (var cue in profile.cues)
                    if (cue != null && float.IsFinite(cue.seconds) && cue.seconds >= 0 && cue.seconds <= Duration)
                        contacts.Add(cue.seconds);
            contactTimes = new float[contacts.Count];
            contacts.CopyTo(contactTimes);
            nextContact = 0;
        }
    }
}
