using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Queues KO at zero HP and shows it after the finishing animation and slow motion.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(15000)]
public sealed class BattleKoPresentation : MonoBehaviour
{
    public GameManager game;
    public CanvasGroup group;
    public Image backdrop, flash;
    public RectTransform word, letterK, letterO, band;
    public Text subtitle, defeatedLabel;
    public BattleKoBurstGraphic burst;
    [Min(.5f)] public float holdSeconds = 1.8f;
    public bool PreviewAnimations { get; set; }
    public bool HasShown { get; private set; }
    public bool IsShowing { get; private set; }
    public int ShowCount { get; private set; }
    public bool Pending => pending;

    Sequence animation;
    bool pending, cached;
    MemeBattleUI.Side pendingSide;
    string pendingName;
    Vector2 kHome, oHome, wordHome, subtitleHome;

    public void ObserveHealth(MemeBattleUI.Side side, long hp, string fighterName)
    {
        if (hp > 0)
        {
            if (pending && pendingSide == side) pending = false;
            return;
        }
        if (HasShown) return;
        pending = true;
        pendingSide = side;
        pendingName = fighterName;
        // Slow motion belongs to the finishing contact, independently of the later KO panel.
        if (game) game.GetComponent<BattleImpactFeedback>()?.BeginKnockoutSlowMotion();
    }

    void LateUpdate()
    {
        if (!pending || HasShown || !game) return;
        Present(pendingSide, pendingName);
    }

    bool FinisherFinished()
    {
        if (!game) return true;
        var feedback = game.GetComponent<BattleImpactFeedback>();
        if (feedback && feedback.isActiveAndEnabled)
        {
            // Retry a result-only KO once an external pause is released; the feedback guards duplicates.
            feedback.BeginKnockoutSlowMotion();
            if (feedback.IsHolding || feedback.IsSlowing ||
                feedback.knockoutSlowMotion && !feedback.HasPlayedKnockoutSlowMotion) return false;
        }
        return !(game.leftCombat && game.leftCombat.IsBusy || game.rightCombat && game.rightCombat.IsBusy);
    }

    void CacheLayout()
    {
        if (cached) return;
        kHome = letterK.anchoredPosition;
        oHome = letterO.anchoredPosition;
        wordHome = word.anchoredPosition;
        subtitleHome = subtitle.rectTransform.anchoredPosition;
        cached = true;
    }

    public bool Present(MemeBattleUI.Side side, string fighterName)
    {
        if (HasShown || !isActiveAndEnabled || (!Application.isPlaying && !PreviewAnimations)) return false;
        if (Application.isPlaying)
        {
            ObserveHealth(side, 0, fighterName);
            if (!FinisherFinished()) return false;
        }
        CacheLayout();
        animation?.Kill(false);
        pending = false;
        HasShown = IsShowing = true;
        ShowCount++;
        group.alpha = 1;
        group.interactable = group.blocksRaycasts = false;
        backdrop.color = new Color(.025f, .008f, .018f, 0);
        flash.color = new Color(1, .72f, .2f, .38f);
        word.localScale = Vector3.one;
        word.anchoredPosition = wordHome;
        letterK.localScale = letterO.localScale = Vector3.one * 2.25f;
        letterK.anchoredPosition = kHome + Vector2.left * 180;
        letterO.anchoredPosition = oHome + Vector2.right * 180;
        band.localScale = new Vector3(.02f, 1, 1);
        subtitle.color = new Color(1, .88f, .64f, 0);
        subtitle.rectTransform.anchoredPosition = subtitleHome - Vector2.up * 16;
        defeatedLabel.text = (string.IsNullOrWhiteSpace(fighterName) ?
            (side == MemeBattleUI.Side.Left ? "LEFT FIGHTER" : "RIGHT FIGHTER") : fighterName.ToUpperInvariant()) + "  /  0 HP";
        defeatedLabel.color = new Color(1, .72f, .43f, 0);
        burst.Progress = 0;

        animation = DOTween.Sequence().SetUpdate(true).SetId(this).SetLink(gameObject, LinkBehaviour.KillOnDisable);
        animation.InsertCallback(.25f, () => { if (Application.isPlaying && game && game.battleSfx) game.battleSfx.PlayKnockoutOnce(); });
        animation.Insert(0, backdrop.DOFade(.56f, .2f))
            .Insert(0, flash.DOFade(0, .3f))
            .Insert(0, band.DOScaleX(1, .24f).SetEase(Ease.OutExpo))
            .Insert(0, letterK.DOAnchorPos(kHome, .28f).SetEase(Ease.OutExpo))
            .Insert(0, letterK.DOScale(1, .3f).SetEase(Ease.OutBack))
            .Insert(.045f, letterO.DOAnchorPos(oHome, .28f).SetEase(Ease.OutExpo))
            .Insert(.045f, letterO.DOScale(1, .3f).SetEase(Ease.OutBack))
            .Insert(.25f, word.DOShakeAnchorPos(.28f, new Vector2(10, 6), 14, 90, false, true))
            .Insert(.3f, subtitle.DOFade(1, .2f))
            .Insert(.3f, subtitle.rectTransform.DOAnchorPos(subtitleHome, .2f).SetEase(Ease.OutQuad))
            .Insert(.4f, defeatedLabel.DOFade(1, .2f))
            .Insert(.12f, DOTween.To(() => burst.Progress, value => burst.Progress = value, 1, .9f).SetEase(Ease.OutCubic))
            .Insert(.45f + holdSeconds, group.DOFade(0, .35f))
            .Insert(.45f + holdSeconds, word.DOScale(.93f, .35f).SetEase(Ease.InQuad))
            .OnComplete(() => IsShowing = false);
        return true;
    }

    public void ResetPresentation()
    {
        animation?.Kill(false);
        animation = null;
        pending = HasShown = IsShowing = false;
        ShowCount = 0;
        if (group) { group.alpha = 0; group.interactable = group.blocksRaycasts = false; }
        if (burst) burst.Progress = 1;
        if (!cached) return;
        // UI children can be destroyed before the HUD's OnDisable during scene teardown.
        if (letterK) { letterK.anchoredPosition = kHome; letterK.localScale = Vector3.one; }
        if (letterO) { letterO.anchoredPosition = oHome; letterO.localScale = Vector3.one; }
        if (word) { word.anchoredPosition = wordHome; word.localScale = Vector3.one; }
        if (band) band.localScale = Vector3.one;
        if (subtitle) subtitle.rectTransform.anchoredPosition = subtitleHome;
    }

    public void EvaluatePreview(float seconds)
    {
        if (PreviewAnimations && animation != null && animation.IsActive()) animation.Goto(seconds, false);
    }

    void Awake() { ResetPresentation(); }
    void OnDisable() { ResetPresentation(); }
    void OnDestroy() { animation?.Kill(false); }
}
