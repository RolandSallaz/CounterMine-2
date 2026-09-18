using UnityEngine;
using UnityEngine.UI;

/// <summary>Texture-free wound vignette. Keeps the center clear and draws beneath the HUD.</summary>
public sealed class HealthScreenEffect : MaskableGraphic
{
    [SerializeField, Range(.05f, .75f)] private float lowHealthThreshold = .35f;
    private float hit, severity, heartbeat;
    private float displayedHit = -1f, displayedLow = -1f;
    private float lowPulse;

    public static HealthScreenEffect Create(Transform parent)
    {
        var go = new GameObject("Wound and Low Health", typeof(RectTransform), typeof(CanvasRenderer), typeof(HealthScreenEffect));
        go.transform.SetParent(parent, false);
        go.transform.SetAsFirstSibling();
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        var effect = go.GetComponent<HealthScreenEffect>();
        effect.raycastTarget = false;
        return effect;
    }

    public void Wound(float damageFraction)
    {
        // Even a light hit is readable; successive hits build up to a bounded peak.
        hit = Mathf.Clamp01(hit + .4f + Mathf.Clamp01(damageFraction) * 1.2f);
    }

    public void Clear()
    {
        hit = severity = heartbeat = lowPulse = 0f;
        displayedHit = displayedLow = -1f;
        canvasRenderer.SetAlpha(0f);
        SetVerticesDirty();
    }

    public void Tick(float healthFraction, bool dead, float deltaTime)
    {
        if (dead) { if (hit > 0f || severity > 0f) Clear(); return; }
        float target = healthFraction < lowHealthThreshold
            ? Mathf.Lerp(.3f, 1f, Mathf.Clamp01(1f - healthFraction / lowHealthThreshold)) : 0f;
        severity = Mathf.Lerp(severity, target, 1f - Mathf.Exp(-deltaTime * (target > severity ? 9f : 4f)));
        hit *= Mathf.Exp(-deltaTime * 5.5f);
        if (hit < .002f) hit = 0f;
        if (target == 0f && severity < .002f) severity = 0f;
        heartbeat = Mathf.Repeat(heartbeat + deltaTime * Mathf.Lerp(1.05f, 1.8f, severity), 1f);
        // Two close pulses, then a quiet interval: a heartbeat rather than a flashing screen.
        float first = Mathf.Exp(-Mathf.Pow((heartbeat - .12f) / .065f, 2f));
        float second = .55f * Mathf.Exp(-Mathf.Pow((heartbeat - .32f) / .085f, 2f));
        lowPulse = severity * (.58f + .42f * Mathf.Max(first, second));
        canvasRenderer.SetAlpha(hit > 0f || severity > 0f ? 1f : 0f);
        if (Mathf.Abs(displayedHit - hit) + Mathf.Abs(displayedLow - lowPulse) < .003f) return;
        displayedHit = hit;
        displayedLow = lowPulse;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        const int columns = 48, rows = 28;
        Rect rect = rectTransform.rect;
        for (int y = 0; y <= rows; y++)
            for (int x = 0; x <= columns; x++)
            {
                float u = x / (float)columns, v = y / (float)rows;
                float nx = Mathf.Abs(u * 2f - 1f), ny = Mathf.Abs(v * 2f - 1f);
                float edge = 1f - (1f - Mathf.Pow(nx, 4f)) * (1f - Mathf.Pow(ny, 4f));
                float lowHealthEdge = 1f - (1f - nx * nx) * (1f - ny * ny);
                float boundary = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.035f, .85f, lowHealthEdge));
                float ripple = Mathf.Sin(u * 79f + Mathf.Sin(v * 33f) * 2f) * Mathf.Sin(v * 61f + u * 17f);
                float wound = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.28f + ripple * .08f, 1f, edge));
                float alpha = Mathf.Min(.88f, boundary * lowPulse * .9f + wound * hit * .78f);
                Color tint = Color.Lerp(new Color(.9f, .008f, .018f), new Color(.72f, .025f, .035f), hit / Mathf.Max(.001f, hit + lowPulse));
                tint.a = alpha;
                mesh.AddVert(new Vector3(rect.xMin + u * rect.width, rect.yMin + v * rect.height), tint, Vector2.zero);
            }
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < columns; x++)
            {
                int i = y * (columns + 1) + x;
                mesh.AddTriangle(i, i + columns + 1, i + 1);
                mesh.AddTriangle(i + 1, i + columns + 1, i + columns + 2);
            }
    }
}
