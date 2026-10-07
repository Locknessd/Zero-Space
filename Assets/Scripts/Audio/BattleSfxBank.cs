using System;
using UnityEngine;
using UnityEngine.Audio;

[CreateAssetMenu(menuName = "Battle/SFX Bank")]
public sealed class BattleSfxBank : ScriptableObject
{
    [Serializable]
    public sealed class Group
    {
        public string id;
        [Range(0f, 1f)] public float volume = .6f;
        public Vector2 pitch = Vector2.one;
        public AudioClip[] clips = Array.Empty<AudioClip>();
        public float[] clipGains = Array.Empty<float>();
        [Tooltip("Optional trim in seconds for each variant, applied before playback.")]
        public float[] startOffsets = Array.Empty<float>();
        public AudioMixerGroup output;
        [Range(1, 8)] public int maxConcurrent = 3;
        [Tooltip("Lower numbers have higher priority. Zero uses the cue family default.")]
        [Range(0, 256)] public int priority;
        [Range(0, 2)] public float gainVariationDb = .65f;
        [Tooltip("Additional cue groups aligned to this cue, with no delayed playback. One layer level only.")]
        public string[] layers = Array.Empty<string>();
    }

    [Serializable]
    public sealed class ContactAnchor
    {
        public Avatar avatar;
        public HumanBodyBones bone;
        public Vector3 offset;
    }

    [Serializable]
    public sealed class Cue
    {
        [Min(0)] public float seconds;
        public string group;
        [Tooltip("Use knockout_fall for this landing when the receiver dies.")]
        public bool finalLanding;
        [Tooltip("Contact sampled on the receiver's animated mesh; shared by impact and damage timing.")]
        public bool hasContactPoint;
        public HumanBodyBones contactBone = HumanBodyBones.Chest;
        public Vector3 contactOffset;
        public string contactSource;
        public ContactAnchor[] avatarContacts = Array.Empty<ContactAnchor>();
        [Tooltip("A throw finisher deals its share of damage on the ground impact.")]
        public bool damageOnLanding;

        public bool TryContactPosition(Animator receiver, out Vector3 position)
        {
            position = Vector3.zero;
            if (!hasContactPoint || !receiver || !receiver.isHuman)
                return false;
            var bone = contactBone;
            var offset = contactOffset;
            foreach (var entry in avatarContacts ?? Array.Empty<ContactAnchor>())
            {
                if (entry == null || entry.avatar != receiver.avatar)
                    continue;
                bone = entry.bone;
                offset = entry.offset;
                break;
            }
            var anchor = receiver.GetBoneTransform(bone);
            if (!anchor)
                return false;
            position = anchor.TransformPoint(offset);
            return float.IsFinite(position.sqrMagnitude);
        }
    }

    [Serializable]
    public sealed class Move
    {
        public string label;
        public AnimationClip attack;
        public AnimationClip reaction;
        public Cue[] cues = Array.Empty<Cue>();
    }

    public AudioMixer mixer;
    public Group[] groups = Array.Empty<Group>();
    public Move[] moves = Array.Empty<Move>();

    public Group FindGroup(string id) => Array.Find(groups, g => g != null && g.id == id);

    public Move FindMove(CombatTripletData move)
    {
        if (move != null && move.grapple) return move.grapple.Presentation(move.grappleOutcome);
        if (move?.sourcePair == null) return null;
        return Array.Find(moves, m => m != null && m.attack == move.sourcePair.attack &&
            m.reaction == move.sourcePair.reaction);
    }
}
