using UnityEngine;

/// <summary>Positions dialogue and damage callouts against the actual HP bars across HUD canvas scales.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(15000)]
public sealed class BattleHudCallouts : MonoBehaviour
{
    public MemeBattleUI ui;
    public RectTransform leftDamageAnchor, rightDamageAnchor;
    readonly Vector3[] corners = new Vector3[4];

    void LateUpdate() => RefreshLayout();

    public void RefreshLayout()
    {
        if (!ui || !(transform is RectTransform host)) return;
        Position(ui.left, leftDamageAnchor, host, false);
        Position(ui.right, rightDamageAnchor, host, true);
    }

    void Position(MemeBattleUI.FighterSlot slot, RectTransform damage, RectTransform host, bool right)
    {
        if (slot?.hpSlider == null) return;
        var bar = (RectTransform)slot.hpSlider.transform;
        var sourceCanvas = bar.GetComponentInParent<Canvas>();
        var hostCanvas = host.GetComponentInParent<Canvas>();
        if (!sourceCanvas || !hostCanvas) return;
        Camera sourceCamera = sourceCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : sourceCanvas.worldCamera;
        Camera hostCamera = hostCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : hostCanvas.worldCamera;
        bar.GetWorldCorners(corners);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(host,
            RectTransformUtility.WorldToScreenPoint(sourceCamera, corners[0]), hostCamera, out Vector2 bottom);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(host,
            RectTransformUtility.WorldToScreenPoint(sourceCamera, corners[2]), hostCamera, out Vector2 top);
        float barWidth = top.x - bottom.x;
        if (damage)
        {
            float x = Mathf.Lerp(bottom.x, top.x, right ? .2f : .8f);
            damage.anchoredPosition = new Vector2(Mathf.Clamp(x, host.rect.xMin + 90, host.rect.xMax - 90), top.y + 8);
        }
        if (!slot.speechBubble) return;
        float width = Mathf.Max(140, Mathf.Min(500, Mathf.Min(barWidth - 20, host.rect.width * .43f)));
        slot.speechBubble.SetWidth(width);
        var bubble = (RectTransform)slot.speechBubble.transform;
        float center = right ? top.x - width * .5f - 10 : bottom.x + width * .5f + 10;
        float edge = width * .5f + 24;
        bubble.anchoredPosition = new Vector2(Mathf.Clamp(center, host.rect.xMin + edge, host.rect.xMax - edge), bottom.y - 20);
    }
}
