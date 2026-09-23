using Photon.Pun;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Shared deployment and death screen over a live spectator view.</summary>
[DefaultExecutionOrder(300)]
public sealed class DeploymentScreen : MonoBehaviour
{
    private LobbyManager lobby;
    private DeathShopUI shop;
    private Button deploy, reconnect;
    private Text status;
    private RectTransform preparation;
    private CanvasGroup preparationGroup;
    private float reveal;
    private Camera spectatorCamera;
    private PlayerHealth target;
    private Camera targetCamera;
    private float nextTargetSearch;
    private int openedFrame;
    private readonly System.Collections.Generic.List<Camera> suspendedCameras = new();
    private readonly System.Collections.Generic.List<AudioListener> suspendedListeners = new();

    public static DeploymentScreen Create(LobbyManager lobby)
    {
        var root = new GameObject("Deployment Screen", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 200;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720); scaler.matchWidthOrHeight = .5f;
        var screen = root.AddComponent<DeploymentScreen>(); screen.lobby = lobby;
        screen.Build(); return screen;
    }

    private void Build()
    {
        var events = FindFirstObjectByType<EventSystem>();
        if (events == null) events = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
        if (events.GetComponent<InputSystemUIInputModule>() == null) events.gameObject.AddComponent<InputSystemUIInputModule>();
        events.enabled = true;
        var backdrop = Rect("Background", transform, Vector2.zero, Vector2.one);
        backdrop.gameObject.AddComponent<Image>().color = new Color(.012f, .018f, .022f, .42f);

        preparation = Rect("Preparation", transform, Vector2.one * .5f, Vector2.one * .5f);
        preparation.sizeDelta = new Vector2(440, 330);
        preparation.gameObject.AddComponent<Image>().color = new Color(.032f, .043f, .05f, .94f);
        var shadow = preparation.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0, 0, 0, .35f); shadow.effectDistance = new Vector2(0, -8);
        var border = preparation.gameObject.AddComponent<Outline>();
        border.effectColor = new Color(.72f, .8f, .83f, .12f); border.effectDistance = new Vector2(1, -1);
        preparationGroup = preparation.gameObject.AddComponent<CanvasGroup>();
        preparationGroup.alpha = 0;

        var accent = Rect("Accent", preparation, new Vector2(.43f, 1), new Vector2(.57f, 1));
        accent.sizeDelta = new Vector2(0, 2);
        var accentImage = accent.gameObject.AddComponent<Image>();
        accentImage.color = new Color(.88f, .76f, .49f); accentImage.raycastTarget = false;
        var title = Label(preparation, "COUNTERMINE", 34, .69f, .88f);
        title.fontStyle = FontStyle.Bold;
        title.color = new Color(.94f, .95f, .91f);

        shop = DeathShopUI.Create(transform, null, lobby.Deploy, () => gameObject.activeInHierarchy);
        deploy = MakeButton(preparation, "\u0412 \u0411\u041e\u0419 [SPACE]", new Vector2(.10f,.40f), new Vector2(.90f,.58f), lobby.Deploy, true);
        reconnect = MakeButton(preparation, "\u041f\u041e\u0414\u041a\u041b\u042e\u0427\u0418\u0422\u042c\u0421\u042f", new Vector2(.10f,.40f), new Vector2(.90f,.58f), lobby.Connect, true);
        MakeButton(preparation, "\u041c\u0410\u0413\u0410\u0417\u0418\u041d", new Vector2(.10f,.19f), new Vector2(.90f,.35f), shop.Open, false);
        status = Label(preparation, "", 12, .04f, .14f);
        status.color = new Color(.65f, .70f, .72f);
    }

    private void OnEnable()
    {
        openedFrame = Time.frameCount;
        reveal = 0;
        if (preparationGroup != null) preparationGroup.alpha = 0;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        if (spectatorCamera == null)
        {
            var previousCamera = Camera.main;
            var cameraObject = new GameObject("Deployment Spectator Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.transform.SetParent(transform, false);
            spectatorCamera = cameraObject.GetComponent<Camera>();
            cameraObject.tag = "MainCamera";
            spectatorCamera.nearClipPlane = .08f;
            spectatorCamera.fieldOfView = 75f;
            spectatorCamera.transform.SetPositionAndRotation(
                previousCamera != null ? previousCamera.transform.position : new Vector3(0, 15, -20),
                previousCamera != null ? previousCamera.transform.rotation : Quaternion.Euler(25, 0, 0));
        }
        foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (camera != spectatorCamera && camera.enabled && camera.targetTexture == null)
            { suspendedCameras.Add(camera); camera.enabled = false; }
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            if (listener.gameObject != spectatorCamera.gameObject && listener.enabled)
            { suspendedListeners.Add(listener); listener.enabled = false; }
        nextTargetSearch = 0;
    }
    private void OnDisable()
    {
        if (shop != null) shop.Close();
        foreach (var camera in suspendedCameras) if (camera != null) camera.enabled = true;
        foreach (var listener in suspendedListeners) if (listener != null) listener.enabled = true;
        suspendedCameras.Clear(); suspendedListeners.Clear();
        target = null; targetCamera = null;
    }

    private void LateUpdate()
    {
        if (target == null || !target.isActiveAndEnabled || target.IsDead)
        {
            target = null; targetCamera = null;
            if (Time.unscaledTime < nextTargetSearch) return;
            nextTargetSearch = Time.unscaledTime + .5f;
            int team = PhotonNetwork.LocalPlayer?.CustomProperties["team"] is int t ? t : 0;
            foreach (var candidate in PlayerHealth.ActivePlayers)
            {
                if (candidate == null || candidate.IsDead || !candidate.isActiveAndEnabled ||
                    (!BotController.IsBot(candidate) && candidate.photonView.IsMine)) continue;
                if (target == null || BotController.TeamOf(candidate) == team) target = candidate;
                if (BotController.TeamOf(candidate) == team) break;
            }
            if (target == null) return;
            targetCamera = target.GetComponentInChildren<Camera>(true);
        }
        // Follow the participant's view from over the shoulder; their camera stays disabled.
        Vector3 eye = targetCamera != null ? targetCamera.transform.position : target.transform.position + Vector3.up * 1.6f;
        Quaternion rotation = targetCamera != null ? targetCamera.transform.rotation : target.transform.rotation;
        Vector3 offset = rotation * new Vector3(.6f, .35f, -2.4f);
        float distance = offset.magnitude;
        foreach (var hit in Physics.RaycastAll(eye, offset.normalized, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(target.transform)) continue;
            distance = Mathf.Min(distance, Mathf.Max(.1f, hit.distance - .15f));
        }
        spectatorCamera.transform.SetPositionAndRotation(eye + offset.normalized * distance, rotation);
    }
    private void Update()
    {
        if (lobby == null) return;
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        bool disconnected = PhotonNetwork.NetworkClientState == Photon.Realtime.ClientState.Disconnected ||
            PhotonNetwork.NetworkClientState == Photon.Realtime.ClientState.PeerCreated;
        reconnect.gameObject.SetActive(disconnected);
        deploy.gameObject.SetActive(!disconnected);
        deploy.interactable = lobby.CanDeploy;
        // Show a short status only when the primary action is unavailable.
        status.text = disconnected ? GameLocalization.T("Connecting to Photon...") :
            !YandexPlayerData.IsLoaded ? GameLocalization.T("\u0417\u0410\u0413\u0420\u0423\u0417\u041a\u0410\u2026") :
            !PhotonNetwork.InRoom ? GameLocalization.T("Connected. Joining room...") : "";
        if (disconnected) status.text = "";
        reveal = Mathf.Min(1f, reveal + Time.unscaledDeltaTime / .22f);
        preparationGroup.alpha = Mathf.SmoothStep(0, 1, reveal);
        var canvasRect = (RectTransform)transform;
        float scale = Mathf.Min(1f, Mathf.Min((canvasRect.rect.width - 40) / 440f, (canvasRect.rect.height - 40) / 330f));
        preparation.localScale = Vector3.one * Mathf.Max(.1f, scale);
        if (Time.frameCount > openedFrame && !shop.IsOpen && lobby.CanDeploy && Keyboard.current?.spaceKey.wasPressedThisFrame == true) lobby.Deploy();
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
    }
    private static Text Label(Transform parent, string source, int size, float min, float max)
    {
        var rect = Rect("Label", parent, new Vector2(.04f,min), new Vector2(.96f,max));
        var label = rect.gameObject.AddComponent<Text>(); label.font = GameUIStyle.Font;
        label.fontSize = size; label.color = Color.white; label.alignment = TextAnchor.MiddleCenter;
        label.resizeTextForBestFit = true; label.resizeTextMinSize = 12; label.resizeTextMaxSize = size;
        label.raycastTarget = false; GameLocalization.Bind(label, source); return label;
    }
    private static Button MakeButton(Transform parent, string source, Vector2 min, Vector2 max, UnityEngine.Events.UnityAction action, bool primary)
    {
        var rect = Rect("Button", parent, min, max);
        var image = rect.gameObject.AddComponent<Image>(); image.color = Color.white;
        if (!primary)
        {
            var outline = rect.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(.65f, .73f, .77f, .20f);
            outline.effectDistance = new Vector2(1, -1);
        }
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None }; button.onClick.AddListener(action);
        var colors = button.colors;
        colors.normalColor = primary ? new Color(.88f, .79f, .59f) : new Color(.10f, .13f, .15f);
        colors.highlightedColor = primary ? new Color(1f, .91f, .72f) : new Color(.17f, .22f, .25f);
        colors.pressedColor = primary ? new Color(.72f, .62f, .43f) : new Color(.07f, .09f, .11f);
        colors.selectedColor = colors.highlightedColor;
        colors.disabledColor = new Color(.30f, .32f, .32f);
        colors.fadeDuration = .12f; button.colors = colors;
        var label = Label(rect, source, primary ? 21 : 16, 0, 1);
        label.fontStyle = primary ? FontStyle.Bold : FontStyle.Normal;
        label.color = primary ? new Color(.06f, .075f, .08f) : new Color(.78f, .83f, .85f);
        return button;
    }
}
