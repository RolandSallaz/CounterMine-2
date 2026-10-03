using System;
using System.Collections.Generic;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Room list in the opening menu, driven by Photon lobby updates.</summary>
public sealed class RoomBrowserScreen : MonoBehaviour
{
    private LobbyManager lobby;
    private InputField search, roomName;
    private Text status, count;
    private RectTransform content;
    private int lastRevision = -1;
    private string lastSearch;

    public static RoomBrowserScreen Create(Transform parent, LobbyManager lobby)
    {
        var root = Rect("Room Browser", parent, new Vector2(.065f, .12f), new Vector2(.70f, .87f));
        var browser = root.gameObject.AddComponent<RoomBrowserScreen>();
        browser.lobby = lobby;
        var background = root.gameObject.AddComponent<Image>();
        GameUIStyle.Surface(background, new Color(.025f, .037f, .046f, .96f));

        var heading = Label(root, "КОМНАТЫ", 32, new Vector2(.035f,.88f), new Vector2(.65f,.98f));
        heading.fontStyle = FontStyle.Bold; heading.color = GameUIStyle.Accent;
        Button(root, "НАЗАД", new Vector2(.79f,.89f), new Vector2(.97f,.97f), lobby.LeaveRoomBrowser, false);

        browser.search = Input(root, "Поиск по названию", new Vector2(.035f,.79f), new Vector2(.64f,.865f));
        Button(root, "ОБНОВИТЬ", new Vector2(.67f,.79f), new Vector2(.97f,.865f), lobby.RefreshRooms, false);
        browser.count = Label(root, "", 15, new Vector2(.04f,.735f), new Vector2(.75f,.79f));
        browser.count.color = GameUIStyle.Muted;

        var viewport = Rect("Rooms Viewport", root, new Vector2(.035f,.30f), new Vector2(.965f,.73f));
        viewport.gameObject.AddComponent<Image>().color = new Color(.015f,.025f,.032f,.95f);
        viewport.gameObject.AddComponent<Mask>().showMaskGraphic = true;
        var scroll = viewport.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        browser.content = Rect("Room Rows", viewport, new Vector2(0,1), new Vector2(1,1));
        browser.content.pivot = new Vector2(.5f,1);
        var layout = browser.content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 3; layout.padding = new RectOffset(5,5,5,5);
        layout.childControlWidth = true; layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        browser.content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = browser.content; scroll.viewport = viewport;

        browser.roomName = Input(root, "Название новой комнаты", new Vector2(.035f,.205f), new Vector2(.64f,.28f));
        Button(root, "СОЗДАТЬ", new Vector2(.67f,.205f), new Vector2(.97f,.28f), () => lobby.CreateNamedRoom(browser.roomName.text), false);
        Button(root, "БЫСТРЫЙ ВХОД", new Vector2(.035f,.095f), new Vector2(.48f,.185f), lobby.QuickJoin, true);
        browser.status = Label(root, "", 16, new Vector2(.51f,.09f), new Vector2(.965f,.19f));
        browser.status.color = GameUIStyle.Muted;
        browser.status.alignment = TextAnchor.MiddleRight;
        root.gameObject.SetActive(false);
        return browser;
    }

    private void Update()
    {
        status.text = lobby.ConnectionStatus;
        string query = search.text.Trim();
        if (lastRevision == lobby.RoomListRevision && lastSearch == query) return;
        lastRevision = lobby.RoomListRevision;
        lastSearch = query;
        Rebuild(query);
    }

    private void Rebuild(string query)
    {
        foreach (Transform child in content) Destroy(child.gameObject);
        var rooms = new List<RoomInfo>(lobby.Rooms);
        rooms.Sort((a,b) =>
        {
            int available = (b.IsOpen && (b.MaxPlayers == 0 || b.PlayerCount < b.MaxPlayers)).CompareTo(a.IsOpen && (a.MaxPlayers == 0 || a.PlayerCount < a.MaxPlayers));
            if (available != 0) return available;
            int population = b.PlayerCount.CompareTo(a.PlayerCount);
            return population != 0 ? population : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        int shown = 0;
        foreach (var room in rooms)
        {
            if (room.Name.IndexOf(query, StringComparison.OrdinalIgnoreCase) < 0) continue;
            shown++;
            bool joinable = room.IsOpen && (room.MaxPlayers == 0 || room.PlayerCount < room.MaxPlayers);
            var row = new GameObject("Room: " + room.Name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            row.transform.SetParent(content, false);
            row.GetComponent<LayoutElement>().preferredHeight = 54;
            row.GetComponent<Image>().color = joinable ? new Color(.10f,.15f,.18f,.97f) : new Color(.07f,.09f,.10f,.9f);
            var button = row.GetComponent<Button>(); button.targetGraphic = row.GetComponent<Image>();
            button.interactable = joinable;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            string name = room.Name;
            button.onClick.AddListener(() => lobby.JoinSelectedRoom(name));
            Label(row.transform, room.Name, 18, new Vector2(.025f,.08f), new Vector2(.68f,.92f));
            var occupancy = Label(row.transform, room.PlayerCount + "/" + room.MaxPlayers, 17, new Vector2(.70f,.08f), new Vector2(.81f,.92f));
            occupancy.alignment = TextAnchor.MiddleRight;
            var state = Label(row.transform, joinable ? "ВОЙТИ" : room.IsOpen ? "ЗАНЯТО" : "ЗАКРЫТА", 15, new Vector2(.83f,.08f), new Vector2(.975f,.92f));
            state.alignment = TextAnchor.MiddleRight;
            state.color = joinable ? GameUIStyle.Accent : GameUIStyle.Muted;
        }
        count.text = lobby.RoomsReady ? GameLocalization.Format("Показано комнат: {0} / {1}", shown, rooms.Count)
            : GameLocalization.T("Подключение к списку комнат...");
        if (shown == 0)
        {
            var empty = new GameObject("No Rooms", typeof(RectTransform), typeof(LayoutElement));
            empty.transform.SetParent(content, false);
            empty.GetComponent<LayoutElement>().preferredHeight = 64;
            Label(empty.transform, lobby.RoomsReady ? "Комнаты не найдены. Создайте свою или выберите быстрый вход." : "Загрузка списка комнат...", 17,
                new Vector2(.025f,0), new Vector2(.975f,1)).color = GameUIStyle.Muted;
        }
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
    }

    private static Text Label(Transform parent, string value, int size, Vector2 min, Vector2 max)
    {
        var text = Rect(value, parent, min, max).gameObject.AddComponent<Text>();
        text.font = GameUIStyle.Font; text.fontSize = size; text.color = Color.white;
        text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
        text.resizeTextForBestFit = true; text.resizeTextMinSize = 12; text.resizeTextMaxSize = size;
        GameLocalization.Bind(text, value);
        return text;
    }

    private static Button Button(Transform parent, string title, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action, bool primary)
    {
        var rect = Rect(title, parent, min, max);
        var image = rect.gameObject.AddComponent<Image>();
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        GameUIStyle.StyleButton(button, primary);
        button.onClick.AddListener(() => GameAudio.Effect("UI/click", Vector3.zero, .5f, 1, true));
        button.onClick.AddListener(action);
        var label = Label(rect, title, 16, new Vector2(.04f,0), new Vector2(.96f,1));
        label.alignment = TextAnchor.MiddleCenter;
        label.fontStyle = FontStyle.Bold;
        label.color = primary ? new Color(.04f,.055f,.065f) : Color.white;
        return button;
    }

    private static InputField Input(Transform parent, string hint, Vector2 min, Vector2 max)
    {
        var rect = Rect(hint, parent, min, max);
        rect.gameObject.AddComponent<Image>().color = new Color(.08f,.12f,.14f,.98f);
        var field = rect.gameObject.AddComponent<InputField>();
        field.textComponent = Label(rect, "", 18, new Vector2(.035f,0), new Vector2(.965f,1));
        var placeholder = Label(rect, hint, 17, new Vector2(.035f,0), new Vector2(.965f,1));
        placeholder.color = GameUIStyle.Muted;
        field.placeholder = placeholder;
        field.characterLimit = 32;
        field.lineType = InputField.LineType.SingleLine;
        return field;
    }
}
