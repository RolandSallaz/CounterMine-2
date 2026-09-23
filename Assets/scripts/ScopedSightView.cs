using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>Owner-only picture-in-picture optics. Only the physical lens displays the magnified view.</summary>
[DefaultExecutionOrder(1000)]
public sealed class ScopedSightView : MonoBehaviour
{
    public const int MinimumMagnification = 6, MaximumMagnification = 16;
    public Renderer lensRenderer;
    [Min(.001f)] public float lensRadius = .0161f;
    [SerializeField, Range(256, 1024)] private int textureSize = 768;
    public int Magnification { get; private set; } = MinimumMagnification;
    public float SensitivityMultiplier => (float)MinimumMagnification / Magnification;
    public Camera ScopeCamera => scopeCamera;
    public RenderTexture ViewTexture => viewTexture;
    private WeaponAimController aim;
    private Camera viewCamera, scopeCamera;
    private PlayerHealth health;
    private PhotonView owner;
    private RenderTexture viewTexture;
    private MaterialPropertyBlock properties;
    private Canvas labelCanvas;
    private Text zoomLabel;
    private Renderer[] hidden;
    private bool[] previous;
    private readonly UniversalRenderPipeline.SingleCameraRequest request = new UniversalRenderPipeline.SingleCameraRequest();
    private static readonly int ScopeTexture = Shader.PropertyToID("_ScopeTex");
    public bool Visible => isActiveAndEnabled && lensRenderer != null && aim != null && aim.AimAmount > .9f &&
        aim.CurrentWeapon != null && aim.CurrentWeapon.transform == transform && health != null && !health.IsDead &&
        viewCamera != null && viewCamera.isActiveAndEnabled && !BotController.IsBot(health) &&
        (!PhotonNetwork.InRoom || (owner != null && owner.IsMine));

    private void OnEnable()
    {
        owner = GetComponentInParent<PhotonView>();
        if (owner == null) return;
        aim = owner.GetComponentInChildren<WeaponAimController>(true);
        if (viewCamera == null) viewCamera = owner.GetComponentInChildren<Camera>(true);
        health = owner.GetComponent<PlayerHealth>();
        if (lensRenderer != null) lensRenderer.enabled = false;
    }
    public void SetMagnification(int value) => Magnification = Mathf.Clamp(value, MinimumMagnification, MaximumMagnification);
    private void Update()
    {
        if (!Visible || !Application.isFocused || Cursor.lockState != CursorLockMode.Locked ||
            Keyboard.current?.tabKey.isPressed == true || Mouse.current == null) return;
        float wheel = Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(wheel) > .001f) SetMagnification(Magnification + (wheel > 0 ? 1 : -1));
    }
    // Account for the fraction of the display occupied by the physical lens. Dividing
    // the main-camera FOV by zoom alone would under-magnify a small PiP window.
    public static float CalculateFieldOfView(float mainFov, float lensViewportHeight, int magnification) =>
        2f * Mathf.Atan(Mathf.Tan(mainFov * Mathf.Deg2Rad * .5f) * Mathf.Max(.001f, lensViewportHeight) /
            Mathf.Clamp(magnification, MinimumMagnification, MaximumMagnification)) * Mathf.Rad2Deg;

    private void LateUpdate()
    {
        if (!Visible)
        {
            if (lensRenderer != null) lensRenderer.enabled = false;
            if (labelCanvas != null) labelCanvas.gameObject.SetActive(false);
            return;
        }
        EnsureResources();
        var lens = lensRenderer.transform;
        var top = viewCamera.WorldToViewportPoint(lens.position + lens.up * lensRadius);
        var bottom = viewCamera.WorldToViewportPoint(lens.position - lens.up * lensRadius);
        float fraction = Mathf.Abs(top.y - bottom.y);
        scopeCamera.transform.SetPositionAndRotation(viewCamera.transform.position, viewCamera.transform.rotation);
        scopeCamera.fieldOfView = Mathf.Clamp(CalculateFieldOfView(viewCamera.fieldOfView, fraction, Magnification), .1f, 60f);
        scopeCamera.cullingMask = viewCamera.cullingMask & ~(1 << 5); // No HUD in the scope image.
        scopeCamera.clearFlags = viewCamera.clearFlags; scopeCamera.backgroundColor = viewCamera.backgroundColor;
        scopeCamera.farClipPlane = viewCamera.farClipPlane; scopeCamera.nearClipPlane = viewCamera.nearClipPlane;
#if UNITY_EDITOR
        scopeCamera.scene = viewCamera.scene;
#endif
        RenderScope();
        lensRenderer.enabled = true;
        zoomLabel.text = Magnification + "?";
        labelCanvas.gameObject.SetActive(true);
        var position = viewCamera.WorldToScreenPoint(lens.position - lens.up * (lensRadius + .007f));
        zoomLabel.rectTransform.position = new Vector3(position.x, position.y, 0);
    }
    private void EnsureResources()
    {
        if (scopeCamera == null)
        {
            var go = new GameObject("L115A3 PiP Camera", typeof(Camera)); go.transform.SetParent(transform, false);
            scopeCamera = go.GetComponent<Camera>(); scopeCamera.enabled = false;
            scopeCamera.aspect = 1; scopeCamera.allowHDR = false; scopeCamera.allowMSAA = false;
            var data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderType = CameraRenderType.Base; data.renderPostProcessing = false;
            data.requiresColorOption = CameraOverrideOption.Off; data.requiresDepthOption = CameraOverrideOption.Off;
        }
        if (viewTexture == null)
        {
            viewTexture = new RenderTexture(textureSize, textureSize, 24, RenderTextureFormat.ARGB32)
                { name = "L115A3 PiP", antiAliasing = 1, useMipMap = false, autoGenerateMips = false, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            viewTexture.Create(); scopeCamera.targetTexture = viewTexture; request.destination = viewTexture;
            properties ??= new MaterialPropertyBlock(); lensRenderer.GetPropertyBlock(properties);
            properties.SetTexture(ScopeTexture, viewTexture); lensRenderer.SetPropertyBlock(properties);
        }
        if (labelCanvas == null)
        {
            var root = new GameObject("L115A3 Magnification", typeof(RectTransform), typeof(Canvas)); root.transform.SetParent(transform, false);
            labelCanvas = root.GetComponent<Canvas>(); labelCanvas.renderMode = RenderMode.ScreenSpaceOverlay; labelCanvas.sortingOrder = 30;
            var go = new GameObject("Zoom", typeof(RectTransform), typeof(Text), typeof(Outline)); go.transform.SetParent(root.transform, false);
            zoomLabel = go.GetComponent<Text>(); zoomLabel.font = GameUIStyle.Font; zoomLabel.fontSize = 18; zoomLabel.alignment = TextAnchor.MiddleCenter;
            zoomLabel.color = Color.white; zoomLabel.raycastTarget = false; zoomLabel.rectTransform.sizeDelta = new Vector2(100, 30);
            go.GetComponent<Outline>().effectColor = new Color(0, 0, 0, .85f);
        }
    }
    private void RenderScope()
    {
        // Hide only this player's body/viewmodel during the additional camera render.
        // Restore in finally so the main view and all other cameras retain the weapon.
        if (hidden == null) { hidden = owner.GetComponentsInChildren<Renderer>(true); previous = new bool[hidden.Length]; }
        for (int i = 0; i < hidden.Length; i++) if (hidden[i] != null)
        { previous[i] = hidden[i].forceRenderingOff; hidden[i].forceRenderingOff = true; }
        try
        {
            if (RenderPipeline.SupportsRenderRequest(scopeCamera, request)) RenderPipeline.SubmitRenderRequest(scopeCamera, request);
            else scopeCamera.Render();
        }
        finally
        {
            for (int i = 0; i < hidden.Length; i++) if (hidden[i] != null) hidden[i].forceRenderingOff = previous[i];
        }
    }
    private void OnDisable()
    {
        if (lensRenderer != null) lensRenderer.enabled = false;
        if (labelCanvas != null) labelCanvas.gameObject.SetActive(false);
        if (scopeCamera != null) scopeCamera.enabled = false;
    }
    private void OnDestroy()
    {
        if (lensRenderer != null) lensRenderer.SetPropertyBlock(null);
        if (scopeCamera != null) scopeCamera.targetTexture = null;
        if (viewTexture != null)
        {
            viewTexture.Release();
            if (Application.isPlaying) Destroy(viewTexture); else DestroyImmediate(viewTexture);
            viewTexture = null;
        }
    }
}
