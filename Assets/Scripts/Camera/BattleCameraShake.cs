using UnityEngine;

/// <summary>Adds a short contact impulse after the authored cinematic shot.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(14550)]
public sealed class BattleCameraShake : MonoBehaviour
{
    public BattleVfxPlayer vfx;
    [Range(0, .1f)] public float heavyStrength = .045f;
    [Range(0, .1f)] public float groundStrength = .035f;
    [Range(.05f, .4f)] public float duration = .2f;
    public int ShakeCount { get; private set; }
    public bool IsShaking => age < duration;
    BattleVfxPlayer subscribed;
    float age = 1, strength;
    Vector3 basePosition, appliedPosition;
    Quaternion baseRotation, appliedRotation;
    bool applied;
    Camera view;
    FrankRetarget.FrankCinematicCamera director;

    public void Bind()
    {
        if (subscribed == vfx) return;
        Unbind(); subscribed = vfx;
        if (subscribed) { subscribed.EffectPlayed += Contact; subscribed.EffectsCleared += ResetShake; }
    }
    void Contact(string cue, GameObject effect)
    {
        if (!effect || cue != "heavy_hit" && cue != "ground_impact") return;
        strength = Mathf.Max(strength * Mathf.Clamp01(1 - age / duration), cue == "heavy_hit" ? heavyStrength : groundStrength);
        age = 0; ShakeCount++;
    }
    public void AdvanceShake(float unscaledDelta) => age += Mathf.Max(0, unscaledDelta);
    public void ApplyShake()
    {
        RemoveOffset();
        if (!IsShaking) { strength = 0; return; }
        basePosition = transform.position; baseRotation = transform.rotation;
        float envelope = Mathf.Pow(1 - Mathf.Clamp01(age / duration), 2);
        float phase = age * 95;
        Vector3 offset = new Vector3(Mathf.Sin(phase + ShakeCount * .9f), Mathf.Sin(phase * 1.35f + 1.2f) * .55f, 0) * (strength * envelope);
        if (!view) view = GetComponent<Camera>();
        if (!director) director = GetComponent<FrankRetarget.FrankCinematicCamera>();
        float roll = Mathf.Sin(phase * 1.1f) * envelope * strength * 8;
        float fit = 1;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            transform.position = basePosition + baseRotation * offset * fit;
            transform.rotation = baseRotation * Quaternion.Euler(0, 0, roll * fit);
            bool safe = true;
            if (view && director)
                foreach (var point in director.LastFramingPoints)
                {
                    var screen = view.WorldToViewportPoint(point);
                    if (screen.x < .05f || screen.x > .95f || screen.y < .05f || screen.y > .95f) { safe = false; break; }
                }
            if (safe) break;
            fit = attempt == 4 ? 0 : fit * .5f;
        }
        appliedPosition = transform.position; appliedRotation = transform.rotation; applied = true;
    }
    void RemoveOffset()
    {
        if (!applied) return;
        // A cinematic camera may already have written the next base pose.
        if ((transform.position - appliedPosition).sqrMagnitude < .0000001f && Quaternion.Angle(transform.rotation, appliedRotation) < .001f)
            transform.SetPositionAndRotation(basePosition, baseRotation);
        applied = false;
    }
    public void ResetShake() { RemoveOffset(); age = duration; strength = 0; }
    void Unbind()
    {
        if (subscribed) { subscribed.EffectPlayed -= Contact; subscribed.EffectsCleared -= ResetShake; }
        subscribed = null;
    }
    void OnEnable() => Bind();
    void Update() { RemoveOffset(); AdvanceShake(Time.unscaledDeltaTime); }
    void LateUpdate() => ApplyShake();
    void OnDisable() { Unbind(); ResetShake(); }
    void OnDestroy() { Unbind(); ResetShake(); }
}
