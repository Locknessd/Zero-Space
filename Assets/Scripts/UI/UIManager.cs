using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// PROJECT ARCHITECTURE: Presentation / UI Layer
/// ROLE: Single entry point ("cổng UI") for ALL game UI updates in Meme Battle.
///
/// WHY THIS EXISTS:
/// - The old flow had GameManager poke left/right PlayerUI instances separately, spreading
///   UI knowledge (which side, which label, which slider) across gameplay code.
/// - UIManager centralises that: gameplay code sends semantic updates (health, damage,
///   dialogue, timer, round, multiplier, votes, result) and UIManager decides how and
///   where to render them.
///
/// DESIGN:
/// - Two "fighter slots" (Left / Right) mirror the two on-screen fighters. A slot holds
///   the widgets for that fighter. This is presentation detail, hidden behind a single
///   API — callers never address slots directly unless they want to.
/// - Global widgets (round number, timer, multiplier, final totals, victory banner) live
///   on UIManager itself, not per fighter.
/// - Every method is null-safe: a missing widget logs nothing and simply no-ops, so an
///   incompletely wired scene never crashes the match.
///
/// AI NOTE: This is the UI authority. GameManager routes events here. Per-fighter widgets
/// are grouped into a serializable FighterSlot class that can be dragged in the Inspector.
/// </summary>
public class MemeBattleUI : MonoBehaviour
{
    public static MemeBattleUI Instance { get; private set; }

    public enum Side { Left, Right }

    /// <summary>
    /// All widgets belonging to one on-screen fighter.
    /// </summary>
    [System.Serializable]
    public class FighterSlot
    {
        [Tooltip("Root GameObject for this fighter's UI (may be toggled off when unused).")]
        public GameObject root;

        [Tooltip("Name label for the fighter.")]
        public Text nameText;

        [Tooltip("Health slider (0..1 fill).")]
        public Slider hpSlider;
        [Tooltip("Rage/energy slider (0..1 fill).")]
        public Slider rageSlider;

        [Tooltip("Health text, e.g. '850/1000'.")]
        public Text healthText;

        [Tooltip("Speech bubble text used for dialogue, meme text and damage numbers.")]
        public Text speechBubbleText;

        [Tooltip("GameObject shown briefly to display the meme result text.")]
        public GameObject memeResultObject;
        [Tooltip("TextMeshPro label inside memeResultObject.")]
        public TextMeshProUGUI memeResultText;
    }

    [Header("Fighter Slots")]
    [Tooltip("Left on-screen fighter (backend 'bot_a' by convention).")]
    public FighterSlot left = new FighterSlot();
    [Tooltip("Right on-screen fighter (backend 'bot_b' by convention).")]
    public FighterSlot right = new FighterSlot();

    [Header("Global Widgets")]
    [Tooltip("Round / turn number label, e.g. 'ROUND 3'.")]
    public Text roundNumberText;
    [Tooltip("Countdown timer label, e.g. '00:05'.")]
    public Text timerText;
    [Tooltip("Multiplier label, e.g. '1.5x'.")]
    public Text multiplierText;
    [Tooltip("Vote totals label.")]
    public Text finalTotalsText;
    [Tooltip("Optional victory/result banner (shown on WINNER_DECLARED).")]
    public GameObject resultPanel;
    [Tooltip("Optional victory/result text.")]
    public Text resultText;

    [Header("Behavior")]
    [Tooltip("Default initial max HP shown before the server snapshot arrives.")]
    public long defaultInitialMaxHpAtomic = 1000;
    [Tooltip("Verbose UI logging (debug only).")]
    public bool verboseLogging = false;

    // ------------------------------------------------------------------
    // Runtime state
    // ------------------------------------------------------------------

    // Countdown timer.
    private System.DateTime _turnClosesAtUtc;
    private bool _timerRunning;

    // Coroutines for smooth slider animation per side.
    private Coroutine _hpLerpLeft;
    private Coroutine _hpLerpRight;

    // Coroutines for temporary meme-result displays per side.
    private Coroutine _memeDisplayLeft;
    private Coroutine _memeDisplayRight;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        UpdateTimer();
    }

    // ------------------------------------------------------------------
    // Public API — fighters
    // ------------------------------------------------------------------

    /// <summary>
    /// Sets the display name for a fighter.
    /// </summary>
    public void SetFighterName(Side side, string name)
    {
        var slot = GetSlot(side);
        if (slot?.nameText == null) return;
        slot.nameText.text = name ?? string.Empty;
        if (slot.root != null) slot.root.SetActive(true);
    }

    /// <summary>
    /// Updates both the health slider (animated) and the health text for a fighter.
    /// Example: UpdateHealth(Side.Left, 850, 1000)
    /// </summary>
    public void UpdateHealth(Side side, long currentHp, long maxHp)
    {
        var slot = GetSlot(side);
        if (slot == null) return;

        // Slider (0..1 by max). Animated separately per side.
        if (slot.hpSlider != null)
        {
            float target = maxHp > 0 ? Mathf.Clamp01((float)currentHp / maxHp) : 0f;
            StartSliderLerp(side, slot.hpSlider, target);
        }

        // Text, e.g. "850/1000".
        if (slot.healthText != null)
        {
            slot.healthText.text = $"{currentHp}/{maxHp}";
        }
        else if (verboseLogging)
        {
            Debug.LogWarning($"UIManager: healthText is not assigned for side {side}.");
        }

        if (verboseLogging) Debug.Log($"UIManager.UpdateHealth [{side}]: {currentHp}/{maxHp}");
    }

    /// <summary>
    /// Updates the rage/energy bar for a fighter (0..max).
    /// </summary>
    public void UpdateRage(Side side, float current, float max)
    {
        var slot = GetSlot(side);
        if (slot?.rageSlider == null) return;
        slot.rageSlider.value = max > 0f ? Mathf.Clamp01(current / max) : 0f;
    }

    /// <summary>
    /// Shows a damage popup (rendered in the fighter's speech bubble) briefly.
    /// </summary>
    public void ShowDamage(Side side, int amount)
    {
        var slot = GetSlot(side);
        if (slot?.speechBubbleText == null) return;

        slot.speechBubbleText.text = $"-{amount}";
        StartCoroutine(ClearTextAfter(slot.speechBubbleText, 1.5f));
    }

    /// <summary>
    /// Sets the fighter's dialogue / speech bubble text (used for meme text).
    /// </summary>
    public void SetDialogue(Side side, string text)
    {
        var slot = GetSlot(side);
        if (slot?.speechBubbleText == null) return;
        slot.speechBubbleText.text = text ?? string.Empty;
    }

    /// <summary>
    /// Clears the fighter's dialogue (used at the start of a new turn).
    /// </summary>
    public void ClearDialogue(Side side)
    {
        var slot = GetSlot(side);
        if (slot?.speechBubbleText == null) return;
        slot.speechBubbleText.text = string.Empty;
    }

    /// <summary>
    /// Shows the meme result text on a fighter for a short duration.
    /// </summary>
    public void ShowMemeResult(Side side, string text, float duration = 2f)
    {
        var slot = GetSlot(side);
        if (slot?.memeResultObject == null || slot.memeResultText == null) return;

        if (side == Side.Left)
        {
            if (_memeDisplayLeft != null) StopCoroutine(_memeDisplayLeft);
            _memeDisplayLeft = StartCoroutine(MemeDisplayRoutine(slot.memeResultObject, slot.memeResultText, text, duration));
        }
        else
        {
            if (_memeDisplayRight != null) StopCoroutine(_memeDisplayRight);
            _memeDisplayRight = StartCoroutine(MemeDisplayRoutine(slot.memeResultObject, slot.memeResultText, text, duration));
        }
    }

    // ------------------------------------------------------------------
    // Public API — global widgets
    // ------------------------------------------------------------------

    /// <summary>
    /// Starts the per-turn countdown. Accepts ISO-8601 UTC open/close timestamps from the server.
    /// </summary>
    public void StartTurnTimer(int turnNumber, string opensAtIsoUtc, string closesAtIsoUtc)
    {
        SetTurnNumber(turnNumber);

        if (string.IsNullOrEmpty(closesAtIsoUtc))
        {
            _timerRunning = false;
            return;
        }

        if (System.DateTime.TryParse(
                closesAtIsoUtc, null,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var closes))
        {
            _turnClosesAtUtc = closes.ToUniversalTime();
            _timerRunning = true;
        }
        else
        {
            Debug.LogWarning($"UIManager: invalid closesAtIsoUtc='{closesAtIsoUtc}'");
            _timerRunning = false;
        }
    }

    /// <summary>
    /// Sets the round / turn number label.
    /// </summary>
    public void SetTurnNumber(int turnNumber)
    {
        if (roundNumberText != null) roundNumberText.text = $"ROUND {turnNumber}";
    }

    /// <summary>
    /// Stops the countdown timer.
    /// </summary>
    public void StopTimer()
    {
        _timerRunning = false;
    }

    /// <summary>
    /// Shows the vote multiplier, e.g. 1.5 -> "1.5x".
    /// </summary>
    public void ShowMultiplier(float multiplier)
    {
        if (multiplierText == null) return;
        multiplierText.text = multiplier.ToString("0.##") + "x";
    }

    /// <summary>
    /// Shows vote totals. Extracts a turn number from keys like "turn_7_C" when present.
    /// </summary>
    public void ShowFinalTotals(System.Collections.Generic.Dictionary<string, long> totals)
    {
        if (finalTotalsText == null) return;

        if (totals == null || totals.Count == 0)
        {
            finalTotalsText.text = string.Empty;
            return;
        }

        // If totals contain keys like "turn_7_C", display "Turn 7".
        try
        {
            foreach (var key in totals.Keys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (key.ToLowerInvariant().StartsWith("turn_"))
                {
                    var parts = key.Split('_');
                    if (parts.Length >= 2 && int.TryParse(parts[1], out int turnNum))
                    {
                        if (roundNumberText != null) roundNumberText.text = $"Turn {turnNum}";
                        break;
                    }
                }
            }
        }
        catch { }

        var sb = new System.Text.StringBuilder();
        foreach (var kv in totals) sb.AppendLine($"{kv.Key}: {kv.Value}");
        finalTotalsText.text = sb.ToString();
    }

    /// <summary>
    /// Shows the final match result (victory / draw). Pass null or empty to hide.
    /// </summary>
    public void ShowResult(string text)
    {
        if (resultText != null) resultText.text = text ?? string.Empty;
        if (resultPanel != null) resultPanel.SetActive(!string.IsNullOrEmpty(text));
    }

    /// <summary>
    /// Hides the result panel.
    /// </summary>
    public void HideResult()
    {
        if (resultPanel != null) resultPanel.SetActive(false);
    }

    // ------------------------------------------------------------------
    // Convenience: reset UI to a neutral starting state
    // ------------------------------------------------------------------

    /// <summary>
    /// Initialises both fighters to full default HP and clears transient text.
    /// Call this at match start before the server snapshot arrives.
    /// </summary>
    public void ResetForNewMatch()
    {
        HideResult();

        for (int i = 0; i < 2; i++)
        {
            var side = i == 0 ? Side.Left : Side.Right;
            var slot = GetSlot(side);
            if (slot == null) continue;

            UpdateHealth(side, defaultInitialMaxHpAtomic, defaultInitialMaxHpAtomic);
            UpdateRage(side, 0f, 1f);
            ClearDialogue(side);
        }

        if (multiplierText != null) multiplierText.text = string.Empty;
        if (finalTotalsText != null) finalTotalsText.text = string.Empty;
        if (timerText != null) timerText.text = string.Empty;
        StopTimer();
    }

    // ------------------------------------------------------------------
    // Internals
    // ------------------------------------------------------------------

    private FighterSlot GetSlot(Side side)
    {
        return side == Side.Left ? left : right;
    }

    private void StartSliderLerp(Side side, Slider slider, float target)
    {
        if (side == Side.Left)
        {
            if (_hpLerpLeft != null) StopCoroutine(_hpLerpLeft);
            _hpLerpLeft = StartCoroutine(LerpSliderRoutine(slider, target));
        }
        else
        {
            if (_hpLerpRight != null) StopCoroutine(_hpLerpRight);
            _hpLerpRight = StartCoroutine(LerpSliderRoutine(slider, target));
        }
    }

    private IEnumerator LerpSliderRoutine(Slider slider, float target)
    {
        if (slider == null) yield break;

        float start = slider.value;
        if (Mathf.Approximately(start, target))
        {
            slider.value = target;
            yield break;
        }

        float diff = Mathf.Abs(target - start);
        // Duration scales with the size of the change; slower when losing HP.
        float duration = Mathf.Clamp(diff * 0.6f, 0.05f, 1.0f);
        if (target < start) duration *= 1.5f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            slider.value = Mathf.Lerp(start, target, t);
            yield return null;
        }
        slider.value = target;
    }

    private IEnumerator MemeDisplayRoutine(GameObject go, TextMeshProUGUI textField, string content, float duration)
    {
        if (go == null || textField == null) yield break;
        textField.text = content ?? string.Empty;
        go.SetActive(true);
        yield return new WaitForSeconds(duration);
        go.SetActive(false);
        textField.text = string.Empty;
    }

    private IEnumerator ClearTextAfter(Text text, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (text != null) text.text = string.Empty;
    }

    private void UpdateTimer()
    {
        if (!_timerRunning) return;

        var remaining = _turnClosesAtUtc - System.DateTime.UtcNow;
        if (remaining.TotalSeconds <= 0)
        {
            if (timerText != null) timerText.text = "00:00";
            _timerRunning = false;
            return;
        }

        if (timerText != null) timerText.text = FormatTimeSpan(remaining);
    }

    private static string FormatTimeSpan(System.TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return string.Format("{0:D2}:{1:D2}:{2:D2}", (int)ts.TotalHours, ts.Minutes, ts.Seconds);
        return string.Format("{0:D2}:{1:D2}", ts.Minutes, ts.Seconds);
    }

    // ------------------------------------------------------------------
    // Mapping helper: backend character id -> on-screen side
    // ------------------------------------------------------------------

    /// <summary>
    /// Resolves a backend character id ("bot_a" / "bot_b" / actual character id) to a side.
    /// Returns null when the id is unknown.
    /// </summary>
    public static Side? SideFromCharacterId(string characterId)
    {
        if (string.IsNullOrEmpty(characterId)) return null;
        if (characterId.Equals("bot_a", System.StringComparison.OrdinalIgnoreCase)) return Side.Left;
        if (characterId.Equals("bot_b", System.StringComparison.OrdinalIgnoreCase)) return Side.Right;
        return null;
    }
}
