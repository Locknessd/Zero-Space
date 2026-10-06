using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Sizes the dialogue card from the complete line before its typewriter animation begins.</summary>
[DisallowMultipleComponent]
public sealed class BattleSpeechBubble : MonoBehaviour
{
    public RectTransform panel;
    public CanvasGroup group;
    public Text dialogue;
    public Text speaker;
    public float maxHeight = 250;
    public bool PreviewAnimations { get; set; }
    public string Content { get; private set; } = string.Empty;
    public bool IsVisible => group && group.alpha > .001f;
    readonly TextGenerator measure = new TextGenerator();
    Sequence animation;
    float width = 500;

    public void SetWidth(float value)
    {
        value = Mathf.Max(140, value);
        if (Mathf.Abs(width - value) < .5f) return;
        width = value;
        Resize();
    }

    public void Show(string content, string speakerName, bool animate = true)
    {
        Content = content ?? string.Empty;
        if (speaker) speaker.text = (speakerName ?? string.Empty).ToUpperInvariant();
        Resize();
        animation?.Kill(); animation = null;
        if (!group || !panel) return;
        group.blocksRaycasts = group.interactable = false;
        bool visible = !string.IsNullOrWhiteSpace(Content);
        if (!animate || !isActiveAndEnabled || !(Application.isPlaying || PreviewAnimations))
        {
            group.alpha = visible ? 1 : 0;
            panel.localScale = Vector3.one;
            return;
        }
        animation = DOTween.Sequence().SetUpdate(true).SetId(this).SetLink(gameObject, LinkBehaviour.KillOnDisable);
        if (visible)
        {
            panel.localScale = Vector3.one * .94f;
            animation.Append(group.DOFade(1, .15f))
                .Join(panel.DOScale(1, .24f).SetEase(Ease.OutBack));
        }
        else
            animation.Append(group.DOFade(0, .12f))
                .Join(panel.DOScale(.97f, .12f).SetEase(Ease.OutQuad));
    }

    void Resize()
    {
        if (!dialogue) return;
        var settings = dialogue.GetGenerationSettings(new Vector2(width - 48, 0));
        settings.resizeTextForBestFit = false;
        float textHeight = measure.GetPreferredHeight(Content, settings) / dialogue.pixelsPerUnit;
        float height = Mathf.Clamp(textHeight + 76, 118, maxHeight);
        var rect = (RectTransform)transform;
        rect.sizeDelta = new Vector2(width, height);
    }

    public void EvaluatePreview(float seconds)
    {
        if (PreviewAnimations && animation != null && animation.IsActive()) animation.Goto(seconds, false);
    }

    public void ResetPresentation()
    {
        animation?.Kill(); animation = null;
        Content = string.Empty;
        if (dialogue) dialogue.text = string.Empty;
        if (group) { group.alpha = 0; group.blocksRaycasts = group.interactable = false; }
        if (panel) panel.localScale = Vector3.one;
    }

    void OnDisable() => ResetPresentation();
    void OnDestroy() { animation?.Kill(); }
}
