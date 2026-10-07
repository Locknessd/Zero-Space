using UnityEngine;

/// <summary>Adds a bounded directional contact impulse after the cinematic shot.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(14550)]
public sealed class BattleCameraShake : MonoBehaviour
{
    public BattleVfxPlayer vfx;
    [Header("Motion preference")]
    [Range(0, 1)] public float motionScale = 1;
    [Header("Displacement at 1080 pixel screen height")]
    public bool screenSpaceImpulse = true;
    [Range(0, 12)] public float lightPixels = 2;
    [Range(0, 12)] public float heavyPixels = 6;
    [Range(0, 12)] public float groundPixels = 4;
    [Range(0, 12)] public float finishingPixels = 9;
    [Range(0, 12)] public float maximumPixels = 9;
    [Tooltip("Maximum roll in degrees. Zero keeps the horizon stable.")]
    [Range(0, .5f)] public float rollDegrees;
    [Header("Legacy world displacement when screen space is disabled")]
    [Range(0, .1f)] public float lightStrength = .014f;
    [Range(0, .1f)] public float heavyStrength = .055f;
    [Range(0, .1f)] public float groundStrength = .04f;
    [Header("Envelope in real seconds")]
    [Range(.05f, .4f)] public float lightDuration = .09f;
    [Range(.05f, .4f)] public float duration = .18f;
    public int ShakeCount { get; private set; }
    public bool IsShaking => age < activeDuration;
    public Vector2 ImpulseDirection => direction;
    public Vector3 ImpulsePosition => contactPosition;
    BattleVfxPlayer subscribed;
    float age = 1, strength, activeDuration = .18f, frequency = 23;
    Vector2 direction = Vector2.right;
    Vector3 contactPosition, basePosition, appliedPosition;
    Quaternion baseRotation, appliedRotation;
    bool applied;
    Camera view;
    FrankRetarget.FrankCinematicCamera director;

    public void Bind()
    {
        if (!view) view = GetComponent<Camera>();
        if (!director) director = GetComponent<FrankRetarget.FrankCinematicCamera>();
        if (subscribed == vfx) return;
        Unbind();
        subscribed = vfx;
        if (!subscribed) return;
        subscribed.ContactOccurred += Contact;
        subscribed.EffectsCleared += ResetShake;
    }

    void Contact(BattleVfxPlayer.Impact impact)
    {
        if (motionScale <= 0) return;
        float impulse = screenSpaceImpulse
            ? Mathf.Min(maximumPixels, impact.finishing ? finishingPixels :
                impact.kind == BattleVfxPlayer.ContactKind.Heavy ? heavyPixels :
                impact.kind == BattleVfxPlayer.ContactKind.Ground ? groundPixels : lightPixels)
            : impact.kind == BattleVfxPlayer.ContactKind.Heavy ? heavyStrength :
                impact.kind == BattleVfxPlayer.ContactKind.Ground ? groundStrength : lightStrength;
        if (impulse <= 0) return;
        if (view)
        {
            var viewport = view.WorldToViewportPoint(impact.position);
            if (viewport.z <= view.nearClipPlane) impulse *= .25f;
            else if (viewport.x < 0 || viewport.x > 1 || viewport.y < 0 || viewport.y > 1) impulse *= .25f;
        }
        float residual = strength * Envelope();
        bool carryingStronger = IsShaking && residual > impulse;
        strength = Mathf.Max(residual, impulse);
        if (!carryingStronger)
        {
            bool light = impact.kind == BattleVfxPlayer.ContactKind.Light && !impact.finishing;
            activeDuration = Mathf.Max(.01f, light ? lightDuration : duration);
            contactPosition = impact.position;
            frequency = impact.kind == BattleVfxPlayer.ContactKind.Ground ? 11 : 23;
            Vector3 strike = impact.attacker && impact.receiver
                ? impact.receiver.transform.position - impact.attacker.transform.position : transform.right;
            Vector3 local = transform.InverseTransformDirection(strike);
            direction = impact.kind == BattleVfxPlayer.ContactKind.Ground
                ? Vector2.down : new Vector2(local.x, local.y * .5f);
            if (direction.sqrMagnitude < .001f) direction = Vector2.right;
            direction.Normalize();
        }
        age = 0;
        ShakeCount++;
    }

    float Envelope() => Mathf.Pow(1 - Mathf.Clamp01(age / Mathf.Max(.01f, activeDuration)), 2);

    float WorldUnitsPerReferencePixel()
    {
        if (!view) return .004f;
        if (view.orthographic) return 2 * view.orthographicSize / 1080;
        float depth = Mathf.Max(view.nearClipPlane,
            Vector3.Dot(contactPosition - transform.position, transform.forward));
        return 2 * depth * Mathf.Tan(view.fieldOfView * Mathf.Deg2Rad * .5f) / 1080;
    }

    public void AdvanceShake(float unscaledDelta)
    {
        if (float.IsFinite(unscaledDelta)) age += Mathf.Max(0, unscaledDelta);
    }

    public void ApplyShake()
    {
        RemoveOffset();
        if (!IsShaking || motionScale <= 0)
        {
            strength = 0;
            return;
        }
        basePosition = transform.position;
        baseRotation = transform.rotation;
        float envelope = Envelope();
        float phase = age * frequency * Mathf.PI * 2;
        float kick = Mathf.Cos(phase) * envelope;
        Vector2 perpendicular = new Vector2(-direction.y, direction.x);
        Vector2 motion = direction * kick + perpendicular * (Mathf.Sin(phase) * envelope * .12f);
        float amplitude = strength * motionScale * (screenSpaceImpulse ? WorldUnitsPerReferencePixel() : 1);
        Vector3 offset = new Vector3(motion.x, motion.y, 0) * amplitude;
        float roll = Mathf.Sin(phase) * envelope * rollDegrees * motionScale;
        float fit = 1;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            transform.position = basePosition + baseRotation * offset * fit;
            transform.rotation = baseRotation * Quaternion.Euler(0, 0, roll * fit);
            bool safe = true;
            if (view && director)
            {
                foreach (var point in director.LastFramingPoints)
                {
                    var screen = view.WorldToViewportPoint(point);
                    if (screen.z > view.nearClipPlane && screen.x >= .05f && screen.x <= .95f &&
                        screen.y >= .05f && screen.y <= .95f) continue;
                    safe = false;
                    break;
                }
            }
            if (safe) break;
            fit = attempt == 4 ? 0 : fit * .5f;
        }
        appliedPosition = transform.position;
        appliedRotation = transform.rotation;
        applied = true;
    }

    void RemoveOffset()
    {
        if (!applied) return;
        // The cinematic camera may already have written the next base pose.
        if ((transform.position - appliedPosition).sqrMagnitude < .0000001f &&
            Quaternion.Angle(transform.rotation, appliedRotation) < .001f)
            transform.SetPositionAndRotation(basePosition, baseRotation);
        applied = false;
    }

    public void ResetShake()
    {
        RemoveOffset();
        age = activeDuration;
        strength = 0;
    }

    void Unbind()
    {
        if (subscribed)
        {
            subscribed.ContactOccurred -= Contact;
            subscribed.EffectsCleared -= ResetShake;
        }
        subscribed = null;
    }

    void OnEnable() => Bind();
    void Update()
    {
        RemoveOffset();
        AdvanceShake(Time.unscaledDeltaTime);
    }
    void LateUpdate() => ApplyShake();
    void OnDisable()
    {
        Unbind();
        ResetShake();
    }
    void OnDestroy()
    {
        Unbind();
        ResetShake();
    }
}
