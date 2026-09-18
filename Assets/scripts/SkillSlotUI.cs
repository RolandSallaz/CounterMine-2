using UnityEngine;
using UnityEngine.UI;

/// <summary>Resolution-independent ability card: clockwise kill progress and vector icon.</summary>
public sealed class SkillSlotUI : MonoBehaviour
{
    private SkillSlotGraphic ring, icon;
    private Text caption, count, charges;
    private Image background;
    private Image radarIcon;
    private float displayed, target;
    private bool active, ready, available;
    private static readonly Color Cyan = new Color(.35f, .8f, .95f);
    private static readonly Color Green = new Color(.45f, 1f, .7f);
    private static readonly Color Amber = new Color(1f, .7f, .3f);

    public static SkillSlotUI Create(Transform parent, int slot)
    {
        var go = new GameObject("Skill " + (slot + 3), typeof(RectTransform), typeof(Image), typeof(SkillSlotUI));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(1, .5f);
        rect.pivot = new Vector2(1, .5f);
        rect.anchoredPosition = new Vector2(-28, (1 - slot) * 108);
        rect.sizeDelta = new Vector2(96, 96);
        var card = go.GetComponent<SkillSlotUI>();
        card.background = go.GetComponent<Image>(); card.background.raycastTarget = false;
        card.background.color = new Color(.02f, .035f, .045f, .94f);
        var outline = go.AddComponent<Outline>(); outline.effectColor = new Color(.25f, .4f, .46f, .5f); outline.effectDistance = new Vector2(1, -1);
        card.ring = Graphic(go.transform, "Clockwise Progress", Vector2.zero, new Vector2(64, 64), false);
        card.icon = Graphic(go.transform, "Icon", Vector2.zero, new Vector2(26, 26), true);
        var iconObject = new GameObject("Radar Icon", typeof(RectTransform), typeof(Image));
        iconObject.transform.SetParent(go.transform, false);
        card.radarIcon = iconObject.GetComponent<Image>();
        card.radarIcon.sprite = Resources.Load<Sprite>("UI/Icons/Radar");
        card.radarIcon.preserveAspect = true; card.radarIcon.raycastTarget = false;
        card.radarIcon.rectTransform.anchorMin = Vector2.zero;
        card.radarIcon.rectTransform.anchorMax = Vector2.one;
        card.radarIcon.rectTransform.offsetMin = card.radarIcon.rectTransform.offsetMax = Vector2.zero;
        iconObject.transform.SetAsFirstSibling();
        card.radarIcon.color = new Color(.5f, .8f, .9f, .3f);
        var key = Label(go.transform, "Key", new Vector2(-35, 35), new Vector2(22, 20), 14);
        key.text = (slot + 3).ToString(); key.color = Color.white;
        card.charges = Label(go.transform, "Charges", new Vector2(29, 35), new Vector2(32, 18), 12);
        card.caption = Label(go.transform, "Caption", new Vector2(0, -38), new Vector2(90, 16), 10);
        card.count = Label(go.transform, "Kills Remaining", Vector2.zero, new Vector2(56, 52), 38);
        card.count.fontStyle = FontStyle.Bold;
        var numberOutline = card.count.gameObject.AddComponent<Outline>();
        numberOutline.effectColor = new Color(0, .015f, .02f, .95f); numberOutline.effectDistance = new Vector2(1.5f, -1.5f);
        card.SetState(slot == 0, 0, 0, false);
        return card;
    }
    private static Text Label(Transform parent, string name, Vector2 position, Vector2 size, int fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text)); go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
        text.rectTransform.anchoredPosition = position; text.rectTransform.sizeDelta = size;
        return text;
    }
    private static SkillSlotGraphic Graphic(Transform parent, string name, Vector2 position, Vector2 size, bool icon)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(SkillSlotGraphic)); go.transform.SetParent(parent, false);
        var graphic = go.GetComponent<SkillSlotGraphic>(); graphic.Icon = icon; graphic.raycastTarget = false;
        graphic.rectTransform.anchoredPosition = position; graphic.rectTransform.sizeDelta = size;
        return graphic;
    }
    public void SetState(bool assigned, int progress, int storedCharges, bool running)
    {
        available = assigned; active = running; ready = assigned && storedCharges > 0;
        target = assigned ? Mathf.Clamp01((float)progress / RadarSkill.RequiredKills) : 0;
        if (ready && !active) target = 1;
        icon.Locked = !assigned; icon.SetVerticesDirty();
        icon.enabled = !assigned;
        radarIcon.enabled = assigned && radarIcon.sprite != null;
        caption.text = !assigned ? "EMPTY SLOT" : active ? "RADAR ACTIVE" : ready ? "PRESS 3" : "RADAR";
        count.text = !assigned ? "" : ready && !active ? "0" : (RadarSkill.RequiredKills - progress).ToString();
        charges.text = assigned && storedCharges > 0 ? "x" + storedCharges : "";
    }
    private void Update()
    {
        displayed = Mathf.MoveTowards(displayed, target, Time.unscaledDeltaTime * 1.8f);
        ring.Fill = displayed;
        Color color = !available ? new Color(.3f, .39f, .43f) : active ? Amber : ready ? Green : Cyan;
        if (active) color *= .85f + .15f * Mathf.Sin(Time.unscaledTime * 3);
        color.a = 1;
        ring.color = color; icon.color = color; caption.color = color; charges.color = Green;
        count.color = available ? Color.white : new Color(.4f, .48f, .52f);
    }
}

public sealed class SkillSlotGraphic : MaskableGraphic
{
    public bool Icon, Locked;
    private float fill;
    public float Fill { set { if (Mathf.Approximately(fill, value)) return; fill = value; SetVerticesDirty(); } }
    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear(); float r = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * .47f;
        if (!Icon)
        {
            Arc(mesh, r, 0, 1, 2, new Color(.17f, .25f, .29f, .8f));
            Arc(mesh, r, 0, fill, 3, color);
        }
        else if (Locked)
        {
            Line(mesh, new Vector2(-7, -8), new Vector2(7, -8), 2, color);
            Line(mesh, new Vector2(-7, -8), new Vector2(-7, 2), 2, color);
            Line(mesh, new Vector2(7, -8), new Vector2(7, 2), 2, color);
            Line(mesh, new Vector2(-7, 2), new Vector2(7, 2), 2, color);
            Arc(mesh, 5, -.25f, .5f, 2, color);
        }
        else
        {
            Arc(mesh, r, 0, 1, 1.3f, color); Arc(mesh, r * .52f, 0, 1, 1, color);
            Line(mesh, Vector2.zero, new Vector2(r * .7f, r * .7f), 1.8f, color);
            Line(mesh, new Vector2(-6, 3), new Vector2(-3, 3), 3, color);
            Line(mesh, new Vector2(3, -6), new Vector2(6, -6), 3, color);
        }
    }
    private static void Arc(VertexHelper mesh, float radius, float start, float amount, float width, Color color)
    {
        int segments = Mathf.CeilToInt(Mathf.Clamp01(amount) * 80);
        for (int i = 0; i < segments; i++)
        {
            float a = (start + amount * i / segments) * 2 * Mathf.PI;
            float b = (start + amount * (i + 1) / segments) * 2 * Mathf.PI;
            Line(mesh, new Vector2(Mathf.Sin(a), Mathf.Cos(a)) * radius, new Vector2(Mathf.Sin(b), Mathf.Cos(b)) * radius, width, color);
        }
    }
    private static void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color color)
    {
        var n = new Vector2(-(b-a).y, (b-a).x).normalized * width * .5f; int i = mesh.currentVertCount;
        mesh.AddVert(a-n, color, Vector2.zero); mesh.AddVert(a+n, color, Vector2.zero);
        mesh.AddVert(b+n, color, Vector2.zero); mesh.AddVert(b-n, color, Vector2.zero);
        mesh.AddTriangle(i, i+1, i+2); mesh.AddTriangle(i, i+2, i+3);
    }
}
