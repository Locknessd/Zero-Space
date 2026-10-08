using System;
using FrankRetarget;
using UnityEngine;

/// <summary>Optional complete receiver outcome on the unchanged, accepted attack clock.</summary>
[Serializable]
public sealed partial class CombatLethalPairVariant
{
    public AnimationClip reaction;
    public FrankPairGrounding grounding;
    public BattleSfxBank.Move presentationProfile;
    public CombatActionDefinition.Contact[] contacts = Array.Empty<CombatActionDefinition.Contact>();

    public float Duration(FrankBattlePair pair) =>
        Mathf.Max(pair.attacks ? pair.attacks.Duration : pair.attack.length, reaction.length);

    // False rejects configuration before either participant enters source playback.
    // Absent data and nonlethal playback deliberately bypass optional-variant validation.
    public static bool TrySelect(CombatTripletData move, bool lethal, BattleSfxBank bank,
        Avatar attackerAvatar, Avatar receiverAvatar, out CombatLethalPairVariant selected, out string error)
    {
        selected = null;
        error = null;
        var candidate = move?.actionDefinition ? move.actionDefinition.lethalPairVariant : null;
        if (!lethal || candidate == null)
            return true;
        if (!candidate.TryValidate(move, bank, attackerAvatar, receiverAvatar, out error))
            return false;
        selected = candidate;
        return true;
    }

    public bool TryValidate(CombatTripletData move, BattleSfxBank bank,
        Avatar attackerAvatar, Avatar receiverAvatar, out string error)
    {
        error = null;
        if (move?.sourcePair == null || move.grapple || !move.actionDefinition ||
            !move.sourcePair.attack || !move.sourcePair.reaction)
            return Fail("A nongrapple source pair and action definition are required.", out error);
        if (!reaction || !float.IsFinite(reaction.length) || reaction.length <= 0 ||
            !grounding || presentationProfile == null || presentationProfile.cues == null ||
            presentationProfile.cues.Length == 0 || contacts == null || contacts.Length == 0 || !bank)
            return Fail("Receiver clip, grounding, routed timeline and contact mappings are required.", out error);
        if (presentationProfile.attack != move.sourcePair.attack || presentationProfile.reaction != reaction)
            return Fail("Variant timeline must retain the source attack and identify its complete receiver clip.", out error);
        var baseline = bank.FindMove(move);
        if (baseline?.cues == null || baseline.cues.Length == 0 ||
            move.actionDefinition.contacts == null || move.actionDefinition.contacts.Length == 0)
            return Fail("The original action must have an authored timeline and contact mappings.", out error);
        float duration = Duration(move.sourcePair);
        if (!float.IsFinite(duration) || duration <= 0)
            return Fail("Invalid variant duration.", out error);
        if (!ValidateGrounding(duration, attackerAvatar, receiverAvatar, out error))
            return false;
        float previous = -1;
        foreach (var cue in presentationProfile.cues)
        {
            if (cue == null || !float.IsFinite(cue.seconds) || cue.seconds < previous ||
                cue.seconds < 0 || cue.seconds > duration || string.IsNullOrEmpty(cue.group))
                return Fail("Variant cues must be complete and ordered on the full pair clock.", out error);
            previous = cue.seconds;
            var group = bank.FindGroup(cue.group);
            if (group?.clips == null || group.clips.Length == 0 || !group.output ||
                Array.Exists(group.clips, clip => !clip))
                return Fail("Variant cue has no complete routed audio group: " + cue.group, out error);
            if (cue.finalLanding)
            {
                var knockout = bank.FindGroup("knockout_fall");
                if (!IsLanding(cue) || knockout?.clips == null || knockout.clips.Length == 0 || !knockout.output)
                    return Fail("Final landings require routed knockout audio.", out error);
            }
            if ((IsDamage(cue) || IsLanding(cue)) && !ValidAnchor(cue))
                return Fail("Every impact needs a finite explicit receiver contact anchor.", out error);
            if (cue.damageOnLanding && !IsLanding(cue))
                return Fail("Only an original landing may carry landing damage.", out error);
        }
        if (!ValidateSourceTimeline(baseline, move.sourcePair, out error))
            return false;
        return ValidateContacts(move, out error);
    }

    bool ValidateSourceTimeline(BattleSfxBank.Move baseline, FrankBattlePair pair, out string error)
    {
        error = null;
        // Preserve every nonlanding source cue, including swings; landing feedback alone can differ.
        foreach (var cue in baseline.cues)
        {
            if (cue == null)
                return Fail("Original timeline contains a null cue.", out error);
            if (!IsLanding(cue) && Count(baseline, cue) != Count(presentationProfile, cue))
                return Fail("Variant changed the original attack cue timeline.", out error);
        }
        foreach (var cue in presentationProfile.cues)
            if (!IsLanding(cue) && Count(baseline, cue) != Count(presentationProfile, cue))
                return Fail("Only measured, nondamaging landing feedback may be added.", out error);
        float originalDuration = Mathf.Max(pair.attacks ? pair.attacks.Duration : pair.attack.length,
            pair.reactions ? pair.reactions.Duration : pair.reactionDelay + pair.reaction.length);
        var originalTimes = BattleHitDamageSequence.ContactTimes(baseline, originalDuration);
        var variantTimes = BattleHitDamageSequence.ContactTimes(presentationProfile, Duration(pair));
        if (originalTimes.Length == 0 || originalTimes.Length != variantTimes.Length)
            return Fail("Variant must preserve the original nonempty damage contact count.", out error);
        for (int i = 0; i < originalTimes.Length; i++)
            if (originalTimes[i] != variantTimes[i])
                return Fail("Variant changed an authoritative damage contact time.", out error);
        return true;
    }

    static int Count(BattleSfxBank.Move profile, BattleSfxBank.Cue expected)
    {
        int count = 0;
        foreach (var cue in profile.cues)
            if (cue != null && cue.group == expected.group && cue.seconds == expected.seconds &&
                cue.damageOnLanding == expected.damageOnLanding)
                count++;
        return count;
    }

    public bool TryValidateReceiver(Animator receiver, out string error)
    {
        error = null;
        foreach (var cue in presentationProfile.cues)
            if ((IsDamage(cue) || IsLanding(cue)) && !cue.TryContactPosition(receiver, out _))
                return Fail("A measured contact bone is unavailable on the selected receiver.", out error);
        return true;
    }

    static bool ValidAnchor(BattleSfxBank.Cue cue)
    {
        if (!cue.hasContactPoint || !ValidBone(cue.contactBone) || !Finite(cue.contactOffset) ||
            string.IsNullOrWhiteSpace(cue.contactSource))
            return false;
        foreach (var anchor in cue.avatarContacts ?? Array.Empty<BattleSfxBank.ContactAnchor>())
            if (anchor == null || !anchor.avatar || !ValidBone(anchor.bone) || !Finite(anchor.offset))
                return false;
        return true;
    }

    static bool ValidBone(HumanBodyBones bone) => bone >= HumanBodyBones.Hips && bone < HumanBodyBones.LastBone;
    static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    static bool IsLanding(BattleSfxBank.Cue cue) => cue.group == "body_fall" || cue.group == "knockout_fall";
    static bool IsDamage(BattleSfxBank.Cue cue) => cue.group == "light_hit" || cue.group == "heavy_hit" ||
        cue.group == "stab_hit" || cue.damageOnLanding && IsLanding(cue);
    static bool Fail(string message, out string error)
    {
        error = message;
        return false;
    }
}
