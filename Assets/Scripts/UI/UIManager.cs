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

    [Header("Speech Bubble Billboard")]
    [Tooltip("Make the world-space speech bubbles (and meme result popups) always FACE the camera, so the text stays readable while the camera orbits in 3D.")]
    public bool billboardSpeechBubbles = true;
    [Tooltip("Extra yaw (degrees) applied after facing the camera. 0 = look straight at it; 180 flips the text if it reads backwards.")]
    public float bubbleYawOffset = 0f;

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
        // NOTE: do NOT Destroy(gameObject) on a duplicate.
        // This component holds THIS scene's widgets; destroying it would wipe the whole UI
        // hierarchy (and leave GameManager.uiManager pointing at a dead object -> silent no-op).
        // We simply register the most recent instance so callers can fall back to it by code.
        if (Instance == null || !Instance.isActiveAndEnabled)
        {
            Instance = this;
        }
        else if (verboseLogging && Instance != this)
        {
            Debug.LogWarning($"MemeBattleUI: multiple instances active. Resting Instance on '{Instance.name}'; this is '{name}'.", this);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        UpdateTimer();
        UpdateSpeechBubbleBillboards();
    }

    /// <summary>
    /// Makes each fighter's world-space speech bubble (and meme result popup) FACE the main camera.
    /// The bubbles live on world-space canvases above the characters, so once the camera orbits in 3D
    /// they would otherwise be seen edge-on / mirrored. Rotating them to match the camera keeps the
    /// text readable from any angle.
    /// </summary>
    private void UpdateSpeechBubbleBillboards()
    {
        if (!billboardSpeechBubbles) return;

        Camera cam = Camera.main;
        if (cam == null) return;

        // NOTE: speechBubbleText is intentionally NOT billboarded -- it is kept at rotation 0 as
        // authored in the scene. Only the meme-result popup is turned, because that one is a separate
        // world-space element that reads mirrored / edge-on while the camera orbits.
        BillboardTowardCamera(left != null ? left.memeResultObject : null, cam);
        BillboardTowardCamera(right != null ? right.memeResultObject : null, cam);
    }

    /// <summary>
    /// Same facing correction applied to a whole GameObject's transform (used for the meme-result
    /// popup). No-op when the object or camera is missing.
    /// </summary>
    private void BillboardTowardCamera(GameObject target, Camera cam)
    {
        if (target == null || cam == null) return;
        BillboardTowardCamera(target.transform, cam);
    }

    /// <summary>
    /// Rotates the transform owning <paramref name="text"/> so it FACES the camera (its forward axis
    /// points from the bubble toward the camera), plus the configured yaw offset.
    ///
    /// NOTE ON THE MATH: this must be LookRotation(cameraPos - bubblePos), NOT a copy of the camera's
    /// rotation. Copying the camera rotation points the canvas' forward AWAY from the camera, which
    /// makes a world-space Canvas render its text MIRRORED ("-200" appeared as "002-").
    /// No-op when the text or camera is missing.
    /// </summary>
    private void BillboardTowardCamera(Text text, Camera cam)
    {
        if (text == null || cam == null) return;
        BillboardTowardCamera(text.transform, cam);
    }

    /// <summary>
    /// Core facing correction: aims <paramref name="t"/>'s forward axis at the camera so its front face
    /// (and therefore its text) is readable from the current camera angle. Shared by the speech bubble
    /// and the meme-result popup.
    /// </summary>
    private void BillboardTowardCamera(Transform t, Camera cam)
    {
        if (t == null || cam == null) return;

        // Direction from the bubble TO the camera. A world-space canvas shows its front face toward its
        // own forward axis, so aiming that axis at the camera is what keeps the text readable.
        Vector3 toCamera = cam.transform.position - t.position;
        toCamera.y = 0f; // keep the bubble upright; only yaw is adjusted
        if (toCamera.sqrMagnitude < 0.0001f) return; // degenerate: camera exactly above/below
        Quaternion facing = Quaternion.LookRotation(toCamera.normalized, Vector3.up);
        t.rotation = facing * Quaternion.Euler(0f, bubbleYawOffset, 0f);

        // NOTE: the bubble follows its TARGET every frame elsewhere, so we only touch rotation here --
        // changing position here would fight that follow logic.
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

    // The speech bubble is ONE Text shared by the meme dialogue and the damage number. To stop them
    // fighting each other, each side tracks its own "who owns the bubble right now":
    //   - _dialogueText[side]  = the meme line currently displayed (empty when none)
    //   - _damageHideRoutine[side] = the pending auto-clear for the damage number
    // When the damage number's timer expires we restore the meme line (if any) instead of blanking the
    // bubble, which is what used to erase the dialogue the moment a hit landed.
    private readonly string[] _dialogueText = new string[2];
    private readonly Coroutine[] _damageHideRoutine = new Coroutine[2];

    private static int SideIndex(Side side) => side == Side.Left ? 0 : 1;

    /// <summary>
    /// Shows a damage popup in the fighter's speech bubble briefly. When the popup expires the meme
    /// dialogue (if one is set) is restored, so the damage number never permanently erases it.
    /// </summary>
    public void ShowDamage(Side side, int amount)
    {
        var slot = GetSlot(side);
        if (slot?.speechBubbleText == null) return;

        int i = SideIndex(side);

        // Restart the hide timer so a new damage number is not wiped by the PREVIOUS number's timer.
        if (_damageHideRoutine[i] != null) StopCoroutine(_damageHideRoutine[i]);

        slot.speechBubbleText.text = $"-{amount}";
        _damageHideRoutine[i] = StartCoroutine(HideDamageThenRestoreDialogue(side, slot.speechBubbleText, 1.5f));
    }

    /// <summary>
    /// Sets the fighter's dialogue / speech bubble text (used for meme text). Remembered so a damage
    /// popup can restore it once its own timer expires.
    /// </summary>
    public void SetDialogue(Side side, string text)
    {
        var slot = GetSlot(side);
        if (slot?.speechBubbleText == null) return;

        int i = SideIndex(side);
        _dialogueText[i] = text ?? string.Empty;

        // A damage popup that is currently counting down must NOT overwrite this dialogue.
        if (_damageHideRoutine[i] != null)
        {
            StopCoroutine(_damageHideRoutine[i]);
            _damageHideRoutine[i] = null;
        }

        slot.speechBubbleText.text = _dialogueText[i];
    }

    /// <summary>
    /// Clears the fighter's dialogue (used at the start of a new turn).
    /// </summary>
    public void ClearDialogue(Side side)
    {
        var slot = GetSlot(side);
        if (slot?.speechBubbleText == null) return;

        int i = SideIndex(side);
        _dialogueText[i] = string.Empty;

        if (_damageHideRoutine[i] != null)
        {
            StopCoroutine(_damageHideRoutine[i]);
            _damageHideRoutine[i] = null;
        }

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
        if (slider == null) return;

        // Any change is applied IMMEDIATELY. The health bar must drop the moment the attack plays
        // (i.e. when the damage event is applied), not tween down over time.
        if (side == Side.Left)
        {
            if (_hpLerpLeft != null) { StopCoroutine(_hpLerpLeft); _hpLerpLeft = null; }
        }
        else
        {
            if (_hpLerpRight != null) { StopCoroutine(_hpLerpRight); _hpLerpRight = null; }
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

    /// <summary>
    /// Hides the damage number after <paramref name="delay"/> and restores whatever meme dialogue the
    /// side had (empty when none). Restoring -- instead of blanking -- is what keeps the dialogue from
    /// being erased by a hit landing on the same fighter.
    /// </summary>
    private IEnumerator HideDamageThenRestoreDialogue(Side side, Text text, float delay)
    {
        yield return new WaitForSeconds(delay);

        int i = SideIndex(side);
        _damageHideRoutine[i] = null;

        if (text != null) text.text = _dialogueText[i] ?? string.Empty;
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
