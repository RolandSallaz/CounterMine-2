using UnityEngine;
using UnityEngine.UI;

/// <summary>Persistent threat arrow around the crosshair: tracks one danger point
/// (nearest live grenade) until hidden. Built in code.</summary>
public sealed class ThreatArrow : MonoBehaviour
{
    private RectTransform root;
    private CanvasGroup group;
    private RectTransform grenadeIcon;

    private void Awake()
    {
        // Prefab-authored instance: collect own components.
        root = (RectTransform)transform;
        group = GetComponent<CanvasGroup>();
        EnsureIcon();
    }

    private void EnsureIcon()
    {
        if (grenadeIcon != null) return;
        var arrow = GetComponentInChildren<HitDirectionArrow>(true);
        if (arrow != null) arrow.Style = HitDirectionArrow.Mark.Chevron;
        var icon = new GameObject("Grenade Icon", typeof(RectTransform), typeof(HitDirectionArrow));
        icon.transform.SetParent(transform, false);
        grenadeIcon = (RectTransform)icon.transform;
        grenadeIcon.anchorMin = grenadeIcon.anchorMax = new Vector2(.5f, 1f);
        grenadeIcon.anchoredPosition = new Vector2(0f, -42f);
        grenadeIcon.sizeDelta = new Vector2(34f, 38f);
        var graphic = icon.GetComponent<HitDirectionArrow>();
        graphic.Style = HitDirectionArrow.Mark.Grenade;
        graphic.color = arrow != null ? arrow.color : new Color(1f, .6f, .1f);
        graphic.raycastTarget = false;
    }
    public static ThreatArrow Create(Transform parent, Color color)
    {
        var go = new GameObject("Threat Arrow", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var root = (RectTransform)go.transform;
        root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
        root.pivot = new Vector2(.5f, .5f);
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = new Vector2(336f, 336f);
        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(HitDirectionArrow));
        arrowGo.transform.SetParent(go.transform, false);
        var arrow = (RectTransform)arrowGo.transform;
        arrow.anchorMin = arrow.anchorMax = new Vector2(.5f, 1f);
        arrow.pivot = new Vector2(.5f, .5f);
        arrow.anchoredPosition = new Vector2(0f, -10f);
        arrow.sizeDelta = new Vector2(28f, 18f);
        var graphic = arrowGo.GetComponent<HitDirectionArrow>();
        graphic.color = color;
        graphic.Style = HitDirectionArrow.Mark.Chevron;
        graphic.raycastTarget = false;
        var outline = arrowGo.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, .9f);
        outline.effectDistance = new Vector2(2f, -2f);
        go.SetActive(false);
        var threat = go.AddComponent<ThreatArrow>();
        threat.root = root;
        threat.group = group;
        return threat;
    }

    /// <summary>Points at the threat. Angle 0 = ahead, positive = right.</summary>
    public void PointAt(float angleDegrees)
    {
        if (root == null)
        {
            // Prefab instance starts disabled: activating runs Awake, which collects components.
            gameObject.SetActive(true);
            if (root == null) return;
        }
        root.gameObject.SetActive(true);
        root.localRotation = Quaternion.Euler(0f, 0f, -angleDegrees);
        EnsureIcon();
        // Keep the silhouette upright while its bearing orbits the crosshair.
        grenadeIcon.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
        if (group != null) group.alpha = 1f;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
