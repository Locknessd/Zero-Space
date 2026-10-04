using UnityEngine;

/// <summary>Battle lighting and short, pooled light pulses on the actual VFX cue clock.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(14500)]
public sealed class BattleLightingRig : MonoBehaviour
{
    public GameManager battle;
    public Camera battleCamera;
    public BattleVfxPlayer vfx;
    public Light fill;
    public Light rim;
    public Light key;
    public bool combatDimming;
    [Range(.5f, 1)] public float combatExposure = .82f;
    public Light[] impactLights = new Light[2];
    [Range(0, 2)] public float impactIntensity = 1.1f;

    public int PlayedFlashCount { get; private set; }
    public int ActiveFlashCount
    {
        get
        {
            int count = 0;
            foreach (var light in impactLights)
                if (light && light.enabled && light.intensity > .001f) count++;
            return count;
        }
    }

    sealed class Pulse
    {
        public Light light;
        public GameObject effect;
        public float elapsed, duration, peak;
    }

    Pulse[] pulses;
    BattleVfxPlayer subscribed;
    bool qualityOwned;
    int previousPixelLights;
    ShadowQuality previousShadows;
    float previousShadowDistance;
    ShadowResolution previousResolution;
    float keyIntensity, fillIntensity, rimIntensity, combatBlend;
    Renderer[] backdropRenderers;
    MaterialPropertyBlock[] backdropBefore, backdropDimmed;
    float[] backdropExposure;
    bool capturedLighting;

    public void InitializeRig()
    {
        if (!capturedLighting)
        {
            keyIntensity = key ? key.intensity : 0; fillIntensity = fill ? fill.intensity : 0; rimIntensity = rim ? rim.intensity : 0;
            var renderers = new System.Collections.Generic.List<Renderer>();
            var exposure = new System.Collections.Generic.List<float>();
            foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.sharedMaterial || !renderer.sharedMaterial.HasProperty("_Exposure")) continue;
                renderers.Add(renderer); exposure.Add(renderer.sharedMaterial.GetFloat("_Exposure"));
            }
            backdropRenderers = renderers.ToArray(); backdropExposure = exposure.ToArray();
            backdropBefore = new MaterialPropertyBlock[renderers.Count]; backdropDimmed = new MaterialPropertyBlock[renderers.Count];
            for (int i = 0; i < renderers.Count; i++)
            {
                backdropBefore[i] = new MaterialPropertyBlock(); renderers[i].GetPropertyBlock(backdropBefore[i]);
                backdropDimmed[i] = new MaterialPropertyBlock(); renderers[i].GetPropertyBlock(backdropDimmed[i]);
            }
            capturedLighting = true;
        }
        if (!qualityOwned)
        {
            previousPixelLights = QualitySettings.pixelLightCount;
            previousShadows = QualitySettings.shadows;
            previousShadowDistance = QualitySettings.shadowDistance;
            previousResolution = QualitySettings.shadowResolution;
            QualitySettings.pixelLightCount = Mathf.Max(5, previousPixelLights);
            QualitySettings.shadows = ShadowQuality.All;
            QualitySettings.shadowDistance = Mathf.Max(18, previousShadowDistance);
            QualitySettings.shadowResolution = ShadowResolution.Medium;
            qualityOwned = true;
        }
        if (subscribed != vfx)
        {
            Unsubscribe();
            subscribed = vfx;
            if (subscribed)
            {
                subscribed.EffectPlayed += OnEffect;
                subscribed.EffectsCleared += ClearFlashes;
            }
        }
        if (pulses == null || pulses.Length != impactLights.Length)
        {
            pulses = new Pulse[impactLights.Length];
            for (int i = 0; i < pulses.Length; i++)
                pulses[i] = new Pulse { light = impactLights[i] };
            ClearFlashes();
        }
        for (int i = 0; i < pulses.Length; i++) pulses[i].light = impactLights[i];
        RefreshLighting(0);
    }

    void OnEnable() => InitializeRig();

    void OnEffect(string cue, GameObject effect)
    {
        if (!effect || pulses == null || pulses.Length == 0) return;
        bool heavy = cue == "heavy_hit";
        bool ground = cue == "ground_impact";
        if (!heavy && !ground && cue != "light_hit") return;
        Pulse selected = null;
        foreach (var pulse in pulses)
        {
            if (!pulse.light) continue;
            if (!pulse.light.enabled) { selected = pulse; break; }
            if (selected == null || pulse.elapsed / pulse.duration > selected.elapsed / selected.duration)
                selected = pulse;
        }
        if (selected == null) return;
        Vector3 toCamera = battleCamera ? (battleCamera.transform.position - effect.transform.position).normalized : Vector3.forward;
        selected.light.transform.position = effect.transform.position + Vector3.up * (ground ? .45f : .1f) + toCamera * .35f;
        selected.light.color = ground ? new Color(1, .62f, .18f) : heavy ? new Color(.45f, .78f, 1) : new Color(1, .85f, .5f);
        selected.elapsed = 0;
        selected.duration = ground ? .28f : heavy ? .22f : .15f;
        selected.peak = impactIntensity * (heavy ? 1 : ground ? .75f : .55f);
        selected.effect = effect;
        selected.light.intensity = selected.peak;
        selected.light.enabled = true;
        PlayedFlashCount++;
    }

    // Also usable by the editor to preview a deterministic animation pose and effect age.
    public void RefreshLighting(float deltaTime)
    {
        bool fighting = combatDimming && battle && (battle.leftCombat && battle.leftCombat.IsBusy || battle.rightCombat && battle.rightCombat.IsBusy);
        combatBlend = Mathf.MoveTowards(combatBlend, fighting ? 1 : 0, Mathf.Max(0, deltaTime) / (fighting ? .18f : .3f));
        float exposure = Mathf.Lerp(1, combatExposure, combatBlend);
        if (key) key.intensity = keyIntensity * Mathf.Lerp(1, .9f, combatBlend);
        if (fill) fill.intensity = fillIntensity * exposure;
        if (rim) rim.intensity = rimIntensity * Mathf.Lerp(1, .92f, combatBlend);
        if (backdropRenderers != null)
            for (int i = 0; i < backdropRenderers.Length; i++)
                if (backdropRenderers[i])
                {
                    backdropDimmed[i].SetFloat("_Exposure", backdropExposure[i] * exposure);
                    backdropRenderers[i].SetPropertyBlock(backdropDimmed[i]);
                }
        if (battle && battle.leftCombat && battle.rightCombat)
        {
            Vector3 left = BodyPosition(battle.leftCombat);
            Vector3 right = BodyPosition(battle.rightCombat);
            Vector3 center = (left + right) * .5f;
            if (fill) fill.transform.position = center + new Vector3(-.6f, .65f, 2.2f);
            if (rim) rim.transform.position = center + new Vector3(.8f, 1.2f, -1.6f);
        }
        if (pulses == null) return;
        foreach (var pulse in pulses)
        {
            if (!pulse.light || !pulse.light.enabled) continue;
            pulse.elapsed += Mathf.Max(0, deltaTime);
            if (!pulse.effect || !pulse.effect.activeInHierarchy || pulse.elapsed >= pulse.duration)
            {
                pulse.light.intensity = 0;
                pulse.light.enabled = false;
                pulse.effect = null;
                continue;
            }
            float fade = 1 - pulse.elapsed / pulse.duration;
            pulse.light.intensity = pulse.peak * fade * fade;
        }
    }

    static Vector3 BodyPosition(CharacterCombat fighter)
    {
        var animator = fighter.Animator;
        var hips = animator && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        return hips ? hips.position : fighter.transform.position + Vector3.up;
    }

    void LateUpdate() => RefreshLighting(Time.deltaTime);

    public void ClearFlashes()
    {
        foreach (var light in impactLights)
        {
            if (!light) continue;
            light.intensity = 0;
            light.enabled = false;
        }
        if (pulses == null) return;
        foreach (var pulse in pulses) pulse.effect = null;
    }

    void Unsubscribe()
    {
        if (subscribed)
        {
            subscribed.EffectPlayed -= OnEffect;
            subscribed.EffectsCleared -= ClearFlashes;
        }
        subscribed = null;
    }

    void OnDisable() => ShutdownRig();

    public void ShutdownRig()
    {
        Unsubscribe();
        ClearFlashes();
        if (capturedLighting)
        {
            if (key) key.intensity = keyIntensity; if (fill) fill.intensity = fillIntensity; if (rim) rim.intensity = rimIntensity;
            for (int i = 0; i < backdropRenderers.Length; i++)
                if (backdropRenderers[i]) backdropRenderers[i].SetPropertyBlock(backdropBefore[i]);
            capturedLighting = false; combatBlend = 0;
        }
        if (!qualityOwned) return;
        QualitySettings.pixelLightCount = previousPixelLights;
        QualitySettings.shadows = previousShadows;
        QualitySettings.shadowDistance = previousShadowDistance;
        QualitySettings.shadowResolution = previousResolution;
        qualityOwned = false;
    }
}
