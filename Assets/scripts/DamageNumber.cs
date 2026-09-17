using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Floating world-space damage numbers at the hit point. Spawned only on the
/// shooter's client (see NetworkWeapon): rise, then fade out quickly.</summary>
public sealed class DamageNumber : MonoBehaviour
{
    [SerializeField, Min(.1f)] private float lifetime = .8f;
    [SerializeField, Min(0f)] private float riseSpeed = .9f;
    private const int MaxActive = 30;

    private static readonly List<DamageNumber> active = new List<DamageNumber>();
    private static Camera viewCamera;
    private static Font cachedFont;

    private Text label;
    private CanvasGroup group;
    private float age;

    public static void Spawn(Vector3 position, int amount, bool crit)
    {
        if (amount <= 0) return;
        if (viewCamera == null || !viewCamera.isActiveAndEnabled) viewCamera = FindViewCamera();
        if (cachedFont == null)
        {
            cachedFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (cachedFont == null) cachedFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (cachedFont == null) return;
        }
        while (active.Count >= MaxActive)
        {
            var oldest = active[0];
            active.RemoveAt(0);
            if (oldest != null) Destroy(oldest.gameObject);
        }

        var go = new GameObject("Damage Number");
        go.transform.position = position + Random.insideUnitSphere * .15f + Vector3.up * .1f;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(2.4f, 1.2f);
        float scale = crit ? .011f : .008f;
        go.transform.localScale = Vector3.one * scale;

        var text = go.AddComponent<Text>();
        text.font = cachedFont;
        text.fontSize = 64;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = false;
        text.color = crit ? new Color(1f, .15f, .1f) : new Color(1f, .3f, .25f);
        text.text = amount.ToString();
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, .85f);
        outline.effectDistance = new Vector2(3f, -3f);

        var number = go.AddComponent<DamageNumber>();
        number.label = text;
        number.group = go.AddComponent<CanvasGroup>();
        active.Add(number);
    }

    /// <summary>MainCamera tag first (player camera), otherwise any enabled camera.</summary>
    private static Camera FindViewCamera()
    {
        var tagged = Camera.main;
        if (tagged != null && tagged.isActiveAndEnabled) return tagged;
        foreach (var camera in Camera.allCameras)
            if (camera != null && camera.isActiveAndEnabled) return camera;
        return null;
    }

    private void Update()
    {
        age += Time.deltaTime;
        transform.position += Vector3.up * riseSpeed * Time.deltaTime;
        if (viewCamera == null || !viewCamera.isActiveAndEnabled) viewCamera = FindViewCamera();
        if (viewCamera != null) transform.rotation = viewCamera.transform.rotation;
        if (group != null) group.alpha = 1f - Mathf.Clamp01((age - (lifetime - .3f)) / .3f);
        if (age >= lifetime)
        {
            active.Remove(this);
            Destroy(gameObject);
        }
    }
}
