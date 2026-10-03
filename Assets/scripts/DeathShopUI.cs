using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Death-screen shop. Purchases persist; selections are applied to the next player life.</summary>
public sealed class DeathShopUI : MonoBehaviour
{
    private static readonly Color Panel = GameUIStyle.Panel;
    private static readonly Color Card = GameUIStyle.Card;
    private static readonly Color Accent = GameUIStyle.Accent;
    private static readonly Color Muted = GameUIStyle.Muted;
    private PlayerHealth owner;
    private Action respawn;
    private Text balance, message, selection;
    private RectTransform content;
    private ShopWeaponPreview weaponPreview;
    private ShopCatalog.Item inspected;
    private Text itemTitle, itemDescription, itemStats, statValues, itemPrice, itemAction, rotateHint;
    private Button itemButton;
    private Image skillImage;
    private RectTransform shopPanel;
    private CanvasGroup visibility;
    private float reveal;
    private readonly List<Row> rows = new List<Row>();
    private readonly List<Button> tabs = new List<Button>();
    private ShopCategory category;
    private float nextRefresh, nextClick;
    private bool preview;
    private bool mainMenu;
    public event Action<string> ItemEquipped;
    private Func<bool> canPrepareLoadout;
    private YandexPlayerData previewData;
    private sealed class Row { public ShopCatalog.Item item; public Text badge, price, action; public Button button; public Image surface, marker; }
    public bool IsOpen => gameObject.activeSelf;
    private YandexPlayerData Data => previewData ?? YandexPlayerData.Current;
    private bool Loaded => preview || YandexPlayerData.IsLoaded;
    private bool CanUse => canPrepareLoadout != null ? canPrepareLoadout() : owner != null && owner.IsDead;

    public static DeathShopUI Create(Transform parent, PlayerHealth owner, Action respawn, Func<bool> canPrepareLoadout = null, bool mainMenu = false)
    {
        var root = new GameObject("Death Shop", typeof(RectTransform), typeof(Image), typeof(CanvasGroup), typeof(DeathShopUI));
        root.transform.SetParent(parent, false); Stretch((RectTransform)root.transform);
        root.GetComponent<Image>().color = new Color(.005f,.012f,.02f,.86f);
        var group = root.GetComponent<CanvasGroup>(); group.ignoreParentGroups = true; group.interactable = true; group.blocksRaycasts = true;
        var shop = root.GetComponent<DeathShopUI>(); shop.owner = owner; shop.respawn = respawn; shop.canPrepareLoadout = canPrepareLoadout;
        shop.mainMenu = mainMenu;
        if (mainMenu) root.GetComponent<Image>().color = new Color(.005f, .012f, .02f, .92f);
        shop.Build(); root.SetActive(false); return shop;
    }
    public void Bind(PlayerHealth player) { owner = player; Close(); }
    public void Open()
    {
        if (!preview && !CanUse) return;
        gameObject.SetActive(true); transform.SetAsLastSibling();
        reveal = 0; visibility.alpha = 0;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        GameLocalization.Bind(message, mainMenu ? "Покупки навсегда. Снаряжение готово к следующему бою." : "Покупки остаются навсегда. Выбор применяется после возрождения.");
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
        reveal = Mathf.Min(1, reveal + Time.unscaledDeltaTime / .18f);
        visibility.alpha = Mathf.SmoothStep(0, 1, reveal);

        shopPanel.anchoredPosition = Vector2.up * (1 - visibility.alpha) * -10f;
        if (Time.unscaledTime >= nextRefresh) { nextRefresh = Time.unscaledTime + .2f; Refresh(); }
    }
    private void Build()
    {
        var panel = Rect("Shop Panel", transform, Vector2.zero, Vector2.one);
        shopPanel = panel; visibility = GetComponent<CanvasGroup>();
        GameUIStyle.Surface(panel.gameObject.AddComponent<Image>(), Panel);
        // The model is the full-screen background; all shop controls sit above it.
        BuildInspector(panel);
        var title = Label(panel, "Title", "МАГАЗИН / СНАРЯЖЕНИЕ", 20, Muted);
        Place(title.rectTransform, new Vector2(0,1), new Vector2(.4f,1), new Vector2(20,-77), new Vector2(0,-48));
        balance = Label(panel, "Balance", "", 20, GameUIStyle.Text); balance.alignment = TextAnchor.MiddleRight;
        Place(balance.rectTransform, new Vector2(.75f,1), Vector2.one, new Vector2(0,-48), new Vector2(-75,-15));
        var close = Button(panel, "Close", "×", Close);
        Place(close.GetComponent<RectTransform>(), Vector2.one, Vector2.one, new Vector2(-60,-49), new Vector2(-20,-15));
        string[] titles = { "ОСНОВНОЕ ОРУЖИЕ", "ПИСТОЛЕТЫ", "НАВЫКИ" };
        for (int i = 0; i < titles.Length; i++)
        {
            int index = i;
            var tab = Button(panel, titles[i], titles[i], () => { Click(); ShowCategory((ShopCategory)index); }); tabs.Add(tab);
            Place(tab.GetComponent<RectTransform>(), new Vector2(.30f+i*.15f,0), new Vector2(.45f+i*.15f,0), new Vector2(5,12), new Vector2(-5,48));
            var underline = Rect("Active underline", tab.transform, Vector2.zero, new Vector2(1,0));
            underline.sizeDelta = new Vector2(0,2);
            var line = underline.gameObject.AddComponent<Image>(); line.color = Accent; line.raycastTarget = false;
        }
        var scrollRoot = Rect("Items", panel, Vector2.zero, Vector2.one);
        scrollRoot.anchorMax = new Vector2(.18f,1);
        scrollRoot.offsetMin = new Vector2(16,65); scrollRoot.offsetMax = new Vector2(-4,-90);
        var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
        var viewport = Rect("Viewport", scrollRoot, Vector2.zero, Vector2.one);
        viewport.offsetMax = new Vector2(-12, 0);
        viewport.gameObject.AddComponent<RectMask2D>();
        var track = Rect("Scroll Track", scrollRoot, new Vector2(1,0), Vector2.one);
        track.offsetMin = new Vector2(-5,0);
        GameUIStyle.Surface(track.gameObject.AddComponent<Image>(), new Color(1,1,1,.06f));
        var thumb = Rect("Thumb", track, Vector2.zero, Vector2.one);
        var thumbImage = thumb.gameObject.AddComponent<Image>(); GameUIStyle.Surface(thumbImage, Muted);
        var scrollbar = track.gameObject.AddComponent<Scrollbar>();
        scrollbar.handleRect = thumb; scrollbar.targetGraphic = thumbImage; scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        content = Rect("Content", viewport, new Vector2(0,1), Vector2.one); content.pivot = new Vector2(.5f,1);
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>(); layout.spacing = 3; layout.childControlWidth = true;
        layout.childControlHeight = true; layout.childForceExpandHeight = false; layout.childForceExpandWidth = true;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = content; scroll.viewport = viewport; scroll.scrollSensitivity = 28;
        selection = Label(panel, "Loadout", "", 13, Muted);
        Place(selection.rectTransform, new Vector2(.48f,0), new Vector2(.73f,0), new Vector2(0,100), new Vector2(0,144));
        message = Label(panel, "Message", "", 13, Muted);
        Place(message.rectTransform, new Vector2(.48f,0), new Vector2(.97f,0), new Vector2(0,61), new Vector2(0,99));
        var done = Button(panel, "Return to death screen", "НАЗАД", () => { Click(); Close(); });
        done.GetComponent<Image>().color = new Color(.86f,.87f,.85f);
        done.GetComponentInChildren<Text>().color = Color.black;
        Place(done.GetComponent<RectTransform>(), Vector2.zero, new Vector2(.13f,0), new Vector2(16,17), new Vector2(0,43));
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
        for (int i = 0; i < tabs.Count; i++)
        {
            bool selected = i == (int)category;
            tabs[i].GetComponent<Image>().color = Color.clear;
            tabs[i].GetComponentInChildren<Text>().color = selected ? Color.white : Muted;
            tabs[i].transform.Find("Active underline").gameObject.SetActive(selected);
        }
        Inspect(rows.Find(r => Data.IsEquipped(r.item.id))?.item ?? (rows.Count > 0 ? rows[0].item : null));
        Refresh();
    }
    private void AddRow(ShopCatalog.Item item)
    {
        var card = Rect(item.id, content, Vector2.zero, Vector2.one);
        var surface = card.gameObject.AddComponent<Image>(); surface.color = new Color(.075f,.078f,.08f,.85f);
        var button = card.gameObject.AddComponent<Button>(); button.targetGraphic = surface;
        button.onClick.AddListener(() => { Click(); Inspect(item); });
        var layout = card.gameObject.AddComponent<LayoutElement>(); layout.preferredHeight = layout.minHeight = 86;
        var marker = Rect("Equipped Marker", card, Vector2.zero, new Vector2(0,1));
        marker.offsetMin = new Vector2(0,10); marker.offsetMax = new Vector2(3,-10);
        var markerImage = marker.gameObject.AddComponent<Image>(); markerImage.color = Accent;
        var art = Rect("Thumbnail", card, new Vector2(.15f,.25f), new Vector2(.85f,.70f));
        var image = art.gameObject.AddComponent<Image>(); image.sprite = Resources.Load<Sprite>(item.icon);
        image.preserveAspect = true; image.raycastTarget = false;
        var name = Label(card, "Name", item.title, 16, GameUIStyle.Text);
        Place(name.rectTransform, new Vector2(.04f,.70f), new Vector2(.96f,.98f), Vector2.zero,Vector2.zero);
        name.alignment = TextAnchor.MiddleCenter;
        var row = new Row { item = item, surface = surface, marker = markerImage, button = button };
        row.badge = Label(card,"Ownership","",16,Muted);
        Place(row.badge.rectTransform,new Vector2(.04f,.01f),new Vector2(.96f,.24f),Vector2.zero,Vector2.zero);
        rows.Add(row);
    }
    private static string L(string ru, string en) => GameLocalization.Language == "ru" ? ru : en;
    private void BuildInspector(RectTransform panel)
    {
        var root = Rect("Weapon inspector", panel, Vector2.zero, Vector2.one);
        var view = Rect("Interactive model", root, Vector2.zero, Vector2.one);
        view.gameObject.AddComponent<RawImage>(); weaponPreview = view.gameObject.AddComponent<ShopWeaponPreview>();
        var fallback = Rect("Skill icon", root, new Vector2(.48f,.36f), new Vector2(.79f,.73f));
        skillImage = fallback.gameObject.AddComponent<Image>(); skillImage.preserveAspect = true; skillImage.raycastTarget = false;

        // Thin grid and floating type follow the reference's open inspection stage.
        for (int i = 1; i < 32; i++)
        {
            var line = Rect("Stage grid", root, new Vector2(.18f+i*.8f/32f, .12f), new Vector2(.18f+i*.8f/32f,.89f));
            line.sizeDelta = new Vector2(1,0);
            var ink = line.gameObject.AddComponent<Image>(); ink.color = new Color(1,1,1,.001f); ink.raycastTarget = false;
        }
        for (int i = 4; i < 25; i++)
        {
            var line = Rect("Stage grid", root, new Vector2(.18f,i/28f), new Vector2(.98f,i/28f));
            line.sizeDelta = new Vector2(0,1);
            var ink = line.gameObject.AddComponent<Image>(); ink.color = new Color(1,1,1,.001f); ink.raycastTarget = false;
        }
        itemTitle = Label(root,"Weapon title","",42,GameUIStyle.Text);
        itemTitle.alignment = TextAnchor.MiddleCenter;
        Place(itemTitle.rectTransform,new Vector2(.30f,.80f),new Vector2(.85f,.91f),Vector2.zero,Vector2.zero);
        itemDescription = Label(root,"Weapon description","",15,Muted);
        itemDescription.alignment = TextAnchor.UpperCenter;
        Place(itemDescription.rectTransform,new Vector2(.30f,.74f),new Vector2(.85f,.80f),Vector2.zero,Vector2.zero);
        rotateHint = Label(root,"Rotate hint","",13,Muted);
        Place(rotateHint.rectTransform,new Vector2(.48f,.26f),new Vector2(.87f,.30f),Vector2.zero,Vector2.zero);
        var reset = Button(root,"Reset view","",() => weaponPreview.ResetView());
        GameLocalization.Bind(reset.GetComponentInChildren<Text>(), () => L("СБРОС","RESET"));
        Place(reset.GetComponent<RectTransform>(),new Vector2(.88f,.26f),new Vector2(.97f,.30f),Vector2.zero,Vector2.zero);
        var details = Rect("Weapon statistics", root, new Vector2(.19f,.14f), new Vector2(.43f,.39f));
        itemStats = Label(details,"Stat labels","",14,GameUIStyle.Text);
        statValues = Label(details,"Stat values","",14,GameUIStyle.Text);
        Place(itemStats.rectTransform,Vector2.zero,new Vector2(.73f,1),Vector2.zero,Vector2.zero);
        Place(statValues.rectTransform,new Vector2(.73f,0),Vector2.one,Vector2.zero,Vector2.zero);
        itemStats.alignment = TextAnchor.UpperLeft; statValues.alignment = TextAnchor.UpperRight;
        itemStats.resizeTextForBestFit = statValues.resizeTextForBestFit = false;
        itemStats.fontSize = statValues.fontSize = 14;
        itemStats.lineSpacing = statValues.lineSpacing = 1.3f;
        for(int i = 1; i <= 7; i++)
        {
            var rule = Rect("Stat rule",details,new Vector2(0,1),Vector2.one);
            rule.offsetMin = new Vector2(0,-i*18); rule.offsetMax = new Vector2(0,-i*18+1);
            var ink = rule.gameObject.AddComponent<Image>(); ink.color = new Color(1,1,1,.18f); ink.raycastTarget = false;
        }
        itemPrice = Label(root,"Weapon price","",18,GameUIStyle.Text);
        itemPrice.alignment = TextAnchor.MiddleRight;
        Place(itemPrice.rectTransform,new Vector2(.76f,.19f),new Vector2(.97f,.24f),Vector2.zero,Vector2.zero);
        itemButton = Button(root,"Purchase or equip","",() => { if(inspected != null) Use(inspected); });
        Place(itemButton.GetComponent<RectTransform>(),new Vector2(.76f,.14f),new Vector2(.97f,.19f),Vector2.zero,Vector2.zero);
        itemButton.GetComponent<Image>().color = new Color(.86f,.87f,.85f);
        itemAction = itemButton.GetComponentInChildren<Text>(); itemAction.color = Color.black;
    }
    private void Inspect(ShopCatalog.Item item)
    {
        inspected = item;
        if(item == null) return;
        weaponPreview.Show(item.id);
        skillImage.sprite = Resources.Load<Sprite>(item.icon);
        skillImage.enabled = !weaponPreview.HasModel && skillImage.sprite != null;
        Refresh();
    }
    private void RefreshInspector()
    {
        if(inspected == null) return;
        itemTitle.text = GameLocalization.T(inspected.title);
        itemDescription.text = GameLocalization.T(inspected.description);
        var entry = weaponPreview.Entry;
        itemStats.text = entry == null ? GameLocalization.T(inspected.detail) :
            L("Урон\nМагазин\nТемп, выстр./мин\nСкорость пули, м/с\nПолный урон, м\nДальность, м\nРежим огня",
              "Damage\nMagazine\nFire rate, RPM\nVelocity, m/s\nFull damage, m\nRange, m\nFire mode");
        statValues.text = entry == null ? "" : entry.damage + (entry.pelletCount > 1 ? " × " + entry.pelletCount : "") +
            "\n" + entry.magazineSize + "\n" + Mathf.RoundToInt(entry.roundsPerMinute) + "\n" + Mathf.RoundToInt(entry.muzzleVelocity) +
            "\n" + entry.fullDamageRange.ToString("0") + "\n" + entry.maximumRange.ToString("0") + "\n" +
            (entry.automatic ? L("АВТО", "AUTO") : L("ОДИН.", "SEMI"));
        rotateHint.text = weaponPreview.HasModel ? L("ЛКМ — вращать · Колесо — масштаб", "DRAG — rotate · SCROLL — zoom") : L("БОЕВАЯ СПОСОБНОСТЬ", "COMBAT ABILITY");
        bool owned = Data.Owns(inspected.id), equipped = Data.IsEquipped(inspected.id);
        itemPrice.text = !Loaded ? L("ЗАГРУЗКА…","LOADING…") : owned ? L("В АРСЕНАЛЕ","OWNED") : inspected.price.ToString("N0") + " $";
        itemAction.text = !Loaded ? L("ЗАГРУЗКА…","LOADING…") : !owned ? Data.money >= inspected.price ?
            L("КУПИТЬ И ВЫБРАТЬ","BUY & EQUIP") : L("НЕ ХВАТАЕТ ДЕНЕГ","NOT ENOUGH FUNDS") : equipped ?
            inspected.category == ShopCategory.Skill ? L("УБРАТЬ","UNEQUIP") : L("ВЫБРАНО","EQUIPPED") : L("ВЫБРАТЬ","EQUIP");
        itemButton.interactable = !preview && Loaded && (owned || Data.money >= inspected.price) && (!equipped || inspected.category == ShopCategory.Skill);
        itemAction.color = itemButton.interactable ? new Color(.06f,.075f,.08f) : Muted;
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
        if (Data.IsEquipped(item.id)) ItemEquipped?.Invoke(item.id);
        Click(); Refresh();
    }
    private void Refresh()
    {
        balance.text = Loaded ? Data.money.ToString("N0") + "  $" : GameLocalization.T("ЗАГРУЗКА…");
        foreach (var row in rows)
        {
            bool owned = Data.Owns(row.item.id), selected = Data.IsEquipped(row.item.id);
            row.marker.enabled = selected;
            row.surface.color = row.item == inspected ? new Color(.20f,.21f,.21f,.95f) : new Color(.075f,.078f,.08f,.85f);
            row.badge.text = !Loaded ? L("ЗАГРУЗКА…","LOADING…") : selected ? L("В СНАРЯЖЕНИИ","EQUIPPED") : owned ? L("КУПЛЕНО","OWNED") : row.item.price.ToString("N0") + " $";
            row.badge.color = selected ? Accent : Muted;
            row.button.interactable = true; // Browsing never purchases or changes the loadout.

        }
        RefreshInspector();
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
        size = Mathf.Max(16, size);
        text.font = GameUIStyle.Font; GameLocalization.Bind(text, value); text.fontSize = size; text.color = color; text.alignment = TextAnchor.MiddleLeft;
        text.resizeTextForBestFit = true; text.resizeTextMinSize = 16; text.resizeTextMaxSize = size;
        text.raycastTarget = false; return text;
    }
    private static Button Button(Transform parent, string name, string label, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(name, parent, Vector2.zero, Vector2.one); var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(.10f,.24f,.31f);
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image; button.navigation = new Navigation { mode = Navigation.Mode.None };
        image.color = new Color(.09f,.095f,.10f,.65f);
        button.onClick.AddListener(action); var text = Label(rect, "Label", label, 16, Color.white); text.alignment = TextAnchor.MiddleCenter;
        text.rectTransform.offsetMin = new Vector2(8,4); text.rectTransform.offsetMax = new Vector2(-8,-4); return button;
    }
}
