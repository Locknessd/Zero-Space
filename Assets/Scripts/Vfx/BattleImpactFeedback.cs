using FrankRetarget;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Participant contact holds and a localized comic flash. Never owns global game time.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(14600)]
public sealed partial class BattleImpactFeedback : MonoBehaviour
{
    public BattleVfxPlayer vfx;
    public Camera battleCamera;
    public BattleImpactFlashGraphic flash;
    [Range(0, 1)] public float flashScale = 1;
    [Range(0, .12f)] public float lightHold = .04f;
    [Range(0, .12f)] public float heavyHold = .08f;
    [Range(0, .12f)] public float groundHold = .055f;
    [Range(0, .2f)] public float finishingHold = .10f;
    [Header("KO slow motion")]
    public bool knockoutSlowMotion;
    [Range(.1f, .8f)] public float knockoutSpeed = .32f;
    [Min(.1f)] public float knockoutSeconds = 1.8f;
    [Min(.05f)] public float knockoutRecovery = .3f;
    public int HitStopCount { get; private set; }
    public bool IsHolding => HasLivePlayback && remaining > 0;
    public bool IsSlowing => slowAge < knockoutSeconds + knockoutRecovery;
    public int SlowMotionCount { get; private set; }
    public bool HasPlayedKnockoutSlowMotion => knockoutPlayed;
    public float RemainingHold => remaining;

    BattleVfxPlayer subscribed;
    FrankBattlePairPlayback clockPlayback;
    int clockPlaybackId = -1;
    bool knockoutPlayed;
    float remaining, flashAge = 1;
    float slowAge = float.PositiveInfinity;
    bool HasLivePlayback => clockPlayback && clockPlayback.Playing && clockPlayback.PlaybackId == clockPlaybackId;

    // Only this accepted pair may consume the presentation clock. UI, particles,
    // audio and menu pause retain their existing clocks. No static owner survives reload.
    public float PlaybackRate(FrankBattlePairPlayback playback)
    {
        if (!isActiveAndEnabled || !HasLivePlayback || clockPlayback != playback) return 1;
        if (IsHolding) return 0;
        if (!IsSlowing) return 1;
        float recovery = Mathf.SmoothStep(0, 1,
            Mathf.Clamp01((slowAge - knockoutSeconds) / Mathf.Max(.001f, knockoutRecovery)));
        return Mathf.Lerp(knockoutSpeed, 1, recovery);
    }

    void BeginSequence(CharacterCombat source, CombatTripletData move, bool lethal)
    {
        clockPlayback = source ? source.SourcePlayback : null;
        clockPlaybackId = clockPlayback ? clockPlayback.PlaybackId : -1;
        remaining = 0;
        slowAge = float.PositiveInfinity;
    }
    Vector3 flashPosition;
    const float FlashDuration = .13f;

    public void Bind()
    {
        if (subscribed == vfx) return;
        Unbind(); subscribed = vfx;
        if (!subscribed) return;
        subscribed.ContactOccurred += Contact;
        subscribed.SequenceBegan += BeginSequence;
        subscribed.EffectsCleared += ResetFeedback;
    }

    void Contact(BattleVfxPlayer.Impact impact)
    {
        float hold = impact.kind == BattleVfxPlayer.ContactKind.Heavy ? heavyHold :
            impact.kind == BattleVfxPlayer.ContactKind.Ground ? groundHold : lightHold;
        if (impact.finishing) hold = Mathf.Max(hold, finishingHold);
        if (hold > 0) HitStopCount++;
        if (hold > 0 && Application.isPlaying && Time.timeScale > 0 && impact.playback && impact.playback.Playing)
        {
            clockPlayback = impact.playback;
            clockPlaybackId = clockPlayback.PlaybackId;
            // Overlapping requests extend to the longest remaining hold, never add up.
            remaining = Mathf.Max(remaining, hold);
            if (impact.finishing) BeginKnockoutSlowMotion();
        }
        flashAge = 0; flashPosition = impact.position;
        HighlightDefender(impact.receiver);
        if (!flash) return;
        RefreshFlashProjection();
        float size = impact.kind == BattleVfxPlayer.ContactKind.Heavy ? 190 :
            impact.kind == BattleVfxPlayer.ContactKind.Ground ? 165 : 120;
        flash.rectTransform.sizeDelta = Vector2.one * size;
        flash.color = impact.kind == BattleVfxPlayer.ContactKind.Ground ? new Color(1, .83f, .32f, .8f) : new Color(1, .98f, .84f, .9f);
        var flashColor = flash.color;
        flashColor.a *= flashScale;
        flash.color = flashColor;
        flash.Progress = 0;
    }

    // Also covers a zero-HP/result event without a playable finishing contact.
    public void BeginKnockoutSlowMotion()
    {
        if (!isActiveAndEnabled || !Application.isPlaying || !knockoutSlowMotion || knockoutPlayed || Time.timeScale <= 0) return;
        knockoutPlayed = true; slowAge = 0; SlowMotionCount++;
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
        float delta = float.IsFinite(unscaledDelta) ? Mathf.Max(0, unscaledDelta) : 0;
        // Contact holds release on real time even while a menu is paused. Releasing a
        // local hold cannot resume that menu because Time.timeScale is never written.
        remaining = HasLivePlayback ? Mathf.Max(0, remaining - delta) : 0;
        // A paused menu does not consume the cinematic slow-motion envelope.
        if (!Application.isPlaying || Time.timeScale > 0)
        {
            slowAge += delta;
            flashAge += delta;
            AdvanceSurfaceHighlight(delta);
        }
        if (flash) flash.Progress = Mathf.Clamp01(flashAge / FlashDuration);
    }

    public void ResetFeedback()
    {
        clockPlayback = null; clockPlaybackId = -1; remaining = 0;
        slowAge = float.PositiveInfinity; flashAge = 1; knockoutPlayed = false;
        if (flash) flash.Progress = 1;
        ClearSurfaceHighlights();
    }

    void Unbind()
    {
        if (subscribed)
        {
            subscribed.ContactOccurred -= Contact;
            subscribed.SequenceBegan -= BeginSequence;
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
