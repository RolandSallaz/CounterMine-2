using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Floating world-space damage numbers at the hit point. Purely local:
/// only the shooter's client spawns them (see NetworkWeapon) and only for enemy
/// hits. Rise, then fade out quickly.</summary>
public sealed class DamageNumber : MonoBehaviour
{
    [SerializeField, Min(.1f)] private float lifetime = .8f;
    [SerializeField, Min(0f)] private float riseSpeed = .9f;
    private const int MaxActive = 30;

    private static readonly List<DamageNumber> active = new List<DamageNumber>();
    private static readonly Stack<DamageNumber> pool = new Stack<DamageNumber>();
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
        DamageNumber number = null;
        while (active.Count >= MaxActive)
        {
            var oldest = active[0];
            active.RemoveAt(0);
            if (oldest != null) { number = oldest; break; }
        }
        while (number == null && pool.Count > 0) number = pool.Pop();
        if (number == null) number = Create();
        number.age = 0;
        number.group.alpha = 1;
        number.transform.position = position + Random.insideUnitSphere * .15f + Vector3.up * .1f;
        number.transform.localScale = Vector3.one * (crit ? .011f : .008f);
        number.label.color = crit ? new Color(1f, .1f, .1f) : new Color(1f, .3f, .3f);
        number.label.text = amount.ToString();
        if (viewCamera != null) number.transform.rotation = viewCamera.transform.rotation;
        number.gameObject.SetActive(true);
        active.Add(number);
    }
    private static DamageNumber Create()
    {
        var go = new GameObject("Damage Number");
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rect = (RectTransform)go.transform;
        rect.sizeDelta = new Vector2(2.4f, 1.2f);

        var text = go.AddComponent<Text>();
        text.font = cachedFont;
        text.fontSize = 64;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.supportRichText = false;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, .85f);
        outline.effectDistance = new Vector2(3f, -3f);

        var number = go.AddComponent<DamageNumber>();
        number.label = text;
        number.group = go.AddComponent<CanvasGroup>();
        number.group.blocksRaycasts = false;
        return number;
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
            gameObject.SetActive(false);
            if (pool.Count < MaxActive) pool.Push(this); else Destroy(gameObject);
        }
    }
    private void OnDestroy() => active.Remove(this);
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPools() { active.Clear(); pool.Clear(); viewCamera = null; cachedFont = null; }
}
