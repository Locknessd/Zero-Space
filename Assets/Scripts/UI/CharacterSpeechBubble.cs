using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls an in-game speech bubble floating directly over/next to a character.
/// Supports smooth pop-in animation, customizable tail, auto-dismiss, and billboard facing camera.
/// </summary>
public class CharacterSpeechBubble : MonoBehaviour
{
    [Header("Target & Position")]
    [Tooltip("Target transform to follow in 3D world (e.g. character head)")]
    public Transform followTarget;
    [Tooltip("World offset from target position")]
    public Vector3 worldOffset = new Vector3(0f, 2.2f, 0f);
    [Tooltip("Whether to billboard/face main camera in World Space")]
    public bool faceCamera = true;

    [Header("UI References")]
    public CanvasGroup canvasGroup;
    public RectTransform bubbleContainer;
    public Text dialogueText;
    public Text speakerNameText;
    public Image bubbleBackground;
    public Image bubbleTail;
    public Image iconImage;

    [Header("Behavior")]
    [Tooltip("Auto hide after seconds (0 = do not auto-hide)")]
    public float autoHideDuration = 5f;
    [Tooltip("Typewriter effect speed (chars per second, 0 = instant)")]
    public float typewriterSpeed = 35f;
    [Tooltip("Scale punch animation duration")]
    public float popDuration = 0.25f;

    [Header("Current State")]
    public bool isVisible = false;

    private Coroutine _showRoutine;
    private Coroutine _typewriterRoutine;
    private Coroutine _autoHideRoutine;
    private Camera _mainCamera;

    void Awake()
    {
        _mainCamera = Camera.main;
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (bubbleContainer == null) bubbleContainer = transform as RectTransform;

        // Start hidden by default
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
        }
        if (bubbleContainer != null)
        {
            bubbleContainer.localScale = Vector3.zero;
        }
        isVisible = false;
    }

    void LateUpdate()
    {
        if (followTarget != null)
        {
            transform.position = followTarget.position + worldOffset;
        }

        if (faceCamera)
        {
            if (_mainCamera == null) _mainCamera = Camera.main;
            if (_mainCamera != null)
            {
                // Face camera rotation
                transform.rotation = _mainCamera.transform.rotation;
            }
        }
    }

    public void SetSpeakerName(string speakerName)
    {
        if (speakerNameText != null)
        {
            speakerNameText.text = speakerName ?? string.Empty;
            speakerNameText.gameObject.SetActive(!string.IsNullOrEmpty(speakerName));
        }
    }

    public void ShowText(string text, string speaker = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            Hide();
            return;
        }

        if (speaker != null)
        {
            SetSpeakerName(speaker);
        }

        if (_showRoutine != null) StopCoroutine(_showRoutine);
        if (_typewriterRoutine != null) StopCoroutine(_typewriterRoutine);
        if (_autoHideRoutine != null) StopCoroutine(_autoHideRoutine);

        _showRoutine = StartCoroutine(ShowRoutine(text));
    }

    private IEnumerator ShowRoutine(string fullText)
    {
        isVisible = true;
        gameObject.SetActive(true);

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
        }

        // Pop in animation
        float elapsed = 0f;
        Vector3 startScale = Vector3.zero;
        Vector3 targetScale = Vector3.one;

        if (typewriterSpeed > 0f && dialogueText != null)
        {
            _typewriterRoutine = StartCoroutine(TypewriterRoutine(fullText));
        }
        else if (dialogueText != null)
        {
            dialogueText.text = fullText;
        }

        while (elapsed < popDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / popDuration);
            // Elastic/Overshoot pop curve: 0 -> 1.15 -> 1.0
            float scaleProgress = OvershootEase(t);
            if (bubbleContainer != null)
            {
                bubbleContainer.localScale = Vector3.LerpUnclamped(startScale, targetScale, scaleProgress);
            }
            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Clamp01(t * 2f);
            }
            yield return null;
        }

        if (bubbleContainer != null) bubbleContainer.localScale = Vector3.one;
        if (canvasGroup != null) canvasGroup.alpha = 1f;

        // Auto hide timer
        if (autoHideDuration > 0f)
        {
            _autoHideRoutine = StartCoroutine(AutoHideRoutine(autoHideDuration));
        }
    }

    private IEnumerator TypewriterRoutine(string fullText)
    {
        if (dialogueText == null) yield break;
        dialogueText.text = "";
        float delay = 1f / typewriterSpeed;

        for (int i = 0; i < fullText.Length; i++)
        {
            dialogueText.text = fullText.Substring(0, i + 1);
            yield return new WaitForSeconds(delay);
        }
    }

    private IEnumerator AutoHideRoutine(float delay)
    {
        yield return new WaitForSeconds(delay);
        Hide();
    }

    public void Hide()
    {
        if (!isVisible && (canvasGroup == null || canvasGroup.alpha <= 0f)) return;

        if (_showRoutine != null) StopCoroutine(_showRoutine);
        if (_typewriterRoutine != null) StopCoroutine(_typewriterRoutine);
        if (_autoHideRoutine != null) StopCoroutine(_autoHideRoutine);

        _showRoutine = StartCoroutine(HideRoutine());
    }

    private IEnumerator HideRoutine()
    {
        isVisible = false;
        float elapsed = 0f;
        float hideDur = 0.18f;
        Vector3 startScale = bubbleContainer != null ? bubbleContainer.localScale : Vector3.one;
        float startAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;

        while (elapsed < hideDur)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / hideDur);
            if (bubbleContainer != null)
            {
                bubbleContainer.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
            }
            if (canvasGroup != null)
            {
                canvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
            }
            yield return null;
        }

        if (bubbleContainer != null) bubbleContainer.localScale = Vector3.zero;
        if (canvasGroup != null) canvasGroup.alpha = 0f;
        if (dialogueText != null) dialogueText.text = "";
    }

    private float OvershootEase(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }
}
