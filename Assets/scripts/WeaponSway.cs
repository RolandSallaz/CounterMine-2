using UnityEngine;
using Photon.Pun;

/// <summary>Moves the common first-person pivot without modifying animated bones.</summary>
[DisallowMultipleComponent]
public sealed class WeaponSway : MonoBehaviour
{
    [SerializeField] private PlayerCameraLook cameraLook;
    [SerializeField] private WeaponAimController aimController;
    [SerializeField, Min(1f)] private float fullSwayTurnSpeed = 240f;
    [SerializeField, Min(0f)] private float maximumPositionOffset = 0.02f;
    [SerializeField] private Vector3 maximumRotation = new Vector3(2f, 3f, 1.5f);
    [SerializeField, Min(0.1f)] private float smoothness = 18f;
    [Header("Walking")]
    [SerializeField] private Vector3 walkPosition = new Vector3(.007f, .01f, .004f);
    [SerializeField] private Vector3 walkRotation = new Vector3(.7f, .45f, .8f);
    [SerializeField, Min(.1f)] private float walkStrideLength = 4.3f;
    [SerializeField, Min(.1f)] private float aimedWalkStrideLength = 34.4f;

    private Vector3 restPosition;
    private Quaternion restRotation;
    private Vector2 sway;
    private bool hasRestPose;
    private PlayerController movement;
    private PlayerRagdollController ragdoll;
    private PlayerDeathController death;
    private WeaponIdleSynchronizer animationSource;
    private PhotonView owner;
    private float sprintPhase;
    private float walkPhase, walkAmount;
    public float SprintAmount { get; private set; }
    public Vector3 NeutralLocalPosition => hasRestPose ? restPosition : transform.localPosition;
    public Quaternion NeutralLocalRotation => hasRestPose ? restRotation : transform.localRotation;

    private void Awake()
    {
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        hasRestPose = true;
        movement = GetComponentInParent<PlayerController>();
        ragdoll = GetComponentInParent<PlayerRagdollController>();
        death = GetComponentInParent<PlayerDeathController>();
        owner = GetComponentInParent<PhotonView>();
        animationSource = GetComponentInParent<PlayerController>()?.GetComponentInChildren<WeaponIdleSynchronizer>(true);
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        // Remote clients receive this entire pivot through NetworkWeaponPresentation.
        if (PhotonNetwork.InRoom && owner != null && !owner.IsMine) return;
        Vector2 velocity = cameraLook != null && cameraLook.isActiveAndEnabled && Application.isFocused
            ? cameraLook.LookDeltaThisFrame / dt : Vector2.zero;
        Step(velocity, dt);
        if (movement != null && movement.isActiveAndEnabled)
            StepWalk(movement.HorizontalSpeed, movement.IsGrounded, movement.IsCrouching,
                movement.IsSprinting, movement.IsSliding, dt);
        ApplySprint(dt);
    }

    private void StepWalk(float speed, bool grounded, bool crouching, bool running, bool sliding, float dt)
    {
        if ((animationSource != null && !animationSource.IsIdlePlaying) ||
            (ragdoll != null && ragdoll.IsRagdoll) || (death != null && death.IsDead))
        { walkAmount = 0; return; }
        float target = grounded && !running && !sliding ? Mathf.Clamp01(speed / 4.8f) : 0;
        walkAmount = Mathf.Lerp(walkAmount, target, 1f - Mathf.Exp(-12f * dt));
        float aim = aimController != null ? aimController.AimAmount : 0;
        float stride = Mathf.Lerp(walkStrideLength, aimedWalkStrideLength, aim);
        if (grounded && speed > .05f)
            walkPhase = Mathf.Repeat(walkPhase + speed * dt / Mathf.Max(.1f, stride), 1f);
        float phase = walkPhase * Mathf.PI * 2f;
        float amount = walkAmount * (crouching ? .5f : 1f) * Mathf.Lerp(1f, .12f, aim);
        // The grip targets follow this pivot; hand IK runs after it for both arms.
        transform.localPosition += Vector3.Scale(walkPosition,
            new Vector3(Mathf.Sin(phase), Mathf.Cos(phase * 2), Mathf.Sin(phase * 2))) * amount;
        transform.localRotation *= Quaternion.Euler(Vector3.Scale(walkRotation,
            new Vector3(Mathf.Cos(phase * 2), Mathf.Sin(phase), -Mathf.Sin(phase))) * amount);
    }

    private void ApplySprint(float dt)
    {
        bool action = animationSource == null || !animationSource.IsIdlePlaying;
        bool disabled = (ragdoll != null && ragdoll.IsRagdoll) || (death != null && death.IsDead);
        var profile = aimController != null ? aimController.CurrentWeapon?.Handling : null;
        bool running = !disabled && !action && movement != null && movement.isActiveAndEnabled &&
            movement.IsSprinting && movement.HorizontalSpeed > .2f &&
            (aimController == null || !aimController.BlocksSprint);
        // Authored actions have IK disabled: remove the carry offset immediately so
        // the animated gun and hands stay in their shared authored coordinate space.
        if (action || disabled) { SprintAmount = 0; return; }
        float ergonomics = aimController != null ? aimController.ErgonomicsNormalized : .55f;
        float duration = (profile != null ? profile.sprintBlendTime : .22f) * Mathf.Lerp(1.25f, .8f, ergonomics);
        SprintAmount = Mathf.MoveTowards(SprintAmount, running ? 1f : 0f, dt / Mathf.Max(.01f, duration));
        if (SprintAmount <= 0) return;
        float blend = Mathf.SmoothStep(0, 1, SprintAmount);
        bool grounded = movement != null && movement.IsGrounded;
        float speed = movement != null ? movement.HorizontalSpeed : 0;
        sprintPhase = Mathf.Repeat(sprintPhase + speed * dt / 2.15f * (profile != null ? profile.sprintBobFrequency : .5f), 1f);
        float wave = sprintPhase * Mathf.PI * 2f;
        float bob = (profile != null ? profile.sprintBob : .012f) * (grounded ? Mathf.Clamp01(speed / 5.5f) : 0f);
        Vector3 offset = (profile != null ? profile.sprintPosition : new Vector3(-.035f, -.015f, -.035f)) * blend;
        offset += new Vector3(Mathf.Sin(wave) * bob, Mathf.Cos(wave * 2) * bob * .7f, 0) * blend;
        Vector3 euler = profile != null ? profile.sprintRotation : new Vector3(10, -32, -18);
        euler += new Vector3(Mathf.Cos(wave * 2), Mathf.Sin(wave), Mathf.Sin(wave) * 2) * (bob / .012f);
        Quaternion rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(euler), blend);
        var grip = animationSource.RightGrip;
        Vector3 anchor = grip != null && grip.IsChildOf(transform) ? transform.InverseTransformPoint(grip.position) : Vector3.zero;
        // Rotate around the trigger hand, not the camera; both grip targets move
        // with the weapon and are solved by both FPS/TPS hand IK at order 300.
        Quaternion before = transform.localRotation;
        transform.localPosition += offset + before * (anchor - rotation * anchor);
        transform.localRotation = before * rotation;
    }

    private void Step(Vector2 turnVelocity, float deltaTime)
    {
        // Authored actions animate hands and weapon in the same space without hand IK.
        // Even a fading procedural offset would pull the weapon away from the hands.
        if (animationSource != null && animationSource.IsPlayingAction)
        {
            sway = Vector2.zero;
            transform.localPosition = restPosition;
            transform.localRotation = restRotation;
            return;
        }
        Vector2 target = Vector2.ClampMagnitude(turnVelocity / Mathf.Max(1f, fullSwayTurnSpeed), 1f);
        sway = Vector2.Lerp(sway, target, 1f - Mathf.Exp(-smoothness * deltaTime));
        float amount = aimController != null && aimController.isActiveAndEnabled ? aimController.SwayMultiplier : 1f;
        transform.localPosition = restPosition + new Vector3(-sway.x, -sway.y, 0f) * (maximumPositionOffset * amount);
        transform.localRotation = restRotation * Quaternion.Euler(
            sway.y * maximumRotation.x * amount, -sway.x * maximumRotation.y * amount, sway.x * maximumRotation.z * amount);
    }

    private void OnDisable()
    {
        sway = Vector2.zero;
        SprintAmount = 0;
        sprintPhase = 0;
        walkAmount = walkPhase = 0;
        transform.localPosition = restPosition;
        transform.localRotation = restRotation;
    }
}
