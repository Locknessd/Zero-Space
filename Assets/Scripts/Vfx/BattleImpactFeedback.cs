using UnityEngine;
using UnityEngine.UI;

/// <summary>Short contact holds and a localized comic flash. Camera settings are untouched.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(14600)]
public sealed class BattleImpactFeedback : MonoBehaviour
{
    public BattleVfxPlayer vfx;
    public Camera battleCamera;
    public BattleImpactFlashGraphic flash;
    [Range(0, .12f)] public float lightHold = .04f;
    [Range(0, .12f)] public float heavyHold = .08f;
    [Range(0, .12f)] public float groundHold = .055f;
    [Range(0, .12f)] public float finishingHold = .10f;
    [Header("KO slow motion")]
    public bool knockoutSlowMotion;
    [Range(.1f, .8f)] public float knockoutSpeed = .32f;
    [Min(.1f)] public float knockoutSeconds = 1.8f;
    [Min(.05f)] public float knockoutRecovery = .3f;
    public int HitStopCount { get; private set; }
    public bool IsHolding => ownsTime && remaining > 0;
    public bool IsSlowing => ownsTime && slowAge < knockoutSeconds + knockoutRecovery;
    public int SlowMotionCount { get; private set; }
    public bool HasPlayedKnockoutSlowMotion => knockoutPlayed;
    public float RemainingHold => remaining;

    BattleVfxPlayer subscribed;
    bool ownsTime;
    bool knockoutPlayed;
    float savedTimeScale, remaining, flashAge = 1;
    float slowAge = float.PositiveInfinity, appliedScale;
    Vector3 flashPosition;
    const float FlashDuration = .13f;

    public void Bind()
    {
        if (subscribed == vfx) return;
        Unbind(); subscribed = vfx;
        if (!subscribed) return;
        subscribed.ContactOccurred += Contact;
        subscribed.EffectsCleared += ResetFeedback;
    }

    void Contact(BattleVfxPlayer.Impact impact)
    {
        float hold = impact.kind == BattleVfxPlayer.ContactKind.Heavy ? heavyHold :
            impact.kind == BattleVfxPlayer.ContactKind.Ground ? groundHold : lightHold;
        if (impact.finishing) hold = Mathf.Max(hold, finishingHold);
        if (hold > 0) HitStopCount++;
        if (hold > 0 && Application.isPlaying && (ownsTime || Time.timeScale > 0))
        {
            if (!ownsTime) { savedTimeScale = Time.timeScale; ownsTime = true; }
            remaining = Mathf.Max(remaining, hold);
            if (impact.finishing) BeginKnockoutSlowMotion();
            ApplyTime();
        }
        flashAge = 0; flashPosition = impact.position;
        if (!flash) return;
        RefreshFlashProjection();
        float size = impact.kind == BattleVfxPlayer.ContactKind.Heavy ? 190 :
            impact.kind == BattleVfxPlayer.ContactKind.Ground ? 165 : 120;
        flash.rectTransform.sizeDelta = Vector2.one * size;
        flash.color = impact.kind == BattleVfxPlayer.ContactKind.Ground ? new Color(1, .83f, .32f, .8f) : new Color(1, .98f, .84f, .9f);
        flash.Progress = 0;
    }

    // Also covers a zero-HP/result event without a playable finishing contact.
    public void BeginKnockoutSlowMotion()
    {
        if (!isActiveAndEnabled || !Application.isPlaying || !knockoutSlowMotion || knockoutPlayed || !ownsTime && Time.timeScale <= 0) return;
        if (!ownsTime) { savedTimeScale = Time.timeScale; ownsTime = true; }
        knockoutPlayed = true; slowAge = 0; SlowMotionCount++; ApplyTime();
    }

    // Reproject after the existing cinematic camera has evaluated its current shot.
    public void RefreshFlashProjection()
    {
        if (!flash || flashAge >= FlashDuration) return;
        var host = flash.rectTransform.parent as RectTransform;
        var canvas = flash.canvas;
        Camera uiCamera = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector3 screen = RectTransformUtility.WorldToScreenPoint(battleCamera, flashPosition);
        if (host && RectTransformUtility.ScreenPointToLocalPointInRectangle(host, screen, uiCamera, out var point))
            flash.rectTransform.anchoredPosition = point;
    }

    public void AdvanceFeedback(float unscaledDelta)
    {
        if (ownsTime)
        {
            // Do not overwrite an explicit speed change made by another system.
            if (!Mathf.Approximately(Time.timeScale, appliedScale))
            { ownsTime = false; remaining = 0; slowAge = float.PositiveInfinity; }
            else
            {
                remaining = Mathf.Max(0, remaining - Mathf.Max(0, unscaledDelta));
                slowAge += Mathf.Max(0, unscaledDelta);
                if (remaining <= 0 && slowAge >= knockoutSeconds + knockoutRecovery) RestoreTime();
                else ApplyTime();
            }
        }
        flashAge += Mathf.Max(0, unscaledDelta);
        if (flash) flash.Progress = Mathf.Clamp01(flashAge / FlashDuration);
    }

    void RestoreTime()
    {
        if (ownsTime && Mathf.Approximately(Time.timeScale, appliedScale)) Time.timeScale = savedTimeScale;
        ownsTime = false; remaining = 0; slowAge = float.PositiveInfinity;
    }

    void ApplyTime()
    {
        float recovery = Mathf.SmoothStep(0, 1, Mathf.Clamp01((slowAge - knockoutSeconds) / knockoutRecovery));
        float factor = float.IsPositiveInfinity(slowAge) ? 1 : Mathf.Lerp(knockoutSpeed, 1, recovery);
        appliedScale = remaining > 0 ? 0 : savedTimeScale * factor;
        Time.timeScale = appliedScale;
    }

    public void ResetFeedback()
    {
        RestoreTime(); flashAge = 1; knockoutPlayed = false;
        if (flash) flash.Progress = 1;
    }

    void Unbind()
    {
        if (subscribed)
        {
            subscribed.ContactOccurred -= Contact;
            subscribed.EffectsCleared -= ResetFeedback;
        }
        subscribed = null;
    }
    void OnEnable() => Bind();
    void Update() => AdvanceFeedback(Time.unscaledDeltaTime);
    void LateUpdate() => RefreshFlashProjection();
    void OnDisable() { Unbind(); ResetFeedback(); }
    void OnDestroy() { Unbind(); ResetFeedback(); }
}
