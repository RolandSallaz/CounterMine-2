using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Death-screen shop. Purchases persist; selections are applied to the next player life.</summary>
public sealed class DeathShopUI : MonoBehaviour
{
    private static readonly Color Panel = new Color(.045f, .062f, .078f, .98f);
    private static readonly Color Card = new Color(.075f, .098f, .12f, 1);
    private static readonly Color Accent = new Color(.35f, .79f, .94f);
    private static readonly Color Muted = new Color(.59f, .67f, .71f);
    private PlayerHealth owner;
    private Action respawn;
    private Text balance, message, selection;
    private RectTransform content;
    private readonly List<Row> rows = new List<Row>();
    private readonly List<Button> tabs = new List<Button>();
    private ShopCategory category;
    private float nextRefresh, nextClick;
    private bool preview;
    private Func<bool> canPrepareLoadout;
    private YandexPlayerData previewData;
    private sealed class Row { public ShopCatalog.Item item; public Text badge, price, action; public Button button; }
    public bool IsOpen => gameObject.activeSelf;
    private YandexPlayerData Data => previewData ?? YandexPlayerData.Current;
    private bool Loaded => preview || YandexPlayerData.IsLoaded;
    private bool CanUse => canPrepareLoadout != null ? canPrepareLoadout() : owner != null && owner.IsDead;

    public static DeathShopUI Create(Transform parent, PlayerHealth owner, Action respawn, Func<bool> canPrepareLoadout = null)
    {
        var root = new GameObject("Death Shop", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(DeathShopUI));
        root.transform.SetParent(parent, false); Stretch((RectTransform)root.transform);
        root.GetComponent<Image>().color = new Color(.005f,.012f,.02f,.86f);
        var group = root.GetComponent<CanvasGroup>(); group.ignoreParentGroups = true; group.interactable = true; group.blocksRaycasts = true;
        var shop = root.GetComponent<DeathShopUI>(); shop.owner = owner; shop.respawn = respawn; shop.canPrepareLoadout = canPrepareLoadout;
        shop.Build(); root.SetActive(false); return shop;
    }
    public void Bind(PlayerHealth player) { owner = player; Close(); }
    public void Open()
    {
        if (!preview && !CanUse) return;
        gameObject.SetActive(true); transform.SetAsLastSibling();
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        GameLocalization.Bind(message, "Покупки остаются навсегда. Выбор применяется после возрождения.");
        ShowCategory(category);
    }
    public void Close() => gameObject.SetActive(false);
    public void ShowPreview(YandexPlayerData data, ShopCategory tab)
    {
        preview = true; previewData = data; category = tab; Open();
    }
    private void Update()
    {
        if (!preview && !CanUse) { Close(); return; }
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true) { Close(); return; }
        if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + .2f; Refresh(); }
    }
    private void Build()
    {
        var panel = Rect("Shop Panel", transform, new Vector2(.08f,.08f), new Vector2(.92f,.92f));
        panel.gameObject.AddComponent<Image>().color = Panel;
        var title = Label(panel, "Title", GameLocalization.T("МАГАЗИН"), 34, Color.white);
        Place(title.rectTransform, new Vector2(0,1), new Vector2(.6f,1), new Vector2(28,-64), new Vector2(-12,-20));
        var subtitle = Label(panel, "Subtitle", GameLocalization.T("СНАРЯЖЕНИЕ ДЛЯ СЛЕДУЮЩЕГО ВЫХОДА"), 13, Muted);
        Place(subtitle.rectTransform, new Vector2(0,1), new Vector2(.65f,1), new Vector2(30,-89), new Vector2(0,-67));
        balance = Label(panel, "Balance", "", 23, Accent); balance.alignment = TextAnchor.MiddleRight;
        Place(balance.rectTransform, new Vector2(.62f,1), Vector2.one, new Vector2(0,-70), new Vector2(-92,-25));
        var close = Button(panel, "Close", "×", Close);
        Place(close.GetComponent<RectTransform>(), Vector2.one, Vector2.one, new Vector2(-68,-70), new Vector2(-24,-26));
        string[] titles = { GameLocalization.T("ОСНОВНОЕ ОРУЖИЕ"), GameLocalization.T("ПИСТОЛЕТЫ"), GameLocalization.T("НАВЫКИ / КИЛЛСТРИКИ") };
        for (int i = 0; i < titles.Length; i++)
        {
            int index = i;
            var tab = Button(panel, titles[i], titles[i], () => { Click(); ShowCategory((ShopCategory)index); }); tabs.Add(tab);
            Place(tab.GetComponent<RectTransform>(), new Vector2(i/3f,1), new Vector2((i+1)/3f,1), new Vector2(i == 0 ? 28 : 5,-147), new Vector2(i == 2 ? -28 : -5,-103));
        }
        var scrollRoot = Rect("Items", panel, Vector2.zero, Vector2.one);
        scrollRoot.offsetMin = new Vector2(28,127); scrollRoot.offsetMax = new Vector2(-28,-163);
        var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = Rect("Viewport", scrollRoot, Vector2.zero, Vector2.one);
        viewport.gameObject.AddComponent<Image>().color = Color.white;
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        content = Rect("Content", viewport, new Vector2(0,1), Vector2.one); content.pivot = new Vector2(.5f,1);
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 14; layout.childControlWidth = true;
        layout.childControlHeight = true; layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content; scroll.viewport = viewport; scroll.scrollSensitivity = 28;
        selection = Label(panel, "Loadout", "", 15, Muted);
        Place(selection.rectTransform, Vector2.zero, new Vector2(.70f,0), new Vector2(30,69), new Vector2(0,115));
        message = Label(panel, "Message", "", 15, Accent);
        Place(message.rectTransform, Vector2.zero, new Vector2(.70f,0), new Vector2(30,20), new Vector2(0,68));
        var done = Button(panel, "Return to death screen", GameLocalization.T("ГОТОВО"), () => { Click(); Close(); });
        Place(done.GetComponent<RectTransform>(), new Vector2(.74f,0), new Vector2(1,0), new Vector2(0,28), new Vector2(-28,86));
    }
    private void ShowCategory(ShopCategory next)
    {
        category = next;
        for (int i = content.childCount - 1; i >= 0; i--)
        {
            var child = content.GetChild(i).gameObject; child.SetActive(false);
            if (Application.isPlaying) Destroy(child); else DestroyImmediate(child);
        }
        rows.Clear();
        foreach (var item in ShopCatalog.Items) if (item != null && item.category == category) AddRow(item);
        content.anchoredPosition = Vector2.zero;
        for (int i = 0; i < tabs.Count; i++) tabs[i].GetComponent<Image>().color = i == (int)category ? new Color(.12f,.34f,.42f) : Card;
        Refresh();
    }
    private void AddRow(ShopCatalog.Item item)
    {
        var card = Rect(item.id, content, Vector2.zero, Vector2.one);
        card.gameObject.AddComponent<Image>().color = Card;
        var layout = card.gameObject.AddComponent<LayoutElement>(); layout.preferredHeight = 196; layout.minHeight = 196;
        var art = Rect("Preview", card, new Vector2(.015f,.1f), new Vector2(.27f,.91f));
        var image = art.gameObject.AddComponent<Image>(); image.sprite = Resources.Load<Sprite>(item.icon);
        image.preserveAspect = true; image.raycastTarget = false; image.enabled = image.sprite != null;
        var name = Label(card, "Name", item.title, 23, Color.white);
        Place(name.rectTransform, new Vector2(.29f,.67f), new Vector2(.74f,.94f), Vector2.zero, Vector2.zero);
        var description = Label(card, "Description", item.description, 17, Muted);
        Place(description.rectTransform, new Vector2(.29f,.31f), new Vector2(.73f,.66f), Vector2.zero, Vector2.zero);
        var details = Label(card, "Details", item.detail, 13, Accent);
        Place(details.rectTransform, new Vector2(.29f,.10f), new Vector2(.74f,.30f), Vector2.zero, Vector2.zero);
        var row = new Row { item = item };
        row.badge = Label(card, "Ownership", "", 13, Muted); row.badge.alignment = TextAnchor.MiddleCenter;
        Place(row.badge.rectTransform, new Vector2(.76f,.74f), new Vector2(.975f,.94f), Vector2.zero, Vector2.zero);
        row.price = Label(card, "Price", "", 24, Color.white); row.price.alignment = TextAnchor.MiddleCenter;
        Place(row.price.rectTransform, new Vector2(.76f,.44f), new Vector2(.975f,.75f), Vector2.zero, Vector2.zero);
        row.button = Button(card, "Purchase or equip", "", () => Use(item));
        Place(row.button.GetComponent<RectTransform>(), new Vector2(.76f,.12f), new Vector2(.975f,.39f), Vector2.zero, Vector2.zero);
        row.action = row.button.GetComponentInChildren<Text>(); rows.Add(row);
    }
    private void Use(ShopCatalog.Item item)
    {
        if (preview || !CanUse || !Loaded || Time.unscaledTime < nextClick) return;
        nextClick = Time.unscaledTime + .3f;
        if (item.category == ShopCategory.Skill && Data.IsEquipped(item.id))
        {
            Data.UnequipSkill(item.id); GameLocalization.Bind(message, "Навык снят со следующего снаряжения.");
        }
        else { Data.TryPurchaseAndEquip(item.id, out string result); GameLocalization.Bind(message, result); }
        Click(); Refresh();
    }
    private void Refresh()
    {
        balance.text = Loaded ? Data.money.ToString("N0") + "  $" : GameLocalization.T("ЗАГРУЗКА…");
        foreach (var row in rows)
        {
            bool owned = Data.Owns(row.item.id), selected = Data.IsEquipped(row.item.id);
            row.badge.text = selected ? GameLocalization.T("В СНАРЯЖЕНИИ") : owned ? GameLocalization.T("КУПЛЕНО НАВСЕГДА") : GameLocalization.T("ПОСТОЯННАЯ ПОКУПКА");
            row.badge.color = selected ? Accent : Muted;
            row.price.text = owned ? GameLocalization.T("КЛАВИША ") + row.item.key : row.item.price.ToString("N0") + " $";
            row.action.text = !Loaded ? GameLocalization.T("ЗАГРУЗКА…") : !owned ? Data.money >= row.item.price ? GameLocalization.T("КУПИТЬ И ВЫБРАТЬ") : GameLocalization.T("НЕ ХВАТАЕТ ДЕНЕГ") :
                selected ? row.item.category == ShopCategory.Skill ? GameLocalization.T("УБРАТЬ") : GameLocalization.T("ВЫБРАНО") : GameLocalization.T("ВЫБРАТЬ");
            row.button.interactable = Loaded && (owned || Data.money >= row.item.price) && (!selected || row.item.category == ShopCategory.Skill);
        }
        var skills = Data.equippedSkills.Count == 0 ? GameLocalization.T("не выбраны") : string.Join(", ", Data.equippedSkills.ConvertAll(id => GameLocalization.T(ShopCatalog.Find(id)?.title ?? id)));
        selection.text = GameLocalization.T("СЛЕДУЮЩЕЕ СНАРЯЖЕНИЕ\n") + GameLocalization.T(ShopCatalog.Find(Data.equippedWeapon)?.title ?? "AK-74") + "  /  " +
            GameLocalization.T(ShopCatalog.Find(Data.equippedPistol)?.title ?? "без пистолета") + "  /  " + skills;
    }
    private static void Click() => GameAudio.Effect("UI/click", Vector3.zero, .5f, 1, true);
    private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); rect.SetParent(parent, false);
        Place(rect, min, max, Vector2.zero, Vector2.zero); return rect;
    }
    private static void Stretch(RectTransform rect) => Place(rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
    private static void Place(RectTransform rect, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = offsetMin; rect.offsetMax = offsetMax; }
    private static Text Label(Transform parent, string name, string value, int size, Color color)
    {
        var rect = Rect(name, parent, Vector2.zero, Vector2.one); var text = rect.gameObject.AddComponent<Text>();
        text.font = GameUIStyle.Font; GameLocalization.Bind(text, value); text.fontSize = size; text.color = color; text.alignment = TextAnchor.MiddleLeft;
        text.resizeTextForBestFit = true; text.resizeTextMinSize = Mathf.Min(size,12); text.resizeTextMaxSize = size;
        text.raycastTarget = false; return text;
    }
    private static Button Button(Transform parent, string name, string label, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(name, parent, Vector2.zero, Vector2.one); var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.10f,.24f,.31f);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = button.colors; colors.highlightedColor = new Color(.7f,.95f,1f); colors.disabledColor = new Color(.45f,.5f,.53f,.6f); button.colors = colors;
        button.onClick.AddListener(action); var text = Label(rect, "Label", label, 16, Color.white); text.alignment = TextAnchor.MiddleCenter;
        text.rectTransform.offsetMin = new Vector2(8,4); text.rectTransform.offsetMax = new Vector2(-8,-4); return button;
    }
}
