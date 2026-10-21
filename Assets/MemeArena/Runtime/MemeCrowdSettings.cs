using System;
using UnityEngine;

namespace MemeArena
{
    public enum MemeCrowdSide { Neutral, FighterA, FighterB }
    public enum MemeCrowdReaction { Idle, Jump, Cheer, Boo, Victory, Defeat }

    [CreateAssetMenu(menuName = "Meme Arena/Crowd settings")]
    public sealed class MemeCrowdSettings : ScriptableObject
    {
        [Serializable]
        public sealed class Spectator
        {
            public string name;
            public Sprite sprite;
            public GameObject prefab;
            [Min(.01f)] public float weight = 1;
        }

        public Spectator[] spectators = Array.Empty<Spectator>();
        [HideInInspector] public string rosterId;
        [HideInInspector] public int importedSourceRows;
        [Range(15, 60)] public int animationRate = 30;
        [Min(5)] public float animatedDistance = 45;
        [Range(.5f, 2)] public float spectatorHeight = 1.2f;
        [Range(0, .3f)] public float idleBounce = .045f;
        [Range(0, .8f)] public float reactionBounce = .3f;
        [Range(0, .5f)] public float reactionStagger = .22f;
    }
}
