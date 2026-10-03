using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Owns the battle HUD's text tweens and restores their layout on interruption.</summary>
[DisallowMultipleComponent]
public sealed class BattleUiTextEffects : MonoBehaviour
{
    public bool PreviewAnimations { get; set; }
    public int StartedAnimations { get; private set; }
    readonly Dictionary<Graphic, TextState> states = new Dictionary<Graphic, TextState>();

    sealed class TextState
    {
        public Graphic label;
        public Vector3 scale;
        public Vector2 position;
        public Color color;
        public Sequence animation;
    }

    bool CanAnimate => isActiveAndEnabled && (Application.isPlaying || PreviewAnimations);

    TextState Prepare(Graphic label)
    {
        if (!label || !CanAnimate) return null;
        if (!states.TryGetValue(label, out var state))
        {
            state = new TextState { label = label, scale = label.rectTransform.localScale,
                position = label.rectTransform.anchoredPosition, color = label.color };
            states.Add(label, state);
        }
        Restore(state);
        state.animation = DOTween.Sequence().SetUpdate(true).SetId(this)
            .SetLink(label.gameObject, LinkBehaviour.KillOnDisable);
        StartedAnimations++;
        return state;
    }

    public void Pop(Graphic label, bool damaged = false)
    {
        var state = Prepare(label);
        if (state == null) return;
        var rect = label.rectTransform;
        state.animation.Append(rect.DOScale(state.scale * (damaged ? 1.16f : 1.08f), .1f).SetEase(Ease.OutQuad))
            .Append(rect.DOScale(state.scale, .24f).SetEase(Ease.OutBack));
        if (damaged)
        {
            label.color = new Color(1, .3f, .15f, state.color.a);
            state.animation.Insert(0, label.DOColor(state.color, .34f));
        }
    }

    public void Reveal(Text label, string content)
    {
        content = content ?? string.Empty;
        var state = Prepare(label);
        if (state == null) { if (label) label.text = content; return; }
        label.text = string.Empty;
        if (content.Length == 0) { state.animation.Kill(); return; }
        state.animation.Append(label.DOText(content, Mathf.Clamp(content.Length * .018f, .14f, 1.1f), true)
            .SetEase(Ease.Linear));
    }

    public void Damage(Text label)
    {
        var state = Prepare(label);
        if (state == null) return;
        var rect = label.rectTransform;
        label.color = new Color(1, .65f, .12f, state.color.a);
        state.animation.Append(rect.DOScale(state.scale * 1.2f, .12f).SetEase(Ease.OutBack))
            .Append(rect.DOScale(state.scale, .2f).SetEase(Ease.OutQuad))
            .Insert(.1f, rect.DOAnchorPos(state.position + Vector2.up * 28, .85f).SetEase(Ease.OutCubic))
            .Insert(.55f, label.DOFade(0, .45f));
    }

    public void Cancel(Graphic label)
    {
        if (label && states.TryGetValue(label, out var state)) Restore(state);
    }

    static void Restore(TextState state)
    {
        state.animation?.Kill(false);
        state.animation = null;
        if (!state.label) return;
        state.label.rectTransform.localScale = state.scale;
        state.label.rectTransform.anchoredPosition = state.position;
        state.label.color = state.color;
    }

    public void ResetEffects()
    {
        foreach (var state in states.Values) Restore(state);
    }

    public void EvaluatePreview(float seconds)
    {
        if (!PreviewAnimations) return;
        foreach (var state in states.Values)
            if (state.animation != null && state.animation.IsActive()) state.animation.Goto(seconds, false);
    }

    void OnDisable() { ResetEffects(); }
    void OnDestroy() { ResetEffects(); }
}
