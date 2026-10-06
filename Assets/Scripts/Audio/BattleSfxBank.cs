using System;
using UnityEngine;

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
        [Tooltip("A throw finisher deals its share of damage on the ground impact.")]
        public bool damageOnLanding;
    }

    [Serializable]
    public sealed class Move
    {
        public string label;
        public AnimationClip attack;
        public AnimationClip reaction;
        public Cue[] cues = Array.Empty<Cue>();
    }

    public Group[] groups = Array.Empty<Group>();
    public Move[] moves = Array.Empty<Move>();

    public Group FindGroup(string id) => Array.Find(groups, g => g != null && g.id == id);

    public Move FindMove(CombatTripletData move)
    {
        if (move?.sourcePair == null) return null;
        return Array.Find(moves, m => m != null && m.attack == move.sourcePair.attack &&
            m.reaction == move.sourcePair.reaction);
    }
}
