using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>Map-backed opening menu. Its display character has no gameplay or network components.</summary>
public sealed class StartMenuScreen : MonoBehaviour
{
    private const int PreviewLayer = 30;
    private readonly List<Camera> suspendedCameras = new();
    private readonly List<AudioListener> suspendedListeners = new();
    private GameObject stage;
    private Camera backgroundCamera, characterCamera;
    private RenderTexture backgroundTexture, characterTexture;
    private RawImage backgroundImage, characterImage;
    private Material blurMaterial;
    private Texture2D shadeTexture;
    private Transform character, weapon;
    private AnimationClip characterIdle, weaponIdle;
    private float elapsed;
    private int renderWidth, renderHeight;
    private DeathShopUI shop;
    private RectTransform modePanel;
    private WeaponIdleSynchronizer previewSource;
    private WeaponHandIK previewIK;
    private readonly Dictionary<Transform, Transform> displayCopies = new();
    private readonly HashSet<Transform> renderedWeapons = new();
    private string displayedWeapon, equippedPrimary;
    private Text wallet, weaponCaption;
    private GameObject walletPanel, menuFooter;
    private CanvasGroup menuGroup;
    private RoomBrowserScreen roomBrowser;
    private float reveal;
    private Vector3 portraitFocus, portraitDirection;

    public static StartMenuScreen Create(LobbyManager lobby, GameObject playerPrefab, Vector3 position)
    {
        var root = new GameObject("Start Menu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;
        canvas.sortingOrder = 210;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1280, 720);
        scaler.matchWidthOrHeight = .5f;
        var menu = root.AddComponent<StartMenuScreen>();
        menu.Build(lobby, playerPrefab, position);
        return menu;
    }

    private void Build(LobbyManager lobby, GameObject prefab, Vector3 position)
    {
        var events = FindFirstObjectByType<EventSystem>();
        if (events == null) events = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();
        if (events.GetComponent<InputSystemUIInputModule>() == null) events.gameObject.AddComponent<InputSystemUIInputModule>();
        events.enabled = true;
        foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            if (camera.enabled && camera.targetTexture == null) { suspendedCameras.Add(camera); camera.enabled = false; }
        foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            if (listener.enabled) { suspendedListeners.Add(listener); listener.enabled = false; }

        stage = new GameObject("Menu Presentation");
        if (Physics.Raycast(position + Vector3.up * 3, Vector3.down, out var ground, 20, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            position = ground.point + Vector3.up * .02f;
        var actor = CreateDisplayCharacter(prefab);
        actor.SetParent(stage.transform, false);
        actor.position = position;

        // Pick a clear camera approach near the spawn instead of placing it inside a wall.
        Vector3 direction = Vector3.forward;
        float bestDistance = 0;
        for (int i = 0; i < 12; i++)
        {
            Vector3 candidate = Quaternion.Euler(0, i * 30, 0) * Vector3.forward;
            float distance = Physics.SphereCast(position + Vector3.up * 1.25f, .25f, candidate, out var hit, 4.5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ? hit.distance : 4.5f;
            if (distance > bestDistance) { bestDistance = distance; direction = candidate; }
        }
        actor.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0, -5, 0);
        backgroundCamera = MakeCamera("Map Camera", ~(1 << PreviewLayer), CameraClearFlags.Skybox);
        characterCamera = MakeCamera("Character Camera", 1 << PreviewLayer, CameraClearFlags.SolidColor);
        backgroundCamera.gameObject.AddComponent<AudioListener>();
        Vector3 cameraPosition = position + direction * Mathf.Max(1.5f, bestDistance - .2f) + Vector3.up * 1.25f;
        Quaternion rotation = Quaternion.LookRotation(position + Vector3.up * 1.0f - cameraPosition);
        Vector3 focus = position + Vector3.up * 1.0f - rotation * Vector3.right * .85f;
        rotation = Quaternion.LookRotation(focus - cameraPosition);
        backgroundCamera.transform.SetPositionAndRotation(cameraPosition, rotation);
        characterCamera.transform.SetPositionAndRotation(cameraPosition, rotation);
        // Frame the transparent portrait independently of the map and UI.
        portraitFocus = position + Vector3.up * 1.23f;
        portraitDirection = direction;
        characterCamera.fieldOfView = 30f;

        backgroundImage = Rect("Blurred Map", transform, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
        backgroundImage.raycastTarget = false;
        var shader = Resources.Load<Shader>("UI/MenuBackgroundBlur");
        if (shader != null) { blurMaterial = new Material(shader); backgroundImage.material = blurMaterial; }
        characterImage = Rect("Sharp Character", transform, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
        characterImage.raycastTarget = false;
        var shade = Rect("Menu Shade", transform, Vector2.zero, Vector2.one).gameObject.AddComponent<RawImage>();
        shadeTexture = new Texture2D(64, 1, TextureFormat.RGBA32, false);
        shadeTexture.wrapMode = TextureWrapMode.Clamp;
        for (int x = 0; x < 64; x++)
        {
            float alpha = Mathf.Lerp(.91f, .04f, Mathf.SmoothStep(0, 1, x / 50f));
            shadeTexture.SetPixel(x, 0, new Color(.018f, .027f, .033f, alpha));
        }
        shadeTexture.Apply(); shade.texture = shadeTexture; shade.raycastTarget = false;

        var panel = Rect("Mode Selection", transform, new Vector2(.065f, .19f), new Vector2(.40f, .79f));
        modePanel = panel;
        menuGroup = panel.gameObject.AddComponent<CanvasGroup>();
        var eyebrow = Label(panel, "TACTICAL OPERATIONS", 16, new Vector2(0, .92f), Vector2.one);
        eyebrow.color = GameUIStyle.Accent;
        var title = Label(panel, "BLOCKFIELD", 42, new Vector2(0, .77f), new Vector2(1, .93f));
        title.fontStyle = FontStyle.Bold;
        var line = Rect("Gold Accent", panel, new Vector2(0, .70f), new Vector2(.15f, .70f));
        line.sizeDelta = new Vector2(0, 2);
        line.gameObject.AddComponent<Image>().color = GameUIStyle.Accent;
        ModeButton(panel, "Single player", .43f, .63f, true, () => lobby.StartGame(true), "Тренировка с ботами");
        ModeButton(panel, "Multiplayer", .20f, .40f, false, lobby.BrowseRooms, "Сражения с другими игроками");
        ModeButton(panel, "МАГАЗИН", -.03f, .17f, false, OpenShop, "Оружие и снаряжение");
        var account = Rect("Wallet", transform, new Vector2(.76f,.88f), new Vector2(.95f,.95f));
        walletPanel = account.gameObject;
        GameUIStyle.Surface(account.gameObject.AddComponent<Image>(), GameUIStyle.Panel);
        wallet = Label(account, "", 20, new Vector2(.10f,0), new Vector2(.90f,1));
        wallet.alignment = TextAnchor.MiddleRight; wallet.color = GameUIStyle.Accent;
        var caption = Rect("Weapon Caption", transform, new Vector2(.66f,.055f), new Vector2(.94f,.155f));
        GameUIStyle.Surface(caption.gameObject.AddComponent<Image>(), GameUIStyle.Panel);
        var captionTitle = Label(caption, "ТЕКУЩЕЕ ОРУЖИЕ", 14, new Vector2(.07f,.55f), new Vector2(.93f,.90f));
        captionTitle.color = GameUIStyle.Muted;
        weaponCaption = Label(caption, "", 23, new Vector2(.07f,.08f), new Vector2(.93f,.57f));
        var footer = Label(transform, "Выберите режим и вступайте в бой", 16, new Vector2(.065f,.05f), new Vector2(.55f,.10f));
        menuFooter = footer.gameObject;
        footer.color = GameUIStyle.Muted;
        shop = DeathShopUI.Create(transform, null, null, () => isActiveAndEnabled, mainMenu: true);
        shop.ItemEquipped += ShowEquippedItem;
        roomBrowser = RoomBrowserScreen.Create(transform, lobby);
        ResizeTargets();
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
    }

    private Camera MakeCamera(string name, int mask, CameraClearFlags clear)
    {
        var go = new GameObject(name, typeof(Camera)); go.transform.SetParent(stage.transform, false);
        var camera = go.GetComponent<Camera>();
        camera.cullingMask = mask; camera.clearFlags = clear; camera.backgroundColor = Color.clear;
        camera.fieldOfView = 36; camera.nearClipPlane = .1f; camera.farClipPlane = 500;
        camera.allowHDR = false; camera.allowMSAA = false;
        var additional = camera.GetUniversalAdditionalCameraData();
        additional.renderPostProcessing = false;
        additional.antialiasing = AntialiasingMode.None;
        return camera;
    }

    private Transform CreateDisplayCharacter(GameObject prefab)
    {
        var root = new GameObject("Display Player").transform;
        var source = prefab != null ? prefab.GetComponentInChildren<WeaponIdleSynchronizer>(true) : null;
        if (source == null || source.CharacterAnimator == null)
        {
            Debug.LogError("Start menu requires the player's character and idle animation.");
            return root;
        }
        // Copy only transforms and renderers. Instantiating the Player prefab would run
        // Photon ownership, health, weapon, ragdoll and HUD initialization in the menu.
        previewSource = source;
        var copies = displayCopies;
        CopyTransforms(prefab.transform, root, copies);
        character = copies[source.CharacterAnimator.transform];
        characterIdle = source.PreviewCharacterIdle;
        CopyRenderers(source.CharacterAnimator.transform, copies);
        var aim = prefab.GetComponentInChildren<WeaponAimController>(true);
        if (aim != null)
        {
            var pivot = new GameObject("Third Person Weapon Offset").transform;
            pivot.SetParent(copies[aim.transform].parent, false);
            copies[aim.transform].SetParent(pivot, false);
            var presentation = prefab.GetComponent<PlayerModelPresentation>();
            pivot.localPosition = presentation != null ? presentation.ThirdPersonWeaponOffset : new Vector3(0, -.25f, .1f);
        }
        var sourceIK = source.CharacterAnimator.GetComponent<WeaponHandIK>();
        if (sourceIK != null)
        {
            previewIK = character.gameObject.AddComponent<WeaponHandIK>();
            previewIK.enabled = false;
            sourceIK.CopyConfigurationTo(previewIK, copies);
            previewIK.SetThirdPersonFrame(root);
        }
        equippedPrimary = YandexPlayerData.Current.equippedWeapon;
        ShowEquippedItem(equippedPrimary);
        SamplePose(0);
        // Center the body, not the combined bounds of body and a long rifle.
        var renderers = character.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            copies[prefab.transform].position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }
        return root;
    }

    private void OpenShop()
    {
        shop.Open();
        modePanel.gameObject.SetActive(!shop.IsOpen);
    }

    public void ShowRoomBrowser(bool visible)
    {
        if (roomBrowser != null) roomBrowser.gameObject.SetActive(visible);
        if (modePanel != null) modePanel.gameObject.SetActive(!visible && (shop == null || !shop.IsOpen));
    }

    private void ShowEquippedItem(string id)
    {
        var entry = previewSource != null ? previewSource.FindPreviewWeapon(id) : null;
        if (entry == null || entry.animator == null || entry.characterIdle == null || entry.weaponIdle == null ||
            entry.leftGrip == null || entry.rightGrip == null || displayedWeapon == id) return;
        if (weapon != null) weapon.gameObject.SetActive(false);
        weapon = displayCopies[entry.animator.transform];
        if (renderedWeapons.Add(weapon)) CopyRenderers(entry.animator.transform, displayCopies);
        weapon.gameObject.SetActive(true);
        characterIdle = entry.characterIdle; weaponIdle = entry.weaponIdle;
        previewIK?.SetGrips(displayCopies[entry.leftGrip], displayCopies[entry.rightGrip]);
        displayedWeapon = id;
        // Keep a selected pistol visible until a different primary is selected.
        equippedPrimary = YandexPlayerData.Current.equippedWeapon;
        elapsed = 0;
        SamplePose(0);
    }

    private static void CopyTransforms(Transform source, Transform parent, Dictionary<Transform, Transform> copies)
    {
        var copy = new GameObject(source.name).transform;
        copy.gameObject.layer = PreviewLayer; copy.SetParent(parent, false);
        copy.localPosition = source.localPosition; copy.localRotation = source.localRotation; copy.localScale = source.localScale;
        copies.Add(source, copy);
        foreach (Transform child in source) CopyTransforms(child, copy, copies);
    }

    private static void CopyRenderers(Transform source, Dictionary<Transform, Transform> copies)
    {
        foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
        {
            Renderer clone;
            var go = copies[renderer.transform].gameObject;
            if (renderer is SkinnedMeshRenderer skin)
            {
                var target = go.AddComponent<SkinnedMeshRenderer>();
                target.sharedMesh = skin.sharedMesh; target.localBounds = skin.localBounds;
                target.bones = System.Array.ConvertAll(skin.bones, bone => bone != null && copies.TryGetValue(bone, out var copy) ? copy : null);
                target.rootBone = skin.rootBone != null && copies.TryGetValue(skin.rootBone, out var rootBone) ? rootBone : null;
                target.updateWhenOffscreen = true;
                for (int i = 0; skin.sharedMesh != null && i < skin.sharedMesh.blendShapeCount; i++) target.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
                clone = target;
            }
            else if (renderer is MeshRenderer && renderer.TryGetComponent<MeshFilter>(out var filter))
            {
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                clone = go.AddComponent<MeshRenderer>();
            }
            else continue;
            clone.sharedMaterials = renderer.sharedMaterials;
            clone.shadowCastingMode = ShadowCastingMode.On; clone.receiveShadows = true;
        }
    }

    private void SamplePose(float time)
    {
        if (character != null && characterIdle != null) characterIdle.SampleAnimation(character.gameObject, Mathf.Repeat(time, Mathf.Max(.01f, characterIdle.length)));
        if (weapon != null && weaponIdle != null) weaponIdle.SampleAnimation(weapon.gameObject, Mathf.Repeat(time, Mathf.Max(.01f, weaponIdle.length)));
        if (weapon != null && previewIK != null)
        {
            previewIK.ApplyBreathing(time, Time.unscaledDeltaTime, true);
            previewIK.Solve();
        }
    }

    private void Update()
    {
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        bool browsing = roomBrowser != null && roomBrowser.gameObject.activeSelf;
        if (shop != null) modePanel.gameObject.SetActive(!shop.IsOpen && !browsing);
        if (walletPanel != null) walletPanel.SetActive(shop == null || !shop.IsOpen);
        if (menuFooter != null) menuFooter.SetActive((shop == null || !shop.IsOpen) && !browsing);
        if (weaponCaption != null) weaponCaption.transform.parent.gameObject.SetActive(!browsing);
        reveal = Mathf.Min(1, reveal + Time.unscaledDeltaTime / .3f);
        if (menuGroup != null) menuGroup.alpha = Mathf.SmoothStep(0, 1, reveal);
        if (wallet != null) wallet.text = YandexPlayerData.IsLoaded ? YandexPlayerData.Current.money.ToString("N0") + "  $" : GameLocalization.T("ЗАГРУЗКА…");
        if (weaponCaption != null) weaponCaption.text = GameLocalization.T(ShopCatalog.Find(displayedWeapon)?.title ?? "AK-74");
        if (YandexPlayerData.IsLoaded && equippedPrimary != YandexPlayerData.Current.equippedWeapon)
            ShowEquippedItem(YandexPlayerData.Current.equippedWeapon);
        elapsed += Time.unscaledDeltaTime;
        SamplePose(elapsed);
        ResizeTargets();
    }

    private void FramePortrait(float aspect)
    {
        // Waist-up on desktop. Pull back on narrow screens to keep the shoulders
        // and weapon inside the frame rather than clipping the portrait sideways.
        float distance = 2.7f * Mathf.Max(1f, 1.35f / aspect);
        Quaternion rotation = Quaternion.LookRotation(-portraitDirection, Vector3.up);
        float halfWidth = distance * Mathf.Tan(characterCamera.fieldOfView * .5f * Mathf.Deg2Rad) * aspect;
        float horizontalOffset = halfWidth * .52f; // Center at 76% of the viewport.
        characterCamera.transform.SetPositionAndRotation(
            portraitFocus + portraitDirection * distance - rotation * Vector3.right * horizontalOffset, rotation);
    }

    private void ResizeTargets()
    {
        int width = Mathf.Max(1, Screen.width), height = Mathf.Max(1, Screen.height);
        if (width == renderWidth && height == renderHeight) return;
        renderWidth = width; renderHeight = height;
        FramePortrait((float)width / height);
        ReleaseTargets();
        float scale = Mathf.Min(1, 1600f / width);
        characterTexture = new RenderTexture(Mathf.Max(1, Mathf.RoundToInt(width * scale)), Mathf.Max(1, Mathf.RoundToInt(height * scale)), 24, RenderTextureFormat.ARGB32);
        backgroundTexture = new RenderTexture(Mathf.Max(1, width / 3), Mathf.Max(1, height / 3), 24, RenderTextureFormat.ARGB32);
        characterTexture.Create(); backgroundTexture.Create();
        backgroundCamera.targetTexture = backgroundTexture; backgroundImage.texture = backgroundTexture;
        characterCamera.targetTexture = characterTexture; characterImage.texture = characterTexture;
    }

    private void ReleaseTargets()
    {
        if (backgroundCamera != null) backgroundCamera.targetTexture = null;
        if (characterCamera != null) characterCamera.targetTexture = null;
        if (backgroundTexture != null) { backgroundTexture.Release(); Destroy(backgroundTexture); }
        if (characterTexture != null) { characterTexture.Release(); Destroy(characterTexture); }
    }

    private void OnDisable()
    {
        if (stage != null) stage.SetActive(false);
        foreach (var camera in suspendedCameras) if (camera != null) camera.enabled = true;
        foreach (var listener in suspendedListeners) if (listener != null) listener.enabled = true;
        suspendedCameras.Clear(); suspendedListeners.Clear();
    }

    private void OnDestroy()
    {
        ReleaseTargets();
        if (stage != null) Destroy(stage);
        if (blurMaterial != null) Destroy(blurMaterial);
        if (shadeTexture != null) Destroy(shadeTexture);
    }

    private static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = min; rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero; return rect;
    }

    private static Text Label(Transform parent, string text, int size, Vector2 min, Vector2 max)
    {
        var label = Rect(text, parent, min, max).gameObject.AddComponent<Text>();
        label.font = GameUIStyle.Font; label.fontSize = size; label.color = new Color(.94f, .95f, .91f);
        label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false;
        label.resizeTextForBestFit = true; label.resizeTextMinSize = Mathf.Min(16, size); label.resizeTextMaxSize = size;
        GameLocalization.Bind(label, text); return label;
    }

    private static void ModeButton(Transform parent, string title, float bottom, float top, bool primary, UnityEngine.Events.UnityAction action, string description)
    {
        var rect = Rect(title, parent, new Vector2(0, bottom), new Vector2(1, top));
        var image = rect.gameObject.AddComponent<Image>(); image.color = Color.white;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        GameUIStyle.StyleButton(button, primary);
        button.onClick.AddListener(() => GameAudio.Effect("UI/click", Vector3.zero, .5f, 1, true));
        button.onClick.AddListener(action);
        var label = Label(rect, title, 23, new Vector2(.07f, .40f), new Vector2(.88f, .89f));
        label.fontStyle = FontStyle.Bold;
        label.color = primary ? new Color(.04f, .055f, .065f) : new Color(.90f, .93f, .94f);
        var hint = Label(rect, description, 16, new Vector2(.07f,.12f), new Vector2(.88f,.43f));
        hint.color = primary ? new Color(.22f,.23f,.20f) : GameUIStyle.Muted;
        var arrow = Label(rect, "›", 28, new Vector2(.89f,.1f), new Vector2(.97f,.9f));
        arrow.color = label.color;
    }
}
