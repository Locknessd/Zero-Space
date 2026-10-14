using System;
using FrankRetarget;
using UnityEngine;

namespace ZeroSpace.Showcase
{
    [CreateAssetMenu(menuName = "ZeroSpace/Attack Showcase Library")]
    public sealed class PepeShowcaseLibrary : ScriptableObject
    {
        public GameObject pepePrefab;
        public GameObject mankeyPrefab;
        public Take[] takes = Array.Empty<Take>();
        public Material weaponTrailMaterial;
        public GameObject muzzlePrefab;
        public BattleWeaponTrails.StaticBlade[] bladeGeometry = Array.Empty<BattleWeaponTrails.StaticBlade>();
        public Mesh[] excludedBladeMeshes = Array.Empty<Mesh>();
        public float introSeconds = 1.1f;
        public float leadSeconds = .3f;
        public float holdSeconds = .85f;
        public float outroSeconds = 1.1f;
        public bool continuous;
        public float entryBlendSeconds = .12f;
        public Material motionAccentMaterial;
        public bool bomTrailer;
        public AnimationClip startupAnimation;
        public float startupPoseSeconds;
        public float slashPower = 1;
        public float slashAfterglow = .25f;
        public float airCutLifetime;
        public float Duration
        {
            get
            {
                float result = introSeconds + outroSeconds;
                foreach (var take in takes)
                    result += take.Duration + leadSeconds + holdSeconds;
                return result;
            }
        }

        public float TakeStart(int index)
        {
            float result = introSeconds;
            for (int i = 0; i < index; i++)
                result += takes[i].Duration + leadSeconds + holdSeconds;
            return result;
        }

        [Serializable]
        public sealed class Take
        {
            public string title;
            public AnimationClip animation;
            public FrankAttackTrack attackTrack;
            public float sourceStartSeconds;
            public float sourceEndSeconds;
            public float Duration => attackTrack ? attackTrack.Duration :
                (sourceEndSeconds > sourceStartSeconds ? sourceEndSeconds : animation.length) - sourceStartSeconds;
            public FrankTestDriver pepeDriver;
            public FrankTestDriver mankeyDriver;
            public FrankPairGrounding grounding;
            public string animatorName;
            public bool nativeWeaponEffects;
            public Cue[] cues = Array.Empty<Cue>();
            public GameObject slashPrefab;
            public float slashScale = 1;
            public Color trailColor = new Color(1, .78f, .5f, .72f);
            public float trailLifetime = .085f;
            public float bladeStart = .24f;
            public float yawStart = -35;
            public float yawEnd = 30;
            public float cameraDistance = 3.8f;
            public bool lockCamera;
        }

        [Serializable]
        public sealed class Cue
        {
            public float seconds;
            public string group;
            public bool gun;
            public bool swing;
            public float effectStart;
            public float effectEnd;
            public Sound[] sounds = Array.Empty<Sound>();
        }

        [Serializable]
        public sealed class Sound
        {
            public AudioClip clip;
            public float offset;
            public float volume = .7f;
            public float pitch = 1;
        }
    }
}
