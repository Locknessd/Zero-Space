using System.Collections.Generic;
using UnityEngine;

public sealed partial class BattleImpactFeedback
{
    [Header("Defender surface accent")]
    [Range(0, 1)] public float surfaceFlashStrength = .55f;
    [Range(.035f, .12f)] public float surfaceFlashSeconds = .065f;
    static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");
    readonly Dictionary<CharacterCombat, SurfaceSlot[]> surfaceSlots =
        new Dictionary<CharacterCombat, SurfaceSlot[]>();
    SurfaceSlot[] highlighted;
    float surfaceAge = 1;

    sealed class SurfaceSlot
    {
        public Renderer renderer;
        public MaterialPropertyBlock previous;
        public MaterialPropertyBlock accent;
    }

    void HighlightDefender(CharacterCombat defender)
    {
        ClearSurfaceHighlights();
        if (!defender || flashScale <= 0 || surfaceFlashStrength <= 0)
            return;
        if (!surfaceSlots.TryGetValue(defender, out var slots))
        {
            var selected = new List<SurfaceSlot>();
            foreach (var renderer in defender.GetComponentsInChildren<Renderer>(true))
            {
                bool supported = false;
                foreach (var material in renderer.sharedMaterials)
                    supported |= material && material.HasProperty(HitFlashId);
                if (!supported)
                    continue;
                selected.Add(new SurfaceSlot
                {
                    renderer = renderer,
                    previous = new MaterialPropertyBlock(),
                    accent = new MaterialPropertyBlock()
                });
            }
            slots = selected.ToArray();
            surfaceSlots.Add(defender, slots);
        }
        foreach (var slot in slots)
        {
            if (!slot.renderer)
                continue;
            slot.renderer.GetPropertyBlock(slot.previous);
            slot.renderer.GetPropertyBlock(slot.accent);
        }
        highlighted = slots;
        surfaceAge = 0;
        AdvanceSurfaceHighlight(0);
    }

    void AdvanceSurfaceHighlight(float delta)
    {
        if (highlighted == null)
            return;
        surfaceAge += delta;
        float strength = surfaceFlashStrength * flashScale *
            Mathf.Clamp01(1 - surfaceAge / Mathf.Max(.001f, surfaceFlashSeconds));
        if (strength <= 0)
        {
            ClearSurfaceHighlights();
            return;
        }
        foreach (var slot in highlighted)
        {
            if (!slot.renderer)
                continue;
            slot.accent.SetFloat(HitFlashId, strength);
            slot.renderer.SetPropertyBlock(slot.accent);
        }
    }

    void ClearSurfaceHighlights()
    {
        if (highlighted == null)
            return;
        foreach (var slot in highlighted)
            if (slot.renderer)
                slot.renderer.SetPropertyBlock(slot.previous);
        highlighted = null;
        surfaceAge = 1;
    }
}
