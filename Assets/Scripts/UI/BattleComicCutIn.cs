using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Compact portrait announcements above the fight, with no camera edits.</summary>
public sealed class BattleComicCutIn : MonoBehaviour
{
    public GameManager game;
    public BattleVfxPlayer vfx;
    public CanvasGroup group;
    public RectTransform band;
    public BattleComicPanelGraphic background;
    public Image portrait;
    public Sprite leftPortrait, rightPortrait;
    public Text title, caption;
    [Header("Legacy presentation")]
    [Min(.3f)] public float displaySeconds = 1.05f;
    [Header("Compact battle announcement")]
    public bool compactPresentation = true;
    [Range(.3f, 1.2f)] public float compactDisplaySeconds = .72f;
    [Range(.3f, .7f)] public float compactWidth = .48f;
    [Range(.76f, .90f)] public float compactAnchorY = .81f;
    [Range(84f, 150f)] public float compactHeight = 110f;
    public float PresentationSeconds => compactPresentation ? compactDisplaySeconds : displaySeconds;
    public bool PreviewAnimations { get; set; }
    public bool IsShowing { get; private set; }
    public int ShowCount { get; private set; }
    Sequence animation;
    BattleVfxPlayer subscribed;
    bool layoutCached, showingLeft;
    Vector2 bandHome, bandAnchorMin, bandAnchorMax, bandSize, bandPivot, portraitSize, parentSize;
    Vector2 titleHome, titleSize, titleAnchorMin, titleAnchorMax;
    Vector2 captionHome, captionSize, captionAnchorMin, captionAnchorMax;

    public void Bind()
    {
        if (subscribed == vfx) return;
        Unbind();
        subscribed = vfx;
        if (!subscribed) return;
        subscribed.SequenceBegan += OnSequence;
        subscribed.EffectsCleared += ResetPresentation;
    }

    void OnSequence(CharacterCombat fighter, CombatTripletData move, bool lethal)
    {
        if (move == null || !game) return;
        bool special = move.weapon == TrumpWeaponManager.WeaponType.Katana ||
            move.weapon == TrumpWeaponManager.WeaponType.Assassin;
        if (special || lethal || move.skill != BattleSkill.None)
            Present(fighter == game.leftCombat, move.weapon, lethal, move.skill);
    }

    void CacheLayout()
    {
        if (layoutCached) return;
        bandHome = band.anchoredPosition;
        bandAnchorMin = band.anchorMin;
        bandAnchorMax = band.anchorMax;
        bandSize = band.sizeDelta;
        bandPivot = band.pivot;
        titleHome = title.rectTransform.anchoredPosition;
        titleSize = title.rectTransform.sizeDelta;
        titleAnchorMin = title.rectTransform.anchorMin;
        titleAnchorMax = title.rectTransform.anchorMax;
        captionHome = caption.rectTransform.anchoredPosition;
        captionSize = caption.rectTransform.sizeDelta;
        captionAnchorMin = caption.rectTransform.anchorMin;
        captionAnchorMax = caption.rectTransform.anchorMax;
        if (portrait.transform.parent is RectTransform portraitFrame) portraitSize = portraitFrame.sizeDelta;
        layoutCached = true;
    }

    public void Present(bool left, TrumpWeaponManager.WeaponType weapon, bool lethal,
        BattleSkill skill = BattleSkill.None)
    {
        if (!Application.isPlaying && !PreviewAnimations) return;
        if (!band || !portrait || !title || !caption || !background || !group) return;
        animation?.Kill(false);
        CacheLayout();
        RestoreLayout();
        showingLeft = left;
        bool skillCard = skill != BattleSkill.None;
        Vector3 cardScale = Vector3.one * (!compactPresentation && skillCard ? .65f : 1);
        band.anchoredPosition = bandHome + (skillCard ? Vector2.up * 120 : Vector2.zero);
        if (compactPresentation) ArrangeCompact();
        portrait.sprite = left ? leftPortrait : rightPortrait;
        portrait.rectTransform.localScale = new Vector3(left ? -1 : 1, 1, 1);
        title.text = lethal ? "FINISHING BLOW!" : skill == BattleSkill.Archer ? "METEOR SHOT!" :
            skill == BattleSkill.WhiteMage ? "CELESTIAL SMITE!" :
            weapon == TrumpWeaponManager.WeaponType.Katana ? "BLADE FURY!" : "PHANTOM STRIKE!";
        var slot = game && game.uiManager ? (left ? game.uiManager.left : game.uiManager.right) : null;
        string fighterName = slot != null && slot.nameText
            ? slot.nameText.text.ToUpperInvariant() : left ? "MANKEY" : "PEPE";
        caption.text = fighterName + "  /  " + (lethal ? "FINISHER" : skillCard ? "SKILL" : "CRITICAL");
        background.color = left ? new Color(1, .64f, .12f, 1) : new Color(.16f, .7f, .73f, 1);
        group.alpha = 1;
        group.blocksRaycasts = false;
        group.interactable = false;
        Vector3 entrance = compactPresentation ? new Vector3(.96f, .98f, 1) : new Vector3(.84f, .9f, 1);
        band.localScale = Vector3.Scale(cardScale, entrance);
        IsShowing = true;
        ShowCount++;
        animation = DOTween.Sequence().SetUpdate(true).SetId(this).SetLink(gameObject, LinkBehaviour.KillOnDisable);
        float entry = compactPresentation ? .09f : .12f;
        float fade = compactPresentation ? .12f : .15f;
        animation.Append(band.DOScale(cardScale, entry).SetEase(Ease.OutCubic))
            .AppendInterval(Mathf.Max(.03f, PresentationSeconds - entry - fade))
            .Append(group.DOFade(0, fade))
            .Join(band.DOScale(Vector3.Scale(cardScale, new Vector3(1.01f, .98f, 1)), fade))
            .OnComplete(() => IsShowing = false);
    }

    void ArrangeCompact()
    {
        if (!(band.parent is RectTransform parent)) return;
        parentSize = parent.rect.size;
        float width = Mathf.Max(1, parentSize.x * compactWidth);
        band.anchorMin = new Vector2(.5f, compactAnchorY);
        band.anchorMax = band.anchorMin;
        band.pivot = new Vector2(.5f, .5f);
        band.sizeDelta = new Vector2(width, compactHeight);
        float inset = Mathf.Min(24, parentSize.x * .025f);
        float position = (showingLeft ? -1 : 1) * Mathf.Max(0, (parentSize.x - width) * .5f - inset);
        band.anchoredPosition = new Vector2(position, 0);
        if (portrait.transform.parent is RectTransform portraitFrame)
            portraitFrame.sizeDelta = Vector2.one * compactHeight * .76f;
        title.rectTransform.anchorMin = new Vector2(.27f, .40f);
        title.rectTransform.anchorMax = new Vector2(.95f, .94f);
        title.rectTransform.anchoredPosition = Vector2.zero;
        title.rectTransform.sizeDelta = Vector2.zero;
        caption.rectTransform.anchorMin = new Vector2(.27f, .10f);
        caption.rectTransform.anchorMax = new Vector2(.95f, .38f);
        caption.rectTransform.anchoredPosition = Vector2.zero;
        caption.rectTransform.sizeDelta = Vector2.zero;
    }

    void RestoreLayout()
    {
        if (!band || !layoutCached) return;
        band.anchorMin = bandAnchorMin;
        band.anchorMax = bandAnchorMax;
        band.sizeDelta = bandSize;
        band.pivot = bandPivot;
        band.anchoredPosition = bandHome;
        if (portrait && portrait.transform.parent is RectTransform portraitFrame)
            portraitFrame.sizeDelta = portraitSize;
        if (title)
        {
            title.rectTransform.anchorMin = titleAnchorMin;
            title.rectTransform.anchorMax = titleAnchorMax;
            title.rectTransform.anchoredPosition = titleHome;
            title.rectTransform.sizeDelta = titleSize;
        }
        if (caption)
        {
            caption.rectTransform.anchorMin = captionAnchorMin;
            caption.rectTransform.anchorMax = captionAnchorMax;
            caption.rectTransform.anchoredPosition = captionHome;
            caption.rectTransform.sizeDelta = captionSize;
        }
    }

    void LateUpdate()
    {
        if (IsShowing && compactPresentation && band && band.parent is RectTransform parent &&
            parent.rect.size != parentSize) ArrangeCompact();
    }

    public void EvaluatePreview(float seconds)
    {
        if (PreviewAnimations && animation != null && animation.IsActive()) animation.Goto(seconds, false);
    }

    public void ResetPresentation()
    {
        animation?.Kill(false);
        animation = null;
        IsShowing = false;
        if (group)
        {
            group.alpha = 0;
            group.interactable = false;
            group.blocksRaycasts = false;
        }
        if (band) band.localScale = Vector3.one;
        RestoreLayout();
    }

    void Unbind()
    {
        if (subscribed)
        {
            subscribed.SequenceBegan -= OnSequence;
            subscribed.EffectsCleared -= ResetPresentation;
        }
        subscribed = null;
    }

    void Awake() => ResetPresentation();
    void OnEnable() => Bind();
    void OnDisable()
    {
        Unbind();
        ResetPresentation();
    }
    void OnDestroy()
    {
        Unbind();
        animation?.Kill(false);
    }
}
