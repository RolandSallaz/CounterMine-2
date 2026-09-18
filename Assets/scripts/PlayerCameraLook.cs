using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(PhotonView))]
[DefaultExecutionOrder(-60)]
public class PlayerCameraLook : MonoBehaviourPun
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private WeaponAimController aimController;
    [SerializeField, Min(0.01f)] private float sensitivity = 0.12f;
    [SerializeField] private float minimumPitch = -80f;
    [SerializeField] private float maximumPitch = 80f;
    [SerializeField] private float maximumLeanRoll = 8f;
    [Header("Movement camera inertia")]
    [SerializeField, Range(0f, 3f)] private float strafeRoll = 1.7f;
    [SerializeField, Range(0f, 2f)] private float walkRoll = .5f;
    [SerializeField, Range(0f, 2f)] private float sprintRoll = 1.05f;
    [SerializeField, Min(.01f)] private float rollSmoothTime = .12f;
    [Header("Explosion shake")]
    [SerializeField, Min(0f)] private float maxShakeDegrees = 3f;
    [SerializeField, Min(.1f)] private float shakeDecay = 1.4f;

    private WeaponIdleSynchronizer weaponAnimation;
    private float pitch;
    private float lean;
    private PlayerController movement;
    private CharacterController capsule;
    private float movementRoll, rollVelocity, stridePhase;
    private float trauma, shakeTime;

    public Vector2 LookDeltaThisFrame { get; private set; }

    private void Awake()
    {
        weaponAnimation = GetComponentInChildren<WeaponIdleSynchronizer>(true);
        movement = GetComponent<PlayerController>();
        capsule = GetComponent<CharacterController>();
        aimController ??= GetComponentInChildren<WeaponAimController>(true);
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
        }

        if (playerCamera != null)
        {
            pitch = NormalizeAngle(playerCamera.transform.localEulerAngles.x);
        }
    }

    private void Start()
    {
        if (!photonView.IsMine)
        {
            return;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        LookDeltaThisFrame = Vector2.zero;
        if (!photonView.IsMine || playerCamera == null)
        {
            return;
        }

        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (Mouse.current == null || Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        float aimSensitivity = aimController != null && aimController.isActiveAndEnabled ? aimController.LookSensitivityMultiplier : 1f;
        Vector2 mouseDelta = Mouse.current.delta.ReadValue() * (sensitivity * aimSensitivity);
        float previousPitch = pitch;
        pitch = Mathf.Clamp(pitch - mouseDelta.y, minimumPitch, maximumPitch);
        LookDeltaThisFrame = new Vector2(mouseDelta.x, previousPitch - pitch);

        transform.Rotate(Vector3.up, mouseDelta.x, Space.Self);
        ApplyRotation();
    }

    public void SetLean(float normalizedLean)
    {
        lean = Mathf.Clamp(normalizedLean, -1f, 1f);
        ApplyRotation();
    }

    public void AddRecoil(Vector2 recoilDegrees)
    {
        if (!isActiveAndEnabled || !photonView.IsMine || playerCamera == null) return;
        // Changes the actual aim direction, so mouse movement can compensate for recoil.
        pitch = Mathf.Clamp(pitch - recoilDegrees.y, minimumPitch, maximumPitch);
        transform.Rotate(Vector3.up, recoilDegrees.x, Space.Self);
        ApplyRotation();
    }

    /// <summary>Visual-only trauma shake: never moves the aim, decays on its own.</summary>
    public void AddShake(float strength)
    {
        if (!isActiveAndEnabled || !photonView.IsMine) return;
        trauma = Mathf.Clamp01(Mathf.Max(trauma, strength));
    }

    private void LateUpdate()
    {
        if (!PhotonNetwork.InRoom || photonView.IsMine)
        {
            if (trauma > 0f)
            {
                trauma = Mathf.Max(0f, trauma - shakeDecay * Time.deltaTime);
                shakeTime += Time.deltaTime;
            }
            UpdateMovementRoll();
            ApplyRotation();
        }
    }

    private void UpdateMovementRoll()
    {
        float target = 0f;
        if (movement != null && movement.isActiveAndEnabled && capsule != null && capsule.enabled &&
            movement.IsGrounded && !movement.IsSliding)
        {
            Vector3 velocity = Vector3.ProjectOnPlane(capsule.velocity, Vector3.up);
            float speed = velocity.magnitude;
            // Advance with distance, so a blocked character does not sway in place.
            stridePhase = Mathf.Repeat(stridePhase + speed * Time.deltaTime * (2f * Mathf.PI / 3.2f), 2f * Mathf.PI);
            float lateral = Vector3.Dot(velocity, transform.right);
            float sway = Mathf.Sin(stridePhase) * (movement.IsSprinting ? sprintRoll : walkRoll);
            target = -Mathf.Clamp(lateral / 3.2f, -1f, 1f) * strafeRoll + sway * Mathf.Clamp01(speed / 3.2f);
            target *= Mathf.Lerp(1f, .2f, aimController != null ? aimController.AimAmount : 0f);
            if (movement.IsCrouching) target *= .5f;
            if (weaponAnimation != null && weaponAnimation.IsPlayingAction) target *= .2f;
        }
        movementRoll = Mathf.SmoothDamp(movementRoll, target, ref rollVelocity, rollSmoothTime, Mathf.Infinity, Time.deltaTime);
    }

    private void OnDisable()
    {
        movementRoll = rollVelocity = stridePhase = 0f;
        trauma = shakeTime = 0f;
    }

    private void ApplyRotation()
    {
        if (playerCamera != null)
        {
            Quaternion animated = weaponAnimation != null ? weaponAnimation.CameraRotationOffset : Quaternion.identity;
            playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, -lean * maximumLeanRoll + movementRoll) * animated * ShakeOffset();
        }
    }

    private Quaternion ShakeOffset()
    {
        if (trauma <= 0f) return Quaternion.identity;
        float s = trauma * trauma * maxShakeDegrees;
        return Quaternion.Euler(
            (Mathf.Sin(shakeTime * 39.7f) + Mathf.Sin(shakeTime * 23.3f) * .6f) * s,
            (Mathf.Sin(shakeTime * 31.9f + 1.7f) + Mathf.Sin(shakeTime * 27.1f) * .6f) * s,
            Mathf.Sin(shakeTime * 35.3f + .6f) * s * .7f);
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }
}
