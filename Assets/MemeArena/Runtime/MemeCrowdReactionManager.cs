using UnityEngine;

namespace MemeArena
{
    [DisallowMultipleComponent]
    public sealed class MemeCrowdReactionManager : MonoBehaviour
    {
        public MemeCrowdSettings settings;
        [Range(0, 2)] public float intensity = 1;
        public Camera view;
        public MemeArenaCelebration celebration;
        MemeCrowdGroup[] groups;
        MemeCrowdFan[] fans;
        float nextTick;
        public int ReactionCount { get; private set; }
        public int AnimatedCount => fans == null ? 0 : fans.Length;

        void Awake() => RebuildCache();

        public void RebuildCache()
        {
            groups = GetComponentsInChildren<MemeCrowdGroup>(true);
            fans = GetComponentsInChildren<MemeCrowdFan>(true);
            foreach (var fan in fans) fan.CachePose();
            if (!view) view = Camera.main;
        }

        void Update()
        {
            if (Time.time < nextTick) return;
            nextTick = Time.time + 1f / (settings ? settings.animationRate : 30);
            Tick(Time.time);
        }

        public void Tick(float now)
        {
            if (fans == null) RebuildCache();
            float distance = settings ? settings.animatedDistance : 45;
            foreach (var fan in fans)
                if (fan && fan.gameObject.activeInHierarchy &&
                    (!view || (fan.transform.position - view.transform.position).sqrMagnitude < distance * distance))
                    fan.Tick(now, settings);
        }

        public void React(MemeCrowdSide side, MemeCrowdReaction reaction, float strength, float duration)
        {
            if (groups == null) RebuildCache();
            ReactionCount++;
            foreach (var group in groups)
                if (side == MemeCrowdSide.Neutral || group.supporters == side ||
                    (group.supporters == MemeCrowdSide.Neutral && reaction != MemeCrowdReaction.Defeat &&
                        reaction != MemeCrowdReaction.Boo))
                    group.React(reaction, strength * intensity, duration, Time.time);
            if (groups.Length == 0)
                foreach (var fan in fans) fan.React(reaction, strength * intensity, duration, Time.time);
        }

        public void OnHit(MemeCrowdSide attacker) => React(attacker, MemeCrowdReaction.Jump, 1, .65f);
        public void OnCombo(MemeCrowdSide attacker) => React(attacker, MemeCrowdReaction.Cheer, 1.6f, 1.4f);
        public void OnCritical(MemeCrowdSide attacker) => React(attacker, MemeCrowdReaction.Cheer, 2, 1.5f);
        public void OnLowHealth(MemeCrowdSide hurt) => React(hurt, MemeCrowdReaction.Boo, 1, 1.8f);
        public void OnRoundStart() => React(MemeCrowdSide.Neutral, MemeCrowdReaction.Cheer, 1, 1.5f);

        public void OnKO(MemeCrowdSide winner)
        {
            React(winner, MemeCrowdReaction.Victory, 2, 3.5f);
            React(winner == MemeCrowdSide.FighterA ? MemeCrowdSide.FighterB : MemeCrowdSide.FighterA,
                MemeCrowdReaction.Defeat, 1.1f, 3.5f);
            if (celebration) celebration.Play();
        }

        public void ResetReactions() => React(MemeCrowdSide.Neutral, MemeCrowdReaction.Idle, 0, 0);
    }
}
