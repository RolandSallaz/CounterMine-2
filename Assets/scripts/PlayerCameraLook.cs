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
    [SerializeField, Min(0f)] private float maxShakeDegrees = 4.2f;
    [SerializeField, Min(.1f)] private float shakeDecay = 1.4f;
    [SerializeField, Range(0f, 12f)] private float explosionKickDegrees = 6f;
    [SerializeField, Range(0f, 10f)] private float explosionFovKick = 5f;
    [SerializeField, Range(0f, 1f)] private float aimedShakeMultiplier = .65f;

    private WeaponIdleSynchronizer weaponAnimation;
    private float pitch;
    private float lean;
    private PlayerController movement;
    private CharacterController capsule;
    private float movementRoll, rollVelocity, stridePhase;
    private float trauma, shakeTime;
    private Vector3 blastKick, blastVelocity;
    private float blastAge = 10f, blastStrength;
    private float noiseSeed;

    public float ExplosionFovOffset => isActiveAndEnabled
        ? explosionFovKick * blastStrength * 1.7f * (1f - Mathf.Exp(-60f * blastAge)) * Mathf.Exp(-10f * blastAge)
            * Mathf.Lerp(1f, aimedShakeMultiplier, aimController != null ? aimController.AimAmount : 0f)
        : 0f;

    public Vector2 LookDeltaThisFrame { get; private set; }

    private void Awake()
    {
        noiseSeed = (GetInstanceID() & 1023) * .137f;
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

    /// <summary>Transient camera shake; does not accumulate into the player's pitch/yaw input.</summary>
    public void AddShake(float strength)
    {
        if (!isActiveAndEnabled || (PhotonNetwork.InRoom && !photonView.IsMine)) return;
        strength = Mathf.Clamp01(strength);
        trauma = Mathf.Clamp01(Mathf.Sqrt(trauma * trauma + strength * strength));
    }

    public void AddExplosionShake(Vector3 source, float strength)
    {
        if (!isActiveAndEnabled || playerCamera == null || (PhotonNetwork.InRoom && !photonView.IsMine)) return;
        strength = Mathf.Clamp01(strength);
        if (strength <= 0f) return;
        AddShake(strength);
        Vector3 incoming = transform.InverseTransformDirection((source - playerCamera.transform.position).normalized);
        // Push away from the blast, followed by a damped spring rebound.
        Vector3 impulse = new Vector3(-.8f - incoming.z * .25f, -incoming.x * .55f, incoming.x * .8f);
        blastVelocity = Vector3.ClampMagnitude(blastVelocity + impulse * (explosionKickDegrees * strength * 34f), 300f);
        blastStrength = Mathf.Clamp01(blastStrength * Mathf.Exp(-10f * blastAge) + strength);
        blastAge = 0f;
    }

    private void LateUpdate()
    {
        if (!PhotonNetwork.InRoom || photonView.IsMine)
        {
            float dt = Time.deltaTime;
            blastAge += dt;
            // Exact damped oscillator integration keeps the kick consistent at different frame rates.
            const float damping = 9f, frequency = 23f;
            float decay = Mathf.Exp(-damping * dt);
            float sin = Mathf.Sin(frequency * dt), cos = Mathf.Cos(frequency * dt);
            Vector3 previous = blastKick;
            Vector3 c = (blastVelocity + damping * previous) / frequency;
            blastKick = (previous * cos + c * sin) * decay;
            blastVelocity = (blastVelocity * cos - (damping * c + frequency * previous) * sin) * decay;
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
            float aim = aimController != null ? aimController.AimAmount : 0f;
            float cadence = movement.IsSprinting ? 1f : Mathf.Lerp(.5f, 1f, aim);
            // Advance with distance, so a blocked character does not sway in place.
            stridePhase = Mathf.Repeat(stridePhase + speed * Time.deltaTime * (2f * Mathf.PI / 3.2f) * cadence, 2f * Mathf.PI);
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
        blastKick = blastVelocity = Vector3.zero;
        blastAge = 10f;
        blastStrength = 0f;
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
        float s = trauma * trauma * maxShakeDegrees;
        float t = shakeTime * 24f;
        Vector3 noise = new Vector3(
            Mathf.PerlinNoise(noiseSeed, t) * 2f - 1f,
            Mathf.PerlinNoise(noiseSeed + 17.3f, t) * 2f - 1f,
            (Mathf.PerlinNoise(noiseSeed + 43.7f, t) * 2f - 1f) * .7f);
        float aimScale = Mathf.Lerp(1f, aimedShakeMultiplier, aimController != null ? aimController.AimAmount : 0f);
        return Quaternion.Euler(Vector3.ClampMagnitude(blastKick + noise * s, 10f) * aimScale);
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }
}
