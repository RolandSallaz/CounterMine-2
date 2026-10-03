using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>PC controls, keyboard-only alternative and save recovery UI.</summary>
[DefaultExecutionOrder(-900)]
public sealed class DesktopControls : MonoBehaviour
{
    public static bool ModalOpen { get; private set; }
    public static bool OneHand { get; private set; }
    public static bool FireHeld => OneHand ? Keyboard.current?.xKey.isPressed == true : Mouse.current?.leftButton.isPressed == true;
    public static bool FirePressed => OneHand ? Keyboard.current?.xKey.wasPressedThisFrame == true : Mouse.current?.leftButton.wasPressedThisFrame == true;
    public static bool AimHeld => OneHand ? Keyboard.current?.zKey.isPressed == true : Mouse.current?.rightButton.isPressed == true;
    public static bool ReloadPressed => OneHand ? Keyboard.current?.tKey.wasPressedThisFrame == true : Keyboard.current?.rKey.wasPressedThisFrame == true;
    public static Vector2 Look(float sensitivity)
    {
        if (!OneHand) return (Mouse.current?.delta.ReadValue() ?? Vector2.zero) * sensitivity;
        var k = Keyboard.current;
        if (k == null) return Vector2.zero;
        return new Vector2((k.eKey.isPressed ? 1 : 0) - (k.qKey.isPressed ? 1 : 0),
            (k.rKey.isPressed ? 1 : 0) - (k.fKey.isPressed ? 1 : 0)) * (75f * Time.deltaTime);
    }
    private GameObject panel, backdrop;
    private Text body, status, mode, help, fullscreenLabel;
    private Button fullscreen;
    private FullscreenIcon fullscreenIcon;
    private Button cloud, local;
    private bool opened, relock, credits;
    private static string L(string ru, string en) => GameLocalization.Language == "ru" ? ru : en;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        ModalOpen = false; OneHand = PlayerPrefs.GetInt("KeyboardOnlyControls", 0) != 0;
        var go = new GameObject("DesktopControls"); DontDestroyOnLoad(go); go.AddComponent<DesktopControls>();
    }
    private RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false); rt.anchorMin = min; rt.anchorMax = max; rt.offsetMin = rt.offsetMax = Vector2.zero; return rt;
    }
    private Text Label(Transform parent, string name, Vector2 min, Vector2 max, int size)
    {
        var text = Rect(name, parent, min, max).gameObject.AddComponent<Text>();
        text.font = GameUIStyle.Font; text.fontSize = size; text.color = GameUIStyle.Text;
        text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false; return text;
    }
    private Button ActionButton(Transform parent, string name, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action)
    {
        var rt = Rect(name, parent, min, max); var image = rt.gameObject.AddComponent<Image>();
        var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = image; GameUIStyle.StyleButton(button);
        Label(rt, "Label", Vector2.zero, Vector2.one, 19); button.onClick.AddListener(action); return button;
    }
    private void Awake()
    {
        var canvas = gameObject.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 600;
        var scaler = gameObject.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f; gameObject.AddComponent<GraphicRaycaster>();
        help = ActionButton(transform, "Help", new Vector2(.02f,.94f), new Vector2(.23f,.99f), Toggle).GetComponentInChildren<Text>();
        status = Label(transform, "SaveStatus", new Vector2(.25f,.93f), new Vector2(.74f,.99f), 18);
        fullscreen = ActionButton(transform, "Fullscreen", new Vector2(.76f,.01f), new Vector2(.98f,.06f), ToggleFullscreen);
        fullscreenLabel = fullscreen.GetComponentInChildren<Text>();
        var fullscreenRect = (RectTransform)fullscreen.transform;
        fullscreenRect.anchorMin = fullscreenRect.anchorMax = new Vector2(1, 0);
        fullscreenRect.pivot = new Vector2(1, 0);
        fullscreenRect.anchoredPosition = new Vector2(-20, 5);
        fullscreenRect.sizeDelta = new Vector2(226, 32);
        var fullscreenColors = fullscreen.colors;
        fullscreenColors.normalColor = new Color(.045f, .064f, .08f, .97f);
        fullscreenColors.highlightedColor = new Color(.19f, .22f, .23f);
        fullscreenColors.selectedColor = fullscreenColors.highlightedColor;
        fullscreenColors.pressedColor = new Color(.28f, .25f, .18f);
        fullscreenColors.fadeDuration = .12f;
        fullscreen.colors = fullscreenColors;
        var border = fullscreen.gameObject.AddComponent<Outline>();
        border.effectColor = new Color(GameUIStyle.Accent.r, GameUIStyle.Accent.g, GameUIStyle.Accent.b, .38f);
        border.effectDistance = new Vector2(1, -1);
        fullscreenLabel.fontSize = 14;
        fullscreenLabel.color = GameUIStyle.Accent;
        fullscreenLabel.alignment = TextAnchor.MiddleLeft;
        fullscreenLabel.rectTransform.offsetMin = new Vector2(45, 0);
        fullscreenLabel.rectTransform.offsetMax = new Vector2(-8, 0);
        var iconRect = Rect("Expand icon", fullscreen.transform, new Vector2(0,.5f), new Vector2(0,.5f));
        iconRect.sizeDelta = new Vector2(18,18); iconRect.anchoredPosition = new Vector2(23,0);
        fullscreenIcon = iconRect.gameObject.AddComponent<FullscreenIcon>();
        fullscreenIcon.color = GameUIStyle.Accent; fullscreenIcon.raycastTarget = false;
        backdrop = Rect("ModalBackdrop", transform, Vector2.zero, Vector2.one).gameObject;
        backdrop.AddComponent<Image>().color = new Color(0, 0, 0, .65f);
        backdrop.SetActive(false);
        panel = Rect("Controls", transform, new Vector2(.12f,.12f), new Vector2(.88f,.9f)).gameObject;
        GameUIStyle.Surface(panel.AddComponent<Image>(), GameUIStyle.Panel);
        body = Label(panel.transform, "Instructions", new Vector2(.04f,.23f), new Vector2(.96f,.94f), 23);
        mode = ActionButton(panel.transform, "Mode", new Vector2(.05f,.12f), new Vector2(.95f,.21f), SwitchMode).GetComponentInChildren<Text>();
        var close = ActionButton(panel.transform, "Close", new Vector2(.35f,.02f), new Vector2(.65f,.1f), Toggle);
        GameLocalization.Bind(close.GetComponentInChildren<Text>(), () => L("ЗАКРЫТЬ / H", "CLOSE / H"));
        var attribution = ActionButton(panel.transform, "Credits", new Vector2(.05f,.02f), new Vector2(.32f,.1f), () => credits = !credits);
        GameLocalization.Bind(attribution.GetComponentInChildren<Text>(), () => L("АВТОРЫ / A", "CREDITS / A"));
        cloud = ActionButton(panel.transform, "Cloud", new Vector2(.05f,.12f), new Vector2(.48f,.22f), () => YandexCloudSave.ResolveConflict(false));
        local = ActionButton(panel.transform, "Local", new Vector2(.52f,.12f), new Vector2(.95f,.22f), () => YandexCloudSave.ResolveConflict(true));
        GameLocalization.Bind(cloud.GetComponentInChildren<Text>(), () => L("ПРОГРЕСС ИЗ ОБЛАКА", "CLOUD PROGRESS"));
        GameLocalization.Bind(local.GetComponentInChildren<Text>(), () => L("ПРОГРЕСС УСТРОЙСТВА", "DEVICE PROGRESS"));
        panel.SetActive(false);
    }
    private void SwitchMode() { OneHand = !OneHand; PlayerPrefs.SetInt("KeyboardOnlyControls", OneHand ? 1 : 0); PlayerPrefs.Save(); }
    private void Toggle() { opened = !opened; }
    private static bool FullscreenActive
    {
        get
        {
#if UNITY_EDITOR
            var type = System.Type.GetType("UnityEditor.GameView,UnityEditor");
            var windows = type != null ? Resources.FindObjectsOfTypeAll(type) : null;
            return windows != null && windows.Length > 0 && ((UnityEditor.EditorWindow)windows[0]).maximized;
#else
            return Screen.fullScreen;
#endif
        }
    }
    private static void ToggleFullscreen()
    {
#if UNITY_EDITOR
        var type = System.Type.GetType("UnityEditor.GameView,UnityEditor");
        if (type == null) return;
        var window = UnityEditor.EditorWindow.GetWindow(type);
        window.maximized = !window.maximized;
#else
        Screen.fullScreen = !Screen.fullScreen;
#endif
    }
    private void Update()
    {
        if (Keyboard.current?.hKey.wasPressedThisFrame == true) Toggle();
        bool conflict = YandexCloudSave.State == "conflict";
        bool show = opened || conflict;
        if (show && !ModalOpen) { relock = Cursor.lockState == CursorLockMode.Locked; Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
        bool closing = !show && ModalOpen; ModalOpen = show;
        if (closing && relock && !PlatformLifecycle.InputBlocked) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        panel.SetActive(show);
        backdrop.SetActive(show);
        if (opened && !conflict && Keyboard.current?.aKey.wasPressedThisFrame == true) credits = !credits;
        if (opened && !conflict && Keyboard.current?.bKey.wasPressedThisFrame == true) SwitchMode();
        help.text = L("УПРАВЛЕНИЕ / H", "CONTROLS / H");
        help.transform.parent.gameObject.SetActive(Cursor.lockState != CursorLockMode.Locked);
        fullscreen.gameObject.SetActive(Cursor.lockState != CursorLockMode.Locked && !show && !YandexAds.Busy);
        bool isFullscreen = FullscreenActive;
        fullscreenIcon.SetRestore(isFullscreen);
        fullscreenLabel.text = isFullscreen ? L("ОКОННЫЙ РЕЖИМ", "WINDOWED MODE") : L("ПОЛНЫЙ ЭКРАН", "FULL SCREEN");
        cloud.gameObject.SetActive(conflict); local.gameObject.SetActive(conflict); mode.transform.parent.gameObject.SetActive(!conflict);
        mode.text = L("B — РЕЖИМ: ", "B — MODE: ") + (OneHand ? L("ОДНА РУКА / КЛАВИАТУРА", "ONE HAND / KEYBOARD") : L("КЛАВИАТУРА И МЫШЬ", "KEYBOARD AND MOUSE"));
        body.text = conflict ? L("Обнаружены разные сохранения.\nВыберите прогресс, который нужно продолжить.\nДругой вариант останется в локальной резервной копии.", "Different saves were found.\nChoose the progress you want to continue.\nThe other version is kept in a local backup.") :
            L("УПРАВЛЕНИЕ\n\nWASD — движение · Shift — бег\nSpace — прыжок · C — присесть / подкат\n1 / 2 — оружие · G — граната\n3 / 4 / 5 — способности\nEsc — освободить курсор · H — справка\n\n", "CONTROLS\n\nWASD — move · Shift — sprint\nSpace — jump · C — crouch / slide\n1 / 2 — weapons · G — grenade\n3 / 4 / 5 — abilities\nEsc — release cursor · H — help\n\n") +
            (OneHand ? L("Q / E — взгляд влево / вправо\nR / F — взгляд вверх / вниз\nX — огонь / продолжить · Z — прицел · T — перезарядка", "Q / E — look left / right\nR / F — look up / down\nX — fire / resume · Z — aim · T — reload") : L("Мышь — обзор · ЛКМ — огонь / продолжить\nПКМ — прицел · R — перезарядка", "Mouse — look · LMB — fire / resume\nRMB — aim · R — reload"));
        if (credits && !conflict) body.text = L("МУЗЫКА\n", "MUSIC\n") +
            "LOOP BOX #2 — Of Far Different Nature\nhttps://fardifferent.carrd.co/\nhttps://opengameart.org/node/116122\nCC BY 4.0 — https://creativecommons.org/licenses/by/4.0/\n\n" +
            L("Изменения: громкость воспроизведения.\nForce Field: конвертация в WAV, громкость,\nсглаживание границы петли (8 мс).\n\nA — вернуться к управлению", "Changes: playback gain adjustment.\nForce Field: WAV conversion, gain adjustment,\n8 ms loop boundary blend.\n\nA — return to controls");
        switch (YandexCloudSave.State)
        {
            case "saved": status.text = ""; break;
            case "cloud-only": status.text = L("Прогресс сохранён в облаке", "Progress saved in the cloud"); break;
            case "storage-unavailable": status.text = L("Локальная копия недоступна. Отправляем в облако…", "Local backup unavailable. Syncing to cloud…"); break;
            case "pending": status.text = L("Прогресс ожидает отправки в облако", "Progress awaiting cloud sync"); break;
            case "loading": status.text = L("Загрузка прогресса…", "Loading progress…"); break;
            case "conflict": status.text = L("Выберите сохранение", "Choose a save"); break;
            case "invalid": status.text = L("Сохранение не прочитано. Перезапустите игру.", "Save could not be read. Restart the game."); break;
            default: status.text = L("Облако недоступно. Повторяем подключение…", "Cloud unavailable. Retrying…"); break;
        }
    }
    private void OnDestroy() { ModalOpen = false; }
}
