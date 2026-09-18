using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Hit-from direction wheel: red arrows around the crosshair pointing at recent
/// attackers. Purely local. Segments come from the PlayerHUD prefab; built in code only as a fallback.</summary>
public sealed class HitDirectionIndicator : MonoBehaviour
{
    private sealed class Segment
    {
        public RectTransform root;
        public CanvasGroup group;
        public float age = float.PositiveInfinity;
    }

    [SerializeField, Min(1)] private int segmentCount = 5;
    [SerializeField, Min(.1f)] private float lifetime = 1.1f;
    [SerializeField, Min(80f)] private float wheelSize = 240f;

    private readonly List<Segment> segments = new List<Segment>();

    public static HitDirectionIndicator Create(Transform parent)
    {
        var go = new GameObject("Hit Direction", typeof(RectTransform), typeof(HitDirectionIndicator));
        go.transform.SetParent(parent, false);
        var indicator = go.GetComponent<HitDirectionIndicator>();
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(indicator.wheelSize, indicator.wheelSize);
        indicator.Build();
        return indicator;
    }

    private void Awake()
    {
        if (segments.Count > 0) return;
        // Prefab-authored segments: direct children carrying a CanvasGroup.
        foreach (Transform child in transform)
        {
            var root = child as RectTransform;
            var group = child.GetComponent<CanvasGroup>();
            if (root == null || group == null) continue;
            group.alpha = 0f;
            group.blocksRaycasts = false;
            child.gameObject.SetActive(false);
            segments.Add(new Segment { root = root, group = group });
        }
        if (segments.Count == 0) Build();
    }

    private void Build()
    {
        if (segments.Count > 0) return;
        for (int i = 0; i < Mathf.Max(1, segmentCount); i++)
        {
            var rootGo = new GameObject("Segment " + i, typeof(RectTransform));
            rootGo.transform.SetParent(transform, false);
            var root = (RectTransform)rootGo.transform;
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            root.pivot = new Vector2(.5f, .5f);
            root.anchoredPosition = Vector2.zero;
            root.sizeDelta = new Vector2(wheelSize, wheelSize);
            var group = rootGo.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(HitDirectionArrow));
            arrowGo.transform.SetParent(rootGo.transform, false);
            var arrow = (RectTransform)arrowGo.transform;
            arrow.anchorMin = arrow.anchorMax = new Vector2(.5f, 1f);
            arrow.pivot = new Vector2(.5f, .5f);
            arrow.anchoredPosition = new Vector2(0f, -26f);
            arrow.sizeDelta = new Vector2(44f, 36f);
            var graphic = arrowGo.GetComponent<HitDirectionArrow>();
            graphic.color = new Color(1f, .22f, .18f);
            graphic.raycastTarget = false;
            var outline = arrowGo.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, .9f);
            outline.effectDistance = new Vector2(2f, -2f);
            rootGo.SetActive(false);
            segments.Add(new Segment { root = root, group = group });
        }
    }

    /// <summary>Shows an arrow pointing at the attacker. Angle 0 = ahead, positive = right.</summary>
    public void Show(float angleDegrees)
    {
        if (segments.Count == 0) return;
        Segment oldest = segments[0];
        foreach (var segment in segments)
            if (segment.age > oldest.age) oldest = segment;
        oldest.age = 0f;
        oldest.root.gameObject.SetActive(true);
        oldest.root.localRotation = Quaternion.Euler(0f, 0f, -angleDegrees);
        oldest.group.alpha = 1f;
    }

    public void Clear()
    {
        foreach (var segment in segments)
        {
            segment.age = float.PositiveInfinity;
            if (segment.root != null) segment.root.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        float duration = Mathf.Max(.01f, lifetime);
        foreach (var segment in segments)
        {
            if (segment.age == float.PositiveInfinity || segment.root == null || !segment.root.gameObject.activeSelf) continue;
            segment.age += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(segment.age / duration);
            segment.group.alpha = t < .35f ? 1f : 1f - (t - .35f) / .65f;
            if (t >= 1f) segment.root.gameObject.SetActive(false);
        }
    }
}

/// <summary>Solid upward triangle used as the hit direction arrow. Vector-drawn so it
/// never depends on a font glyph.</summary>
public sealed class HitDirectionArrow : MaskableGraphic
{
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect rect = rectTransform.rect;
        float half = Mathf.Min(rect.width, rect.height) * .5f;
        if (half <= 0f) return;
        mesh.AddVert(new Vector2(0f, half), color, Vector2.zero);
        mesh.AddVert(new Vector2(-half * .85f, -half * .7f), color, Vector2.zero);
        mesh.AddVert(new Vector2(half * .85f, -half * .7f), color, Vector2.zero);
        mesh.AddTriangle(0, 1, 2);
    }
}

/// <summary>Persistent threat arrow around the crosshair: tracks one danger point
/// (nearest live grenade) until hidden. Built in code.</summary>
public sealed class ThreatArrow : MonoBehaviour
{
    private RectTransform root;
    private CanvasGroup group;

    private void Awake()
    {
        if (root != null) return;
        // Prefab-authored instance: collect own components.
        root = (RectTransform)transform;
        group = GetComponent<CanvasGroup>();
    }
    public static ThreatArrow Create(Transform parent, Color color)
    {
        var go = new GameObject("Threat Arrow", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var root = (RectTransform)go.transform;
        root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
        root.pivot = new Vector2(.5f, .5f);
        root.anchoredPosition = Vector2.zero;
        root.sizeDelta = new Vector2(240f, 240f);
        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.blocksRaycasts = false;
        var arrowGo = new GameObject("Arrow", typeof(RectTransform), typeof(HitDirectionArrow));
        arrowGo.transform.SetParent(go.transform, false);
        var arrow = (RectTransform)arrowGo.transform;
        arrow.anchorMin = arrow.anchorMax = new Vector2(.5f, 1f);
        arrow.pivot = new Vector2(.5f, .5f);
        arrow.anchoredPosition = new Vector2(0f, -26f);
        arrow.sizeDelta = new Vector2(44f, 36f);
        var graphic = arrowGo.GetComponent<HitDirectionArrow>();
        graphic.color = color;
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
        if (group != null) group.alpha = 1f;
    }

    public void Hide()
    {
        if (root != null) root.gameObject.SetActive(false);
    }
}
