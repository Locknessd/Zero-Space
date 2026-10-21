using UnityEngine;

namespace MemeArena
{
    // Compatibility facade for the imported package's public methods.
    public sealed class MemeCrowdReactions : MonoBehaviour
    {
        MemeCrowdReactionManager manager;
        void Awake()
        {
            manager = GetComponent<MemeCrowdReactionManager>();
            if (!manager) manager = gameObject.AddComponent<MemeCrowdReactionManager>();
            manager.RebuildCache();
        }
        MemeCrowdReactionManager Manager => manager ? manager : GetComponent<MemeCrowdReactionManager>();
        public void Cheer(float intensity, float duration) =>
            Manager?.React(MemeCrowdSide.Neutral, MemeCrowdReaction.Cheer, intensity, duration);
        public void OnHit() => Manager?.OnHit(MemeCrowdSide.Neutral);
        public void OnCombo() => Manager?.OnCombo(MemeCrowdSide.Neutral);
        public void OnLowHealth() => Manager?.OnLowHealth(MemeCrowdSide.Neutral);
        public void OnKO() => Manager?.OnKO(MemeCrowdSide.FighterA);
        [ContextMenu("Test Crowd Cheer")]
        void TestCrowdCheer() => OnCombo();
    }
}
