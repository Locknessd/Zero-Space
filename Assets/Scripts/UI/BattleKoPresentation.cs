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
    public BattleKoInkGraphic ink;
    [Min(.5f)] public float holdSeconds = 1.05f;
    public bool PreviewAnimations { get; set; }
    public bool HasShown { get; private set; }
    public bool IsShowing { get; private set; }
    public int ShowCount { get; private set; }
    public bool Pending => pending;

    Sequence animation;
    const float LetterFlightSeconds = .18f;
    const float KnockoutImpactSeconds = .29f;
    bool pending, cached;
    MemeBattleUI.Side pendingSide;
    string pendingName;
    Vector2 kHome, oHome, wordHome, subtitleHome;
    Vector3 kHomeScale, oHomeScale;

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
        if (IsShowing)
            FitWordToCanvas();
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

    void FitWordToCanvas()
    {
        if (!word || !(transform is RectTransform root) || root.rect.width <= 0)
            return;
        float angle = word.localEulerAngles.z * Mathf.Deg2Rad;
        float cosine = Mathf.Abs(Mathf.Cos(angle));
        float sine = Mathf.Abs(Mathf.Sin(angle));
        float width = word.rect.width * cosine + word.rect.height * sine;
        float height = word.rect.height * cosine + word.rect.width * sine;
        float fit = Mathf.Clamp01(Mathf.Min((root.rect.width - 64) / Mathf.Max(1, width),
            (root.rect.height - 220) / Mathf.Max(1, height)));
        word.localScale = Vector3.one * fit;
    }

    void OnRectTransformDimensionsChange()
    {
        if (IsShowing)
            FitWordToCanvas();
    }

    void CacheLayout()
    {
        if (cached) return;
        kHome = letterK.anchoredPosition;
        oHome = letterO.anchoredPosition;
        kHomeScale = letterK.localScale;
        oHomeScale = letterO.localScale;
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
        backdrop.color = new Color(.025f, .018f, .045f, 0);
        flash.color = new Color(1, .94f, .76f, 0);
        Canvas.ForceUpdateCanvases();
        FitWordToCanvas();
        word.anchoredPosition = wordHome;
        var kGraphic = letterK.GetComponent<Graphic>();
        var oGraphic = letterO.GetComponent<Graphic>();
        kGraphic.color = oGraphic.color = new Color(1, 1, 1, 0);
        subtitle.color = new Color(1, .97f, .78f, 0);
        subtitle.rectTransform.anchoredPosition = subtitleHome + Vector2.up * 8;
        subtitle.rectTransform.localScale = Vector3.one;
        defeatedLabel.text = (string.IsNullOrWhiteSpace(fighterName) ?
            (side == MemeBattleUI.Side.Left ? "LEFT FIGHTER" : "RIGHT FIGHTER") :
            fighterName.ToUpperInvariant()) + "  /  0 HP";
        defeatedLabel.color = new Color(1, .9f, .65f, 0);
        if (ink)
        {
            ink.Progress = 0;
            ink.rectTransform.localScale = Vector3.one * 1.28f;
            if (band)
                band.gameObject.SetActive(false);
            if (burst)
            {
                burst.gameObject.SetActive(true);
                burst.Progress = 1;
            }
        }

        animation = DOTween.Sequence().SetUpdate(true).SetId(this).SetLink(gameObject, LinkBehaviour.KillOnDisable);
        animation.InsertCallback(KnockoutImpactSeconds, () =>
        {
            if (Application.isPlaying && game && game.battleSfx)
                game.battleSfx.PlayKnockoutOnce();
        });
        InsertLetterFlight(letterK, kGraphic, kHome, kHomeScale, .01f, 6.6f, -4);
        InsertLetterFlight(letterO, oGraphic, oHome, oHomeScale,
            KnockoutImpactSeconds - LetterFlightSeconds, 7.4f, 4);
        animation.Insert(0, backdrop.DOFade(.22f, .12f))
            .Insert(KnockoutImpactSeconds - .016f, flash.DOFade(.5f, .016f).SetEase(Ease.Linear))
            .Insert(KnockoutImpactSeconds, flash.DOFade(0, .09f).SetEase(Ease.OutQuad))
            .Insert(KnockoutImpactSeconds + .04f, subtitle.DOFade(1, .06f))
            .Insert(KnockoutImpactSeconds + .04f,
                subtitle.rectTransform.DOAnchorPos(subtitleHome, .08f).SetEase(Ease.OutQuad))
            .Insert(KnockoutImpactSeconds + .13f, defeatedLabel.DOFade(1, .14f))
            .Insert(KnockoutImpactSeconds + .18f + holdSeconds, group.DOFade(0, .24f))
            .OnComplete(() => IsShowing = false);
        if (ink)
        {
            animation.Insert(KnockoutImpactSeconds - .1f,
                DOTween.To(() => ink.Progress, value => ink.Progress = value, 1, .095f).SetEase(Ease.OutExpo));
            animation.Insert(KnockoutImpactSeconds - .1f,
                ink.rectTransform.DOScale(1, .1f).SetEase(Ease.OutCubic));
        }
        if (burst)
        {
            animation.InsertCallback(KnockoutImpactSeconds, () => burst.Progress = 0);
            animation.Insert(KnockoutImpactSeconds,
                DOTween.To(() => 0f, value => burst.Progress = value, 1, .19f).SetEase(Ease.OutQuad));
        }
        return true;
    }

    void InsertLetterFlight(RectTransform letter, Graphic graphic, Vector2 home, Vector3 homeScale,
        float startsAt, float foregroundScale, float tilt)
    {
        letter.localScale = homeScale * foregroundScale;
        letter.localRotation = Quaternion.Euler(0, 0, tilt);
        letter.anchoredPosition = home + new Vector2(Mathf.Sign(home.x) * 24, 18);
        // Accelerate out of the foreground, then stop exactly at home without a bounce or drifting tail.
        animation.Insert(startsAt, graphic.DOFade(1, .018f).SetEase(Ease.Linear));
        animation.Insert(startsAt, letter.DOScale(homeScale, LetterFlightSeconds).SetEase(Ease.InCubic));
        animation.Insert(startsAt, letter.DOAnchorPos(home, LetterFlightSeconds).SetEase(Ease.InCubic));
        animation.Insert(startsAt, letter.DOLocalRotate(Vector3.zero, LetterFlightSeconds).SetEase(Ease.InCubic));
    }

    public void ResetPresentation()
    {
        animation?.Kill(false);
        DOTween.Kill(this, false);
        animation = null;
        pending = HasShown = IsShowing = false;
        ShowCount = 0;
        if (group)
        {
            group.alpha = 0;
            group.interactable = group.blocksRaycasts = false;
        }
        if (burst)
            burst.Progress = 1;
        if (ink)
        {
            ink.Progress = 0;
            ink.rectTransform.localScale = Vector3.one;
        }
        if (!cached) return;
        // UI children can be destroyed before the HUD's OnDisable during scene teardown.
        if (letterK)
        {
            letterK.anchoredPosition = kHome;
            letterK.localScale = kHomeScale;
            letterK.localRotation = Quaternion.identity;
        }
        if (letterO)
        {
            letterO.anchoredPosition = oHome;
            letterO.localScale = oHomeScale;
            letterO.localRotation = Quaternion.identity;
        }
        if (word)
        {
            word.anchoredPosition = wordHome;
            word.localScale = Vector3.one;
        }
        if (band)
            band.localScale = Vector3.one;
        if (subtitle)
        {
            subtitle.rectTransform.anchoredPosition = subtitleHome;
            subtitle.rectTransform.localScale = Vector3.one;
        }
    }

    public void EvaluatePreview(float seconds)
    {
        if (PreviewAnimations && animation != null && animation.IsActive())
        {
            animation.Goto(seconds, false);
            FitWordToCanvas();
        }
    }

    void Awake()
    {
        ResetPresentation();
    }

    void OnDisable()
    {
        ResetPresentation();
    }

    void OnDestroy()
    {
        animation?.Kill(false);
    }
}
