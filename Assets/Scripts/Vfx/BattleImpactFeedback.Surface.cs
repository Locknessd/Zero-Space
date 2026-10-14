using System.Collections.Generic;
using UnityEngine;

public sealed partial class BattleImpactFeedback
{
    [Header("Defender surface accent")]
    [Range(0, 1)] public float surfaceFlashStrength = .55f;
    [Range(.035f, .12f)] public float surfaceFlashSeconds = .065f;
    static readonly int HitFlashId = Shader.PropertyToID("_HitFlash");
    static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    static readonly int DimColorId = Shader.PropertyToID("_ColorDim");
    readonly Dictionary<CharacterCombat, SurfaceSlot[]> surfaceSlots =
        new Dictionary<CharacterCombat, SurfaceSlot[]>();
    SurfaceSlot[] highlighted;
    float surfaceAge = 1;

    sealed class SurfaceSlot
    {
        public Renderer renderer;
        public MaterialPropertyBlock previous;
        public MaterialPropertyBlock accent;
        public int materialIndex = -1;
        public Color baseColor;
        public Color dimColor;
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
                bool legacyFlash = false;
                var materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    var material = materials[index];
                    if (!material)
                        continue;
                    legacyFlash |= material.HasProperty(HitFlashId);
                    if (material.shader.name == "FlatKit/Stylized Surface" &&
                        material.HasProperty(BaseColorId) && material.HasProperty(DimColorId))
                        selected.Add(CreateSurfaceSlot(renderer, index));
                }
                if (legacyFlash)
                    selected.Add(CreateSurfaceSlot(renderer, -1));
            }
            slots = selected.ToArray();
            surfaceSlots.Add(defender, slots);
        }
        foreach (var slot in slots)
        {
            if (!slot.renderer)
                continue;
            ReadSurfaceBlock(slot, slot.previous);
            ReadSurfaceBlock(slot, slot.accent);
            if (slot.materialIndex < 0)
                continue;
            // An indexed block replaces the renderer block. Carry its existing overrides
            // into the temporary accent, then restore the original indexed block exactly.
            if (slot.accent.isEmpty)
                slot.renderer.GetPropertyBlock(slot.accent);
            var material = slot.renderer.sharedMaterials[slot.materialIndex];
            slot.baseColor = slot.accent.HasColor(BaseColorId)
                ? slot.accent.GetColor(BaseColorId) : material.GetColor(BaseColorId);
            slot.dimColor = slot.accent.HasColor(DimColorId)
                ? slot.accent.GetColor(DimColorId) : material.GetColor(DimColorId);
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
            if (slot.materialIndex < 0)
                slot.accent.SetFloat(HitFlashId, strength);
            else
            {
                var bright = new Color(2.6f, 2.25f, 1.8f, slot.baseColor.a);
                var dim = new Color(1.5f, 1.25f, .9f, slot.dimColor.a);
                slot.accent.SetColor(BaseColorId, Color.Lerp(slot.baseColor, bright, strength));
                slot.accent.SetColor(DimColorId, Color.Lerp(slot.dimColor, dim, strength));
            }
            WriteSurfaceBlock(slot, slot.accent);
        }
    }

    void ClearSurfaceHighlights()
    {
        if (highlighted == null)
            return;
        foreach (var slot in highlighted)
            if (slot.renderer)
                WriteSurfaceBlock(slot, slot.previous);
        highlighted = null;
        surfaceAge = 1;
    }

    static SurfaceSlot CreateSurfaceSlot(Renderer renderer, int materialIndex)
    {
        return new SurfaceSlot
        {
            renderer = renderer,
            materialIndex = materialIndex,
            previous = new MaterialPropertyBlock(),
            accent = new MaterialPropertyBlock()
        };
    }

    static void ReadSurfaceBlock(SurfaceSlot slot, MaterialPropertyBlock block)
    {
        if (slot.materialIndex < 0)
            slot.renderer.GetPropertyBlock(block);
        else
            slot.renderer.GetPropertyBlock(block, slot.materialIndex);
    }

    static void WriteSurfaceBlock(SurfaceSlot slot, MaterialPropertyBlock block)
    {
        if (slot.materialIndex < 0)
            slot.renderer.SetPropertyBlock(block);
        else
            slot.renderer.SetPropertyBlock(block, slot.materialIndex);
    }
}
