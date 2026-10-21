using UnityEngine;

namespace MemeArena
{
    public sealed class MemeCrowdGroup : MonoBehaviour
    {
        public MemeCrowdSide supporters;
        public Renderer[] staticBatches = System.Array.Empty<Renderer>();
        public MemeCrowdFan[] animatedFans = System.Array.Empty<MemeCrowdFan>();
        MaterialPropertyBlock block;
        public MemeCrowdReaction LastReaction { get; private set; }
        public int ReactionCount { get; private set; }

        public void React(MemeCrowdReaction reaction, float intensity, float duration, float now)
        {
            LastReaction = reaction;
            ReactionCount++;
            block ??= new MaterialPropertyBlock();
            block.SetFloat("_ReactionType", (int)reaction);
            block.SetFloat("_ReactionStart", now);
            block.SetFloat("_ReactionDuration", duration);
            block.SetFloat("_ReactionStrength", Mathf.Clamp(intensity, 0, 3));
            foreach (var renderer in staticBatches)
                if (renderer) renderer.SetPropertyBlock(block);
            foreach (var fan in animatedFans)
                if (fan) fan.React(reaction, intensity, duration, now);
        }
    }
}
