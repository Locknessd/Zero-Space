using System;
using UnityEngine;

public sealed partial class CombatLethalPairVariant
{
    bool ValidateGrounding(float duration, Avatar attackerAvatar, Avatar receiverAvatar, out string error)
    {
        error = null;
        if (grounding.tracks == null)
            return Fail("Variant has no measured grounding tracks.", out error);
        bool attack = false;
        bool receiver = false;
        for (int i = 0; i < grounding.tracks.Length; i++)
        {
            var track = grounding.tracks[i];
            if (track == null || !track.avatar || !float.IsFinite(track.duration) || track.duration < duration ||
                track.lift == null || track.lift.Length < 2 || Array.Exists(track.lift, lift => !float.IsFinite(lift)))
                return Fail("Grounding tracks must contain finite measurements covering the full pair duration.", out error);
            for (int j = 0; j < i; j++)
                if (grounding.tracks[j].avatar == track.avatar && grounding.tracks[j].receiver == track.receiver)
                    return Fail("Grounding has duplicate avatar/role tracks.", out error);
            attack |= !track.receiver && (!attackerAvatar || track.avatar == attackerAvatar);
            receiver |= track.receiver && (!receiverAvatar || track.avatar == receiverAvatar);
        }
        return attack && receiver || Fail("Measured grounding is missing for a participant avatar/role.", out error);
    }

    bool ValidateContacts(CombatTripletData move, out string error)
    {
        error = null;
        float last = -1;
        for (int i = 0; i < contacts.Length; i++)
        {
            var contact = contacts[i];
            if (contact == null || string.IsNullOrWhiteSpace(contact.strikeId) ||
                contact.attack != move.sourcePair.attack || contact.reaction != reaction ||
                !float.IsFinite(contact.seconds) || contact.seconds < last || contact.seconds < 0 ||
                !float.IsFinite(contact.activeWindowSeconds.x) || !float.IsFinite(contact.activeWindowSeconds.y) ||
                contact.activeWindowSeconds.x < 0 || contact.activeWindowSeconds.x > contact.seconds ||
                contact.activeWindowSeconds.y < contact.seconds || contact.activeWindowSeconds.y > Duration(move.sourcePair) ||
                string.IsNullOrWhiteSpace(contact.targetRegion) || string.IsNullOrWhiteSpace(contact.continuation) ||
                !Finite(contact.directionInVictimSpace))
                return Fail("Variant contact mapping is incomplete or outside its authoritative window.", out error);
            last = contact.seconds;
            for (int j = 0; j < i; j++)
                if (contacts[j].strikeId == contact.strikeId)
                    return Fail("Variant contact identities must be unique.", out error);
            var cue = Array.Find(presentationProfile.cues,
                item => item.seconds == contact.seconds && item.group == contact.presentationGroup);
            if (cue == null || !(IsDamage(cue) || IsLanding(cue)) ||
                contact.nonDamagingInteraction == IsDamage(cue))
                return Fail("Variant contact must map exactly to an impact and its damage classification.", out error);
            var original = Array.Find(move.actionDefinition.contacts, item => item != null &&
                item.strikeId == contact.strikeId);
            if (!contact.nonDamagingInteraction && (original == null || original.nonDamagingInteraction ||
                original.seconds != contact.seconds || original.presentationGroup != contact.presentationGroup))
                return Fail("Variant changed an original damaging strike identity or time.", out error);
            if (contact.nonDamagingInteraction && !IsLanding(cue))
                return Fail("Only nondamaging landing mappings may be added.", out error);
        }
        foreach (var original in move.actionDefinition.contacts)
            if (original != null && !original.nonDamagingInteraction &&
                !Array.Exists(contacts, item => !item.nonDamagingInteraction && item.strikeId == original.strikeId))
                return Fail("Variant omitted an original damaging strike identity.", out error);
        foreach (var cue in presentationProfile.cues)
            if (IsDamage(cue) || IsLanding(cue))
            {
                int count = 0;
                foreach (var contact in contacts)
                    if (contact.seconds == cue.seconds && contact.presentationGroup == cue.group)
                        count++;
                if (count != 1)
                    return Fail("Every impact requires exactly one explicit variant contact mapping.", out error);
            }
        return true;
    }
}
