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
        GameLocalization.PrepareDynamic(text);
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
        GameLocalization.Bind(close.GetComponentInChildren<Text>(), () => GameLocalization.T("ЗАКРЫТЬ / H"));
        var attribution = ActionButton(panel.transform, "Credits", new Vector2(.05f,.02f), new Vector2(.32f,.1f), () => credits = !credits);
        GameLocalization.Bind(attribution.GetComponentInChildren<Text>(), () => GameLocalization.T("АВТОРЫ / A"));
        cloud = ActionButton(panel.transform, "Cloud", new Vector2(.05f,.12f), new Vector2(.48f,.22f), () => YandexCloudSave.ResolveConflict(false));
        local = ActionButton(panel.transform, "Local", new Vector2(.52f,.12f), new Vector2(.95f,.22f), () => YandexCloudSave.ResolveConflict(true));
        GameLocalization.Bind(cloud.GetComponentInChildren<Text>(), () => GameLocalization.T("ПРОГРЕСС ИЗ ОБЛАКА"));
        GameLocalization.Bind(local.GetComponentInChildren<Text>(), () => GameLocalization.T("ПРОГРЕСС УСТРОЙСТВА"));
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
        help.text = GameLocalization.T("УПРАВЛЕНИЕ / H");
        help.transform.parent.gameObject.SetActive(Cursor.lockState != CursorLockMode.Locked);
        fullscreen.gameObject.SetActive(Cursor.lockState != CursorLockMode.Locked && !show && !YandexAds.Busy);
        bool isFullscreen = FullscreenActive;
        fullscreenIcon.SetRestore(isFullscreen);
        fullscreenLabel.text = isFullscreen ? GameLocalization.T("ОКОННЫЙ РЕЖИМ") : GameLocalization.T("ПОЛНЫЙ ЭКРАН");
        cloud.gameObject.SetActive(conflict); local.gameObject.SetActive(conflict); mode.transform.parent.gameObject.SetActive(!conflict);
        mode.text = GameLocalization.T("B — РЕЖИМ: ") + (OneHand ? GameLocalization.T("ОДНА РУКА / КЛАВИАТУРА") : GameLocalization.T("КЛАВИАТУРА И МЫШЬ"));
        body.text = conflict ? GameLocalization.T("Обнаружены разные сохранения.\nВыберите прогресс, который нужно продолжить.\nДругой вариант останется в локальной резервной копии.") :
            GameLocalization.T("УПРАВЛЕНИЕ\n\nWASD — движение · Shift — бег\nSpace — прыжок · C — присесть / подкат\n1 / 2 — оружие · G — граната\n3 / 4 / 5 — способности\nEsc — освободить курсор · H — справка\n\n") +
            (OneHand ? GameLocalization.T("Q / E — взгляд влево / вправо\nR / F — взгляд вверх / вниз\nX — огонь / продолжить · Z — прицел · T — перезарядка") : GameLocalization.T("Мышь — обзор · ЛКМ — огонь / продолжить\nПКМ — прицел · R — перезарядка"));
        if (credits && !conflict) body.text = GameLocalization.T("МУЗЫКА\n") +
            "LOOP BOX #2 — Of Far Different Nature\nhttps://fardifferent.carrd.co/\nhttps://opengameart.org/node/116122\nCC BY 4.0 — https://creativecommons.org/licenses/by/4.0/\n\n" +
            GameLocalization.T("Изменения: громкость воспроизведения.\nForce Field: конвертация в WAV, громкость,\nсглаживание границы петли (8 мс).\n\nA — вернуться к управлению");
        switch (YandexCloudSave.State)
        {
            case "saved": status.text = ""; break;
            case "cloud-only": status.text = GameLocalization.T("Прогресс сохранён в облаке"); break;
            case "storage-unavailable": status.text = GameLocalization.T("Локальная копия недоступна. Отправляем в облако…"); break;
            case "pending": status.text = GameLocalization.T("Прогресс ожидает отправки в облако"); break;
            case "loading": status.text = GameLocalization.T("Загрузка прогресса…"); break;
            case "conflict": status.text = GameLocalization.T("Выберите сохранение"); break;
            case "invalid": status.text = GameLocalization.T("Сохранение не прочитано. Перезапустите игру."); break;
            default: status.text = GameLocalization.T("Облако недоступно. Повторяем подключение…"); break;
        }
    }
    private void OnDestroy() { ModalOpen = false; }
}
