using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Brief portrait/name panels for critical moves and lethal windups, with no camera edits.</summary>
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
    [Header("Presentation")]
    [Min(.3f)] public float displaySeconds = 1.05f;
    public bool PreviewAnimations { get; set; }
    public bool IsShowing { get; private set; }
    public int ShowCount { get; private set; }
    Sequence animation;
    BattleVfxPlayer subscribed;
    bool layoutCached;
    Vector2 bandHome;

    public void Bind()
    {
        if (subscribed == vfx) return;
        Unbind(); subscribed = vfx;
        if (subscribed) { subscribed.SequenceBegan += OnSequence; subscribed.EffectsCleared += ResetPresentation; }
    }

    void OnSequence(CharacterCombat fighter, CombatTripletData move, bool lethal)
    {
        bool special = move.weapon == TrumpWeaponManager.WeaponType.Katana || move.weapon == TrumpWeaponManager.WeaponType.Assassin;
        if (special || lethal || move.skill != BattleSkill.None) Present(fighter == game.leftCombat, move.weapon, lethal, move.skill);
    }

    public void Present(bool left, TrumpWeaponManager.WeaponType weapon, bool lethal, BattleSkill skill = BattleSkill.None)
    {
        if (!Application.isPlaying && !PreviewAnimations) return;
        animation?.Kill(false);
        if (!layoutCached) { bandHome = band.anchoredPosition; layoutCached = true; }
        bool skillCard = skill != BattleSkill.None;
        Vector3 cardScale = Vector3.one * (skillCard ? .65f : 1);
        band.anchoredPosition = bandHome + (skillCard ? Vector2.up * 120 : Vector2.zero);
        portrait.sprite = left ? leftPortrait : rightPortrait;
        portrait.rectTransform.localScale = new Vector3(left ? -1 : 1, 1, 1);
        title.text = lethal ? "FINISHING BLOW!" : skill == BattleSkill.Archer ? "METEOR SHOT!" :
            skill == BattleSkill.WhiteMage ? "CELESTIAL SMITE!" : weapon == TrumpWeaponManager.WeaponType.Katana ? "BLADE FURY!" : "PHANTOM STRIKE!";
        var slot = left ? game.uiManager.left : game.uiManager.right;
        caption.text = (slot.nameText ? slot.nameText.text.ToUpperInvariant() : left ? "MANKEY" : "PEPE") + "  /  " + (lethal ? "FINISHER" : skill != BattleSkill.None ? "SKILL" : "CRITICAL");
        background.color = left ? new Color(1, .64f, .12f, 1) : new Color(.16f, .7f, .73f, 1);
        group.alpha = 1; group.blocksRaycasts = group.interactable = false;
        band.localScale = Vector3.Scale(cardScale, new Vector3(.84f, .9f, 1));
        IsShowing = true; ShowCount++;
        animation = DOTween.Sequence().SetUpdate(true).SetId(this).SetLink(gameObject, LinkBehaviour.KillOnDisable);
        animation.Append(band.DOScale(cardScale, .12f).SetEase(Ease.OutExpo))
            .AppendInterval(Mathf.Max(.03f, displaySeconds - .27f)).Append(group.DOFade(0, .15f))
            .Join(band.DOScale(Vector3.Scale(cardScale, new Vector3(1.04f, .95f, 1)), .15f))
            .OnComplete(() => IsShowing = false);
    }

    public void EvaluatePreview(float seconds) { if (PreviewAnimations && animation != null && animation.IsActive()) animation.Goto(seconds, false); }
    public void ResetPresentation()
    {
        animation?.Kill(false); animation = null; IsShowing = false;
        if (group) { group.alpha = 0; group.interactable = group.blocksRaycasts = false; }
        if (band) band.localScale = Vector3.one;
        if (band && layoutCached) band.anchoredPosition = bandHome;
    }
    void Unbind()
    {
        if (subscribed) { subscribed.SequenceBegan -= OnSequence; subscribed.EffectsCleared -= ResetPresentation; }
        subscribed = null;
    }
    void Awake() => ResetPresentation();
    void OnEnable() => Bind();
    void OnDisable() { Unbind(); ResetPresentation(); }
    void OnDestroy() { Unbind(); animation?.Kill(false); }
}
