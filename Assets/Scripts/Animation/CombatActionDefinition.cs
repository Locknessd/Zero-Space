using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Authoring and validation data for actions played by the existing battle exchange.</summary>
[CreateAssetMenu(menuName = "Battle/Combat Action Definition")]
public sealed class CombatActionDefinition : ScriptableObject
{
    public enum Result { StandingRecoil, Stagger, Knockdown, Release, Hold, Escape }
    public enum ReactionPolicy { AuthoredPairContinuation, BlendFromCurrentPose, StrongerStateOverride }

    [Serializable]
    public sealed class Contact
    {
        public string strikeId;
        public AnimationClip attack;
        public AnimationClip reaction;
        [Min(0)] public float seconds;
        public Vector2 activeWindowSeconds;
        public HumanBodyBones strikingLimb;
        public string targetRegion;
        public Vector3 directionInVictimSpace;
        public ReactionPolicy reactionPolicy;
        public Result result;
        public string presentationGroup;
        public bool nonDamagingInteraction;
        [TextArea] public string continuation;
    }

    public string actionId;
    public string displayName;
    [TextArea] public string gameplayTrigger;
    [TextArea] public string entryAndInterruption;
    [TextArea] public string recovery;
    [TextArea] public string presentation;
    public Contact[] contacts = Array.Empty<Contact>();

    public IEnumerable<string> Validate(CombatTripletData move, BattleSfxBank bank)
    {
        if (move == null || move.moveName != actionId)
            yield return "Move ID does not match its action definition.";
        if (contacts == null || contacts.Length == 0)
        {
            yield return "Action has no authored contacts.";
            yield break;
        }
        var profile = bank ? bank.FindMove(move) : null;
        if (profile == null)
            yield return "Action has no shared sound and VFX timeline.";
        var ids = new HashSet<string>(StringComparer.Ordinal);
        float last = -1;
        foreach (var contact in contacts)
        {
            if (contact == null)
            {
                yield return "Null contact record.";
                continue;
            }
            if (string.IsNullOrEmpty(contact.strikeId) || !ids.Add(contact.strikeId))
                yield return "Missing or repeated strike identity.";
            if (!contact.attack || !contact.reaction)
                yield return contact.strikeId + ": missing attack or victim reaction.";
            if (!float.IsFinite(contact.seconds) || contact.seconds < 0 || contact.seconds < last ||
                contact.activeWindowSeconds.x > contact.seconds || contact.activeWindowSeconds.y < contact.seconds)
                yield return contact.strikeId + ": invalid authoritative contact window in seconds.";
            last = contact.seconds;
            var group = bank ? bank.FindGroup(contact.presentationGroup) : null;
            if (group == null || group.clips == null || group.clips.Length == 0 || !group.output)
                yield return contact.strikeId + ": missing routed contact audio.";
            if (profile != null && !Array.Exists(profile.cues, cue => cue != null &&
                Mathf.Abs(cue.seconds - contact.seconds) < .0001f && cue.group == contact.presentationGroup &&
                (cue.hasContactPoint || cue.damageOnLanding || contact.nonDamagingInteraction)))
                yield return contact.strikeId + ": missing positioned presentation cue on the shared clock.";
            if (string.IsNullOrWhiteSpace(contact.targetRegion) || string.IsNullOrWhiteSpace(contact.continuation))
                yield return contact.strikeId + ": contact response policy is incomplete.";
        }
    }
}
