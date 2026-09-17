using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Universal ADS: align the currently mounted sight, independent of weapon model or optic height.</summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
public sealed class WeaponAimController : MonoBehaviour
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private PlayerCameraLook cameraLook;
    [SerializeField] private PlayerDeathController deathController;
    [SerializeField] private WeaponAimRig weapon;
    [SerializeField] private WeaponIdleSynchronizer weaponAnimation;
    private Vector3 restPosition, alignmentPosition, switchPosition;
    private Quaternion restRotation, alignmentRotation, switchRotation;
    private float restFieldOfView, progress, switchProgress = 1f, alignmentFov, switchFov;
    private bool initialized, hasAlignment;
    private WeaponSight lastSight;
    public WeaponAimRig CurrentWeapon => weapon;
    private WeaponHandlingProfile Handling => weapon != null ? weapon.Handling : null;
    public bool IsAiming { get; private set; }
    public float AimAmount { get; private set; }
    public float ErgonomicsNormalized => weapon != null ? weapon.Ergonomics / 100f : .55f;
    public float AimInTime => Mathf.Max(.01f, Mathf.Lerp(Handling != null ? Handling.lowErgonomicsAimTime : .5f,
        Handling != null ? Handling.highErgonomicsAimTime : .16f, ErgonomicsNormalized));
    public float LookSensitivityMultiplier => Mathf.Lerp(1f, Handling != null ? Handling.aimedSensitivity : .7f, AimAmount);
    public float MovementSpeedMultiplier => Mathf.Lerp(1f, Handling != null ? Handling.aimedMovementSpeed : .65f, AimAmount);
    public float SwayMultiplier => Mathf.Lerp(1f, Mathf.Lerp(.4f, .16f, ErgonomicsNormalized), AimAmount);
    public bool BlocksSprint => isActiveAndEnabled && (IsAiming || AimAmount > .01f);

    private void Awake()
    {
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        if (playerCamera != null) restFieldOfView = playerCamera.fieldOfView;
        initialized = true;
    }
    public void Equip(WeaponAimRig nextWeapon)
    {
        if (nextWeapon != null && !nextWeapon.transform.IsChildOf(transform))
        {
            Debug.LogError("Mount the weapon under WeaponAimPivot before equipping it.", nextWeapon);
            return;
        }
        weapon = nextWeapon;
    }
    private void Update()
    {
        bool canAim = playerCamera != null && playerCamera.isActiveAndEnabled &&
            (weaponAnimation == null || weaponAnimation.IsIdlePlaying) &&
            cameraLook != null && cameraLook.isActiveAndEnabled &&
            (deathController == null || !deathController.IsDead) &&
            Application.isFocused && Cursor.lockState == CursorLockMode.Locked &&
            Keyboard.current?.escapeKey.wasPressedThisFrame != true;
        Step(canAim && Mouse.current?.rightButton.isPressed == true, Time.deltaTime);
    }
    private void Step(bool wantsAim, float deltaTime)
    {
        var sight = weapon != null && weapon.isActiveAndEnabled ? weapon.ActiveSight : null;
        IsAiming = wantsAim && sight != null && sight.AimPoint.IsChildOf(transform);
        if (deltaTime <= 0f) return;
        float duration = AimInTime * (IsAiming ? 1f : Handling != null ? Handling.aimOutTimeMultiplier : .8f);
        progress = Mathf.MoveTowards(progress, IsAiming ? 1f : 0f, deltaTime / Mathf.Max(.01f, duration));
        AimAmount = Mathf.SmoothStep(0f, 1f, progress);
    }
    // All animation Updates have finished. Sway, recoil and hand IK run after this LateUpdate.
    private void LateUpdate() => ApplyPose(Time.deltaTime);
    private void ApplyPose(float deltaTime)
    {
        if (!initialized || deltaTime <= 0f) return;
        var sight = weapon != null && weapon.isActiveAndEnabled ? weapon.ActiveSight : null;
        if (sight != null && TryCalculateAlignment(sight, out var position, out var rotation))
        {
            float fov = Mathf.Min(restFieldOfView, sight.AimedFieldOfView);
            if (sight != lastSight)
            {
                switchPosition = hasAlignment ? alignmentPosition : position;
                switchRotation = hasAlignment ? alignmentRotation : rotation;
                switchFov = hasAlignment ? alignmentFov : fov;
                switchProgress = hasAlignment ? 0f : 1f;
                lastSight = sight;
            }
            switchProgress = Mathf.MoveTowards(switchProgress, 1f, deltaTime / Mathf.Max(.01f, Handling != null ? Handling.sightSwitchTime : .16f));
            float blend = Mathf.SmoothStep(0f, 1f, switchProgress);
            alignmentPosition = Vector3.Lerp(switchPosition, position, blend);
            alignmentRotation = Quaternion.Slerp(switchRotation, rotation, blend);
            alignmentFov = Mathf.Lerp(switchFov, fov, blend);
            hasAlignment = true;
        }
        if (hasAlignment)
        {
            transform.localPosition = Vector3.Lerp(restPosition, alignmentPosition, AimAmount);
            transform.localRotation = Quaternion.Slerp(restRotation, alignmentRotation, AimAmount);
            if (playerCamera != null) playerCamera.fieldOfView = Mathf.Lerp(restFieldOfView, alignmentFov, AimAmount);
        }
    }
    private bool TryCalculateAlignment(WeaponSight sight, out Vector3 position, out Quaternion rotation)
    {
        position = restPosition; rotation = restRotation;
        if (playerCamera == null || transform.parent == null || !sight.AimPoint.IsChildOf(transform)) return false;
        Matrix4x4 relative = Matrix4x4.identity;
        Quaternion relativeRotation = Quaternion.identity;
        for (var node = sight.AimPoint; node != transform; node = node.parent)
        {
            Vector3 localPosition = node.localPosition;
            Quaternion localRotation = node.localRotation;
            // Strip last frame's procedural offsets from the alignment calculation, not from the rendered pose.
            if (node.TryGetComponent<WeaponSway>(out var sway))
            { localPosition = sway.NeutralLocalPosition; localRotation = sway.NeutralLocalRotation; }
            if (node.TryGetComponent<WeaponRecoilController>(out var recoil))
            { localPosition = recoil.NeutralLocalPosition; localRotation = recoil.NeutralLocalRotation; }
            relative = Matrix4x4.TRS(localPosition, localRotation, node.localScale) * relative;
            relativeRotation = localRotation * relativeRotation;
        }
        rotation = Quaternion.Inverse(transform.parent.rotation) * playerCamera.transform.rotation * Quaternion.Inverse(relativeRotation);
        Vector3 desiredPoint = playerCamera.transform.position + playerCamera.transform.forward * Mathf.Max(sight.EyeRelief, playerCamera.nearClipPlane + .01f);
        position = transform.parent.InverseTransformPoint(desiredPoint) - rotation * Vector3.Scale(transform.localScale, relative.MultiplyPoint3x4(Vector3.zero));
        return true;
    }
    private void OnDisable()
    {
        IsAiming = false; progress = AimAmount = 0f;
        hasAlignment = false; lastSight = null;
        if (!initialized) return;
        transform.localPosition = restPosition;
        transform.localRotation = restRotation;
        if (playerCamera != null) playerCamera.fieldOfView = restFieldOfView;
    }
}
