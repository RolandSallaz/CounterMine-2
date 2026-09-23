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

    public static StartMenuScreen Create(LobbyManager lobby, GameObject playerPrefab, Vector3 position)
    {
        var root = new GameObject("Start Menu", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
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
        actor.rotation = Quaternion.LookRotation(direction) * Quaternion.Euler(0, -15, 0);
        backgroundCamera = MakeCamera("Map Camera", ~(1 << PreviewLayer), CameraClearFlags.Skybox);
        characterCamera = MakeCamera("Character Camera", 1 << PreviewLayer, CameraClearFlags.SolidColor);
        backgroundCamera.gameObject.AddComponent<AudioListener>();
        Vector3 cameraPosition = position + direction * Mathf.Max(1.5f, bestDistance - .2f) + Vector3.up * 1.25f;
        Quaternion rotation = Quaternion.LookRotation(position + Vector3.up * 1.0f - cameraPosition);
        Vector3 focus = position + Vector3.up * 1.0f - rotation * Vector3.right * .85f;
        rotation = Quaternion.LookRotation(focus - cameraPosition);
        backgroundCamera.transform.SetPositionAndRotation(cameraPosition, rotation);
        characterCamera.transform.SetPositionAndRotation(cameraPosition, rotation);

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

        var panel = Rect("Mode Selection", transform, new Vector2(.075f, .25f), new Vector2(.40f, .76f));
        var title = Label(panel, "COUNTERMINE", 38, new Vector2(0, .75f), Vector2.one);
        title.fontStyle = FontStyle.Bold;
        var line = Rect("Gold Accent", panel, new Vector2(0, .70f), new Vector2(.15f, .70f));
        line.sizeDelta = new Vector2(0, 2);
        line.gameObject.AddComponent<Image>().color = new Color(.88f, .76f, .49f);
        ModeButton(panel, "Single player", .37f, .56f, true, () => lobby.StartGame(true));
        ModeButton(panel, "Multiplayer", .12f, .31f, false, () => lobby.StartGame(false));
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
        var copies = new Dictionary<Transform, Transform>();
        CopyTransforms(prefab.transform, root, copies);
        character = copies[source.CharacterAnimator.transform];
        characterIdle = source.PreviewCharacterIdle;
        CopyRenderers(source.CharacterAnimator.transform, copies);
        if (source.WeaponRoot != null)
        {
            weapon = copies[source.WeaponRoot];
            weaponIdle = source.PreviewWeaponIdle;
            CopyRenderers(source.WeaponRoot, copies);
        }
        SamplePose(0);
        var renderers = root.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            copies[prefab.transform].position -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
        }
        return root;
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
    }

    private void Update()
    {
        Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        elapsed += Time.unscaledDeltaTime;
        SamplePose(elapsed);
        ResizeTargets();
    }

    private void ResizeTargets()
    {
        int width = Mathf.Max(1, Screen.width), height = Mathf.Max(1, Screen.height);
        if (width == renderWidth && height == renderHeight) return;
        renderWidth = width; renderHeight = height;
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
        label.resizeTextForBestFit = true; label.resizeTextMinSize = 12; label.resizeTextMaxSize = size;
        GameLocalization.Bind(label, text); return label;
    }

    private static void ModeButton(Transform parent, string title, float bottom, float top, bool primary, UnityEngine.Events.UnityAction action)
    {
        var rect = Rect(title, parent, new Vector2(0, bottom), new Vector2(1, top));
        var image = rect.gameObject.AddComponent<Image>(); image.color = Color.white;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        var colors = button.colors;
        colors.normalColor = primary ? new Color(.88f, .79f, .59f) : new Color(.09f, .12f, .14f, .92f);
        colors.highlightedColor = primary ? new Color(1, .91f, .72f) : new Color(.18f, .23f, .26f);
        colors.pressedColor = primary ? new Color(.70f, .60f, .41f) : new Color(.06f, .08f, .10f);
        colors.selectedColor = colors.highlightedColor; colors.fadeDuration = .12f; button.colors = colors;
        button.onClick.AddListener(action);
        var label = Label(rect, title, 23, new Vector2(.07f, 0), new Vector2(.93f, 1));
        label.fontStyle = FontStyle.Bold;
        label.color = primary ? new Color(.04f, .055f, .065f) : new Color(.90f, .93f, .94f);
    }
}
