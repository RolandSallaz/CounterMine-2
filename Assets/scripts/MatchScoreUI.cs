using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Hold-Tab match standings and a bounded, animated reward stack.</summary>
public sealed class MatchScoreUI : MonoBehaviour
{
    private sealed class Row
    {
        public RectTransform root;
        public Image background, stripe;
        public Text rank, name, team, kills, deaths, assists, score;
    }
    private sealed class Toast
    {
        public RectTransform root;
        public CanvasGroup group;
        public float born;
    }
    private readonly List<MatchScore.Entry> entries = new List<MatchScore.Entry>();
    private readonly List<Row> rows = new List<Row>();
    private readonly List<Toast> toasts = new List<Toast>();
    private RectTransform panel, toastRoot;
    private Text totals, footer;
    private Font font;
    private float refreshAt;
    private int firstRow;
    private static readonly Color Muted = new Color(.57f, .65f, .72f);
    private static readonly Color Gold = new Color(1f, .78f, .3f);

    private void Awake()
    {
        KillRewards.EnsureSubscribed();
        font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        panel = Rect("Scoreboard", transform, new Vector2(.13f, .16f), new Vector2(.87f, .85f));
        var bg = panel.gameObject.AddComponent<Image>();
        bg.color = new Color(.018f, .027f, .039f, .96f); bg.raycastTarget = false;
        var border = panel.gameObject.AddComponent<Outline>();
        border.effectColor = new Color(.24f, .42f, .55f, .65f); border.effectDistance = new Vector2(1f, -1f);
        Label(panel, "ТАБЛИЦА СЧЁТА", 24, Color.white, .025f, .65f, 12, 30);
        var musicCredit = Label(panel, GameAudio.BackgroundMusicCredit, 11, Muted, .52f, .975f, 10, 32);
        musicCredit.alignment = TextAnchor.UpperRight;
        totals = Label(panel, "", 16, Gold, .025f, .97f, 46, 24);
        Label(panel, "#", 13, Muted, .025f, .07f, 82, 22);
        Label(panel, "ИГРОК", 13, Muted, .08f, .43f, 82, 22);
        Label(panel, "КОМАНДА", 12, Muted, .44f, .6f, 82, 22);
        Label(panel, "У", 13, Muted, .61f, .68f, 82, 22);
        Label(panel, "С", 13, Muted, .69f, .76f, 82, 22);
        Label(panel, "П", 13, Muted, .77f, .84f, 82, 22);
        Label(panel, "ОЧКИ", 13, Gold, .85f, .98f, 82, 22);
        footer = Label(panel, "", 13, Muted, .025f, .98f, 0, 26);
        footer.rectTransform.anchorMin = new Vector2(.025f, 0);
        footer.rectTransform.anchorMax = new Vector2(.98f, 0);
        footer.rectTransform.anchoredPosition = new Vector2(0, 28);
        panel.gameObject.SetActive(false);
        toastRoot = Rect("Reward Notifications", transform, new Vector2(.5f, .14f), new Vector2(.5f, .14f));
        toastRoot.sizeDelta = new Vector2(340, 0);
    }
    private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
    private Text Label(Transform parent, string value, int size, Color tint, float x0, float x1, float top, float height)
    {
        var rect = Rect("Label", parent, new Vector2(x0, 1), new Vector2(x1, 1));
        rect.pivot = new Vector2(.5f, 1);
        rect.anchoredPosition = new Vector2(0, -top); rect.sizeDelta = new Vector2(0, height);
        var text = rect.gameObject.AddComponent<Text>();
        text.font = font; text.fontSize = size; text.color = tint; text.text = value;
        text.supportRichText = false; text.raycastTarget = false;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }
    private Row MakeRow()
    {
        var root = Rect("Standing", panel, new Vector2(.02f, 1), new Vector2(.98f, 1));
        root.pivot = new Vector2(.5f, 1); root.sizeDelta = new Vector2(0, 32);
        var row = new Row { root = root };
        row.background = root.gameObject.AddComponent<Image>(); row.background.raycastTarget = false;
        var stripe = Rect("Team Accent", root, Vector2.zero, new Vector2(0, 1));
        stripe.sizeDelta = new Vector2(3, 0);
        row.stripe = stripe.gameObject.AddComponent<Image>(); row.stripe.raycastTarget = false;
        row.rank = Label(root, "", 15, Muted, .005f, .05f, 0, 32);
        row.name = Label(root, "", 16, Color.white, .062f, .43f, 0, 32);
        row.team = Label(root, "", 12, Muted, .438f, .60f, 0, 32);
        row.kills = Label(root, "", 16, Color.white, .615f, .68f, 0, 32);
        row.deaths = Label(root, "", 16, Muted, .698f, .76f, 0, 32);
        row.assists = Label(root, "", 16, Muted, .781f, .84f, 0, 32);
        row.score = Label(root, "", 18, Gold, .864f, 1, 0, 32);
        return row;
    }
    private void Update()
    {
        bool show = Application.isFocused && Keyboard.current?.tabKey.isPressed == true;
        if (panel.gameObject.activeSelf != show)
        {
            panel.gameObject.SetActive(show);
            if (show) { firstRow = 0; refreshAt = 0; panel.SetAsLastSibling(); }
        }
        if (show)
        {
            float wheel = Mouse.current != null ? Mouse.current.scroll.ReadValue().y : 0;
            int step = wheel < 0 || Keyboard.current.pageDownKey.wasPressedThisFrame ? 1 : wheel > 0 || Keyboard.current.pageUpKey.wasPressedThisFrame ? -1 : 0;
            if (step != 0) { firstRow += step * 5; refreshAt = 0; }
            if (Time.unscaledTime >= refreshAt) { refreshAt = Time.unscaledTime + .2f; Refresh(); }
        }
        while (KillRewards.TryTakeNotice(out var notice)) AddToast(notice);
        for (int i = toasts.Count - 1; i >= 0; i--)
        {
            var toast = toasts[i];
            float age = Time.unscaledTime - toast.born;
            if (age >= 3f) { Destroy(toast.root.gameObject); toasts.RemoveAt(i); continue; }
            float enter = Mathf.Clamp01(age / .18f), exit = Mathf.Clamp01((3f - age) / .5f);
            toast.group.alpha = enter * exit;
            float y = (toasts.Count - 1 - i) * 51f + (1f - enter) * -16f + (1f - exit) * 12f;
            toast.root.anchoredPosition = Vector2.Lerp(toast.root.anchoredPosition, new Vector2(0, y), 1f - Mathf.Exp(-18f * Time.unscaledDeltaTime));
        }
    }
    private void Refresh()
    {
        MatchScore.ReadRoster(entries);
        int visible = Mathf.Max(1, Mathf.FloorToInt((panel.rect.height - 142f) / 34f));
        firstRow = Mathf.Clamp(firstRow, 0, Mathf.Max(0, entries.Count - visible));
        int count = Mathf.Min(visible, entries.Count - firstRow);
        while (rows.Count < count) rows.Add(MakeRow());
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i]; row.root.gameObject.SetActive(i < count);
            if (i >= count) continue;
            var entry = entries[firstRow + i];
            bool local = PhotonNetwork.InRoom && entry.key == "p" + PhotonNetwork.LocalPlayer.ActorNumber;
            Color team = entry.team == 1 ? new Color(.3f, .72f, 1f) : new Color(1f, .51f, .25f);
            row.root.anchoredPosition = new Vector2(0, -110 - i * 34);
            row.background.color = local ? new Color(.13f, .23f, .29f, .95f) : new Color(.075f, .105f, .14f, i % 2 == 0 ? .85f : .4f);
            row.stripe.color = team; row.rank.text = (firstRow + i + 1).ToString();
            row.name.text = (local ? "› " : "") + entry.name;
            row.team.text = entry.team == 1 ? "ALPHA" : entry.team == 2 ? "BRAVO" : "—"; row.team.color = team;
            row.kills.text = entry.kills.ToString(); row.deaths.text = entry.deaths.ToString();
            row.assists.text = entry.assists.ToString(); row.score.text = entry.score.ToString();
        }
        totals.text = "ВАШИ ОЧКИ  " + KillRewards.Score + "     /     ДЕНЬГИ  " + YandexPlayerData.Current.money;
        footer.text = "У — убийства   С — смерти   П — помощь" + (entries.Count > visible ? "     |     Колесо / PgUp / PgDn  " + (firstRow + 1) + "–" + (firstRow + count) + "/" + entries.Count : "     |     TAB — закрыть");
    }
    private void AddToast(KillRewards.Notice notice)
    {
        while (toasts.Count >= 3) { Destroy(toasts[0].root.gameObject); toasts.RemoveAt(0); }
        var root = Rect("Reward", toastRoot, Vector2.zero, new Vector2(1, 0));
        root.pivot = new Vector2(.5f, 0); root.sizeDelta = new Vector2(0, 42);
        var image = root.gameObject.AddComponent<Image>(); image.color = new Color(.02f, .035f, .05f, .93f); image.raycastTarget = false;
        var accent = Rect("Accent", root, Vector2.zero, new Vector2(0, 1)); accent.sizeDelta = new Vector2(3, 0);
        var strip = accent.gameObject.AddComponent<Image>(); strip.color = notice.kill ? Gold : new Color(.35f, .85f, .9f); strip.raycastTarget = false;
        string title = notice.kill ? "УБИЙСТВО" : "ПОМОЩЬ";
        if (notice.count > 1) title += " ×" + notice.count;
        var label = Label(root, title + " +" + notice.amount, 22, strip.color, .045f, .955f, 0, 42);
        label.alignment = TextAnchor.MiddleCenter;
        var group = root.gameObject.AddComponent<CanvasGroup>(); group.blocksRaycasts = false; group.interactable = false; group.alpha = 0;
        toasts.Add(new Toast { root = root, group = group, born = Time.unscaledTime });
    }
    private void OnDisable()
    {
        if (panel != null) panel.gameObject.SetActive(false);
        foreach (var toast in toasts) if (toast.root != null) Destroy(toast.root.gameObject);
        toasts.Clear();
    }
    private void OnDestroy()
    {
        if (panel != null) Destroy(panel.gameObject);
        if (toastRoot != null) Destroy(toastRoot.gameObject);
    }
}
