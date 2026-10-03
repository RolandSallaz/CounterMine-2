using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Render-only model copies: no player, network or weapon scripts are instantiated.</summary>
public sealed class ShopWeaponPreview : MonoBehaviour, IDragHandler, IScrollHandler
{
    private GameObject stage, model;
    private Camera cameraView;
    private RenderTexture texture;
    private RawImage image;
    private Renderer[] modelRenderers;
    private Vector3 framingExtents;
    private float yaw = 115, pitch = -12, zoom = 1, radius = 1;
    private readonly Dictionary<Transform, Transform> copies = new Dictionary<Transform, Transform>();
    public WeaponIdleSynchronizer.WeaponEntry Entry { get; private set; }
    public bool HasModel => model != null;
    private void Awake()
    {
        image = GetComponent<RawImage>();
        stage = new GameObject("Shop preview stage"); stage.transform.position = new Vector3(0,-2500,0);
        var cameraObject = new GameObject("Shop preview camera"); cameraObject.transform.SetParent(stage.transform, false);
        cameraView = cameraObject.AddComponent<Camera>(); cameraView.enabled = false;
        cameraView.clearFlags = CameraClearFlags.SolidColor; cameraView.backgroundColor = new Color(.012f,.015f,.018f,1);
        cameraView.cullingMask = 1 << 30; cameraView.nearClipPlane = .01f; cameraView.farClipPlane = 25;
        cameraView.orthographic = true;
        foreach (float side in new[] { -1f, 1f })
        {
            var go = new GameObject("Preview light"); go.transform.SetParent(stage.transform, false);
            go.transform.localPosition = new Vector3(side * 2, 2, -2);
            var light = go.AddComponent<Light>(); light.type = LightType.Point; light.range = 9;
            light.intensity = side < 0 ? 12 : 8; light.color = side < 0 ? new Color(.78f,.88f,1) : new Color(1,.86f,.64f);
            light.cullingMask = 1 << 30;
        }
    }
    public void Show(string id)
    {
        if (model != null) { model.SetActive(false); Destroy(model); model = null; }
        copies.Clear();
        var prefab = Resources.Load<GameObject>("Player");
        Entry = prefab != null ? prefab.GetComponentInChildren<WeaponIdleSynchronizer>(true)?.FindPreviewWeapon(id) : null;
        image.enabled = Entry != null && Entry.animator != null;
        if (!image.enabled) return;
        model = new GameObject("Weapon turntable"); model.transform.SetParent(stage.transform, false);
        var source = Entry.animator.transform;
        Copy(source, model.transform);
        foreach (var original in source.GetComponentsInChildren<Renderer>(true))
        {
            var go = copies[original.transform].gameObject;
            Renderer renderer;
            if (original is SkinnedMeshRenderer skin)
            {
                var target = go.AddComponent<SkinnedMeshRenderer>(); target.sharedMesh = skin.sharedMesh;
                target.bones = System.Array.ConvertAll(skin.bones, b => b != null && copies.TryGetValue(b, out var bone) ? bone : null);
                target.rootBone = skin.rootBone != null && copies.TryGetValue(skin.rootBone, out var root) ? root : null;
                target.localBounds = skin.localBounds; target.updateWhenOffscreen = true; renderer = target;
            }
            else if (original is MeshRenderer && original.TryGetComponent<MeshFilter>(out var mesh))
            { go.AddComponent<MeshFilter>().sharedMesh = mesh.sharedMesh; renderer = go.AddComponent<MeshRenderer>(); }
            else continue;
            renderer.sharedMaterials = original.sharedMaterials;
        }
        var rootCopy = copies[source];
        rootCopy.localPosition = Vector3.zero; rootCopy.localRotation = Quaternion.identity;
        if (Entry.weaponIdle != null) Entry.weaponIdle.SampleAnimation(rootCopy.gameObject, 0);
        var renderers = modelRenderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) { image.enabled = false; return; }
        Bounds bounds = renderers[0].bounds;
        foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
        radius = Mathf.Max(.05f, bounds.extents.magnitude);
        rootCopy.position -= bounds.center - model.transform.position;
        ResetView();
        // Fit once at the default viewing angle. Rotation must not refit the camera.
        model.transform.localRotation = Quaternion.Euler(pitch, yaw, 0);
        Bounds initialFrame = renderers[0].bounds;
        foreach (var renderer in renderers) initialFrame.Encapsulate(renderer.bounds);
        framingExtents = initialFrame.extents;
    }
    private void Copy(Transform original, Transform parent)
    {
        var copy = new GameObject(original.name).transform; copy.gameObject.layer = 30;
        copy.SetParent(parent, false); copy.localPosition = original.localPosition;
        copy.localRotation = original.localRotation; copy.localScale = original.localScale;
        copies[original] = copy;
        foreach (Transform child in original) Copy(child, copy);
    }
    public void ResetView() { yaw = 115; pitch = -12; zoom = 1; }
    public void OnDrag(PointerEventData data)
    {
        if (data.button != PointerEventData.InputButton.Left || !HasModel) return;
        yaw -= data.delta.x * .4f; pitch = Mathf.Clamp(pitch + data.delta.y * .3f, -75,75);
    }
    public void OnScroll(PointerEventData data) { zoom = Mathf.Clamp(zoom - data.scrollDelta.y * .07f, .65f,1.5f); }
    private void LateUpdate()
    {
        if (model == null || !image.enabled || modelRenderers == null || modelRenderers.Length == 0) return;
        var size = image.rectTransform.rect.size;
        float scale = GetComponentInParent<Canvas>().scaleFactor;
        // Preserve the display aspect ratio even when capping GPU texture size.
        float resolutionScale = Mathf.Min(scale, 1920f / Mathf.Max(1, size.x), 1080f / Mathf.Max(1, size.y));
        int width = Mathf.Max(1, Mathf.CeilToInt(size.x * resolutionScale));
        int height = Mathf.Max(1, Mathf.CeilToInt(size.y * resolutionScale));
        if (texture == null || texture.width != width || texture.height != height)
        {
            Release(); texture = new RenderTexture(width,height,24,RenderTextureFormat.ARGB32);
            texture.antiAliasing = 2; texture.Create(); image.texture = texture; cameraView.targetTexture = texture;
        }
        model.transform.localRotation = Quaternion.Euler(pitch,yaw,0);

        // Frame inside the clear area between the catalogue and the text overlays.
        float aspect = (float)width / height;
        float clearHeight = .46f;
        cameraView.orthographicSize = Mathf.Max(.03f,
            Mathf.Max(framingExtents.y / clearHeight, framingExtents.x / (aspect * .62f))) * 1.12f * zoom;
        // Keep the turntable pivot fixed instead of chasing its rotated bounds.
        Vector3 center = Vector3.zero;
        float centerY = .60f;
        cameraView.transform.localPosition = new Vector3(
            center.x - .24f * cameraView.orthographicSize * aspect,
            center.y - (centerY * 2 - 1) * cameraView.orthographicSize, -radius * 4 - 1);
        cameraView.farClipPlane = radius * 8 + 5;
        cameraView.Render();
    }
    private void Release()
    {
        if (cameraView != null) cameraView.targetTexture = null;
        if (image != null) image.texture = null;
        if (texture != null) { texture.Release(); Destroy(texture); texture = null; }
    }
    private void OnEnable() { if (stage != null) stage.SetActive(true); }
    private void OnDisable() { if (stage != null) stage.SetActive(false); Release(); }
    private void OnDestroy() { Release(); if (stage != null) Destroy(stage); }
}
