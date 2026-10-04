using UnityEngine;
using UnityEngine.UI;

/// <summary>Immediate HP plus delayed damage chips, critical badges and cue-counted combos.</summary>
public sealed class BattleHudFeedback : MonoBehaviour
{
    public MemeBattleUI ui;
    public GameManager game;
    public BattleVfxPlayer vfx;
    public Image leftChip, rightChip;
    public Text leftCritical, rightCritical, leftCombo, rightCombo;
    public int ComboCount { get; private set; }
    public int CriticalCount { get; private set; }
    sealed class State { public float target, start, delayed, elapsed, badgeTime, comboTime; public bool initialized; }
    readonly State[] states = {new State(), new State()};
    BattleVfxPlayer subscribed;
    int attackerSide;

    public float DelayedHealth(MemeBattleUI.Side side) => states[(int)side].delayed;

    public void Bind()
    {
        if (subscribed == vfx) return;
        Unbind(); subscribed = vfx;
        if (subscribed)
        {
            subscribed.SequenceBegan += BeginSequence;
            subscribed.EffectPlayed += Effect;
            subscribed.EffectsCleared += ResetEffects;
        }
        ResetEffects();
    }

    void BeginSequence(CharacterCombat fighter, CombatTripletData move, bool lethal)
    {
        attackerSide = fighter == game.leftCombat ? 0 : 1;
        ComboCount = 0;
        if (leftCombo) leftCombo.text = ""; if (rightCombo) rightCombo.text = "";
    }

    void Effect(string cue, GameObject effect)
    {
        if (cue != "heavy_hit" && cue != "light_hit") return;
        ComboCount++;
        Text label = attackerSide == 0 ? leftCombo : rightCombo;
        if (label) { label.text = ComboCount + (ComboCount == 1 ? " HIT!" : " HITS!"); label.color = new Color(1, .83f, .28f, 1); }
        states[attackerSide].comboTime = .8f;
    }

    public void Health(MemeBattleUI.Side side, float value)
    {
        var state = states[(int)side]; value = Mathf.Clamp01(value);
        if (state.initialized && Mathf.Abs(value - state.target) < .0001f) return;
        if (!state.initialized || value >= state.target)
        {
            state.delayed = state.start = value; state.elapsed = 1; state.initialized = true;
        }
        else
        {
            state.start = Mathf.Max(state.delayed, state.target); state.delayed = state.start; state.elapsed = 0;
        }
        state.target = value;
        LayoutChip((int)side);
    }

    public void Damage(MemeBattleUI.Side side, bool critical)
    {
        Text badge = side == MemeBattleUI.Side.Left ? leftCritical : rightCritical;
        var state = states[(int)side]; state.badgeTime = critical ? .75f : 0;
        if (badge) { badge.text = critical ? "CRITICAL!" : ""; badge.color = new Color(1, .56f, .08f, critical ? 1 : 0); }
        if (critical) CriticalCount++;
    }

    public void AdvanceHud(float unscaledDelta)
    {
        unscaledDelta = Mathf.Max(0, unscaledDelta);
        for (int i = 0; i < 2; i++)
        {
            var state = states[i]; state.elapsed = Mathf.Min(1, state.elapsed + unscaledDelta);
            float u = Mathf.Clamp01((state.elapsed - .2f) / .45f);
            state.delayed = Mathf.Lerp(state.start, state.target, 1 - (1 - u) * (1 - u));
            LayoutChip(i);
            state.badgeTime = Mathf.Max(0, state.badgeTime - unscaledDelta);
            var badge = i == 0 ? leftCritical : rightCritical;
            if (badge) { var tint = badge.color; tint.a = Mathf.Clamp01(state.badgeTime / .2f); badge.color = tint; }
            state.comboTime = Mathf.Max(0, state.comboTime - unscaledDelta);
            var combo = i == 0 ? leftCombo : rightCombo;
            if (combo)
            {
                var tint = combo.color; tint.a = Mathf.Clamp01(state.comboTime / .2f); combo.color = tint;
                var scale = Vector3.one * (1 + .18f * Mathf.Clamp01((state.comboTime - .6f) / .2f));
                if (combo.rectTransform.localScale != scale) combo.rectTransform.localScale = scale;
            }
        }
    }

    void LayoutChip(int side)
    {
        var image = side == 0 ? leftChip : rightChip;
        var slider = side == 0 ? ui?.left.hpSlider : ui?.right.hpSlider;
        if (!image || !slider) return;
        var state = states[side]; float low = state.target, high = Mathf.Max(low, state.delayed);
        bool reversed = slider.direction == Slider.Direction.RightToLeft;
        var rect = image.rectTransform;
        var min = new Vector2(reversed ? 1 - high : low, 0);
        var max = new Vector2(reversed ? 1 - low : high, 1);
        if (rect.anchorMin != min) rect.anchorMin = min;
        if (rect.anchorMax != max) rect.anchorMax = max;
        if (rect.offsetMin != Vector2.zero) rect.offsetMin = Vector2.zero;
        if (rect.offsetMax != Vector2.zero) rect.offsetMax = Vector2.zero;
        image.enabled = high - low > .0001f;
    }

    public void ResetEffects()
    {
        ComboCount = 0;
        for (int i = 0; i < 2; i++)
        {
            var slider = i == 0 ? ui?.left.hpSlider : ui?.right.hpSlider;
            var state = states[i]; state.target = state.start = state.delayed = slider ? slider.value : 1;
            state.initialized = true; state.elapsed = 1; state.comboTime = state.badgeTime = 0;
            Text badge = i == 0 ? leftCritical : rightCritical; Text combo = i == 0 ? leftCombo : rightCombo;
            if (badge) badge.text = ""; if (combo) { combo.text = ""; combo.rectTransform.localScale = Vector3.one; }
            LayoutChip(i);
        }
    }
    void Unbind()
    {
        if (subscribed) { subscribed.SequenceBegan -= BeginSequence; subscribed.EffectPlayed -= Effect; subscribed.EffectsCleared -= ResetEffects; }
        subscribed = null;
    }
    void OnEnable() => Bind();
    void Update() => AdvanceHud(Time.unscaledDeltaTime);
    void OnDisable() { Unbind(); ResetEffects(); }
}
