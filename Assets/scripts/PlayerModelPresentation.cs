using System;
using System.Collections.Generic;
using Animancer;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Local camera arms and a world-space body; remote players always use the complete body.</summary>
[DefaultExecutionOrder(250)]
[DisallowMultipleComponent]
public sealed class PlayerModelPresentation : MonoBehaviourPun
{
    [SerializeField, Min(0f)] private float bodyBackOffset = .15f;
    [SerializeField] private string armsMeshName = "arms";
    [SerializeField] private Vector3 thirdPersonWeaponOffset = new Vector3(0f, -.25f, .10f);
    public Vector3 ThirdPersonWeaponOffset => thirdPersonWeaponOffset;
    private Transform weaponPresentationPivot;
    private Transform worldModel, fpsModel;
    private Camera viewCamera;
    private WeaponIdleSynchronizer animationSource;
    private AnimancerComponent worldAnimation, fpsAnimation;
    private WeaponHandIK worldIK;
    private SkinnedMeshRenderer[] worldRenderers;
    private readonly Dictionary<Transform, Transform> copies = new Dictionary<Transform, Transform>();
    private bool configured, localView, inRagdoll;
    private Transform lastWeapon;

    private void Start() => ConfigureView(!PhotonNetwork.InRoom || photonView.IsMine);

    public void ConfigureView(bool local)
    {
        local &= !BotController.IsBot(this);
        if (!configured)
        {
            animationSource = GetComponentInChildren<WeaponIdleSynchronizer>(true);
            worldAnimation = animationSource != null ? animationSource.CharacterAnimator : null;
            if (worldAnimation == null) return;
            worldModel = worldAnimation.transform;
            worldIK = worldModel.GetComponent<WeaponHandIK>();
            if (worldIK != null) worldIK.SetThirdPersonFrame(transform);
            viewCamera = GetComponentInChildren<Camera>(true);
            worldRenderers = worldModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            worldModel.SetParent(transform, true);
            worldModel.localPosition += Vector3.back * bodyBackOffset;
            configured = true;
        }
        localView = local;
        ConfigureWeaponPosition();
        worldModel.gameObject.SetActive(true);
        worldAnimation.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (local && fpsModel == null) CreateFirstPersonArms();
        if (fpsModel != null) fpsModel.gameObject.SetActive(local && !inRagdoll);
        animationSource.SetCharacterAnimator(local && !inRagdoll && fpsAnimation != null ? fpsAnimation : worldAnimation);
        foreach (var renderer in worldRenderers)
        {
            renderer.gameObject.SetActive(true);
            renderer.enabled = true;
            renderer.gameObject.layer = gameObject.layer;
            renderer.forceRenderingOff = false;
            renderer.updateWhenOffscreen = true;
            renderer.shadowCastingMode = local && !inRagdoll && IsArms(renderer) ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }
        UpdateWeaponShadows();
    }

    private void ConfigureWeaponPosition()
    {
        if (weaponPresentationPivot == null)
        {
            var aim = GetComponentInChildren<WeaponAimController>(true);
            if (aim == null || aim.transform.parent == null) return;
            // Keep the authored/networked ADS, sway and recoil local poses intact.
            // The extra parent moves the weapon and both grip targets together,
            // before WeaponHandIK solves the third-person arms.
            weaponPresentationPivot = new GameObject("WeaponPresentationPivot").transform;
            weaponPresentationPivot.SetParent(aim.transform.parent, false);
            aim.transform.SetParent(weaponPresentationPivot, false);
        }
        weaponPresentationPivot.localPosition = localView ? Vector3.zero : thirdPersonWeaponOffset;
    }

    private bool IsArms(SkinnedMeshRenderer renderer) =>
        string.Equals(renderer.name, armsMeshName, StringComparison.OrdinalIgnoreCase) ||
        (renderer.sharedMesh != null && string.Equals(renderer.sharedMesh.name, armsMeshName, StringComparison.OrdinalIgnoreCase));

    private Transform CopyHierarchy(Transform source, Transform parent)
    {
        var copy = new GameObject(source.name).transform;
        copy.SetParent(parent, false); copy.localPosition = source.localPosition;
        copy.localRotation = source.localRotation; copy.localScale = source.localScale;
        copies.Add(source, copy);
        foreach (Transform child in source) CopyHierarchy(child, copy);
        return copy;
    }

    private void CreateFirstPersonArms()
    {
        if (viewCamera == null || !Array.Exists(worldRenderers, IsArms))
        { Debug.LogError("FPS arms mesh or camera is missing.", this); return; }
        fpsModel = CopyHierarchy(worldModel, viewCamera.transform);
        fpsModel.name = "FPSArms";
        // Preserve the authored origin while making camera motion drive the viewmodel.
        fpsModel.SetPositionAndRotation(worldModel.position + transform.forward * bodyBackOffset, worldModel.rotation);
        foreach (var source in worldRenderers)
        {
            if (!IsArms(source)) continue;
            var target = copies[source.transform].gameObject.AddComponent<SkinnedMeshRenderer>();
            target.sharedMesh = source.sharedMesh; target.sharedMaterials = source.sharedMaterials;
            target.bones = Array.ConvertAll(source.bones, bone => copies[bone]);
            target.rootBone = source.rootBone ? copies[source.rootBone] : fpsModel;
            target.localBounds = source.localBounds; target.updateWhenOffscreen = true;
            target.shadowCastingMode = ShadowCastingMode.Off; target.receiveShadows = false;
        }
        var animator = fpsModel.gameObject.AddComponent<Animator>();
        animator.avatar = worldAnimation.Animator.avatar;
        animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        fpsAnimation = fpsModel.gameObject.AddComponent<AnimancerComponent>(); fpsAnimation.Animator = animator;
        if (worldIK != null)
        {
            var ik = fpsModel.gameObject.AddComponent<WeaponHandIK>();
            worldIK.CopyConfigurationTo(ik, copies);
        }
        worldAnimation.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
    }

    private void LateUpdate()
    {
        if (!configured || inRagdoll) return;
        if (localView && fpsModel != null && animationSource.LastEvaluatedFrame == Time.frameCount)
            CopyAnimatedPoseToBody(); // Before both arm IK solvers (order 300).
        if (lastWeapon != animationSource.WeaponRoot) UpdateWeaponShadows();
    }

    private void CopyAnimatedPoseToBody()
    {
        foreach (var pair in copies)
        {
            if (pair.Key == worldModel) continue;
            pair.Key.localPosition = pair.Value.localPosition;
            pair.Key.localRotation = pair.Value.localRotation;
            pair.Key.localScale = pair.Value.localScale;
        }
    }

    private void UpdateWeaponShadows()
    {
        lastWeapon = animationSource.WeaponRoot;
        if (lastWeapon == null) return;
        foreach (var renderer in lastWeapon.GetComponentsInChildren<Renderer>(true))
            renderer.shadowCastingMode = localView && !inRagdoll ? ShadowCastingMode.Off : ShadowCastingMode.On;
    }

    public void BeginRagdoll()
    {
        if (!configured) ConfigureView(!PhotonNetwork.InRoom || photonView.IsMine);
        if (!configured) return;
        if (localView && fpsModel != null) CopyAnimatedPoseToBody();
        inRagdoll = true;
        ConfigureView(localView);
    }

    public void EndRagdoll()
    {
        inRagdoll = false;
        ConfigureView(localView);
    }
}
