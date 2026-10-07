using System.Collections.Generic;

namespace FrankRetarget
{
    public sealed partial class FrankBattlePairPlayback
    {
        void BuildContactTimes(BattleSfxBank.Move profile)
        {
            var contacts = new SortedSet<float>(BattleHitDamageSequence.ContactTimes(profile, Duration));
            // Non-damaging landings and grapple cues also need their exact shared pose.
            if (profile != null)
                foreach (var cue in profile.cues)
                    if (cue != null && (cue.group == "body_fall" || cue.group == "knockout_fall" ||
                        cue.group == "grapple_grip" || cue.group == "grapple_break" || cue.group == "grapple_release") &&
                        float.IsFinite(cue.seconds) && cue.seconds >= 0 && cue.seconds <= Duration)
                        contacts.Add(cue.seconds);
            contactTimes = new float[contacts.Count];
            contacts.CopyTo(contactTimes);
            nextContact = 0;
        }
    }
}
