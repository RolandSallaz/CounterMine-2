using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(-75)]
public class PlayerController : MonoBehaviourPun
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float walkSpeed = 4.8f;
    [SerializeField, Min(0f)] private float sprintSpeed = 9.2f;
    [SerializeField, Min(0f)] private float crouchSpeed = 2.6f;
    [SerializeField, Min(0f)] private float groundAcceleration = 42f;
    [SerializeField, Min(0f)] private float groundDeceleration = 48f;
    [SerializeField, Range(0f, 1f)] private float airControl = 0.45f;
    [SerializeField, Range(0f, 1f)] private float strafeSpeedMultiplier = 0.95f;
    [SerializeField, Range(0f, 1f)] private float backwardSpeedMultiplier = 0.82f;

    [Header("Stamina")]
    [SerializeField, Min(1f)] private float maximumStamina = 100f;
    [SerializeField, Min(0f)] private float sprintStaminaDrain = 14f;
    [SerializeField, Min(0f)] private float staminaRecovery = 30f;
    [SerializeField, Range(0f, 1f)] private float exhaustionRecoveryThreshold = 0.2f;

    [Header("Jumping")]
    [SerializeField, Min(0f)] private float jumpHeight = 1.15f;
    [SerializeField] private float gravity = -24f;
    [SerializeField] private float terminalVelocity = -45f;

    [Header("Stance")]
    [SerializeField, Min(0.1f)] private float crouchHeight = 1.15f;
    [SerializeField, Min(0f)] private float stanceTransitionSpeed = 11f;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private WeaponAimController aimController;
    [Header("Slide")]
    [SerializeField, Min(0f)] private float slideMinimumSpeed = 4f;
    [SerializeField, Min(.1f)] private float slideDuration = .95f;
    [SerializeField, Min(0f)] private float slideSpeedBoost = 0.8f;
    [SerializeField, Min(0f)] private float slideFriction = 7.5f;
    [SerializeField, Min(.5f)] private float slideHeight = .85f;
    [SerializeField, Min(0f)] private float slideStaminaCost = 12f;
    [SerializeField, Min(0f)] private float slideCooldown = .8f;
    private float slideRemaining, nextSlideTime;
    private readonly Collider[] headroom = new Collider[32];

    private CharacterController controller;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private float standingHeight;
    private float standingCameraHeight;
    private float stamina;
    private bool isExhausted;

    public bool IsSprinting { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsSliding { get; private set; }
    public float CrouchAmount => Mathf.Clamp01((standingHeight - controller.height) / Mathf.Max(.01f, standingHeight - crouchHeight));
    public bool IsGrounded => controller.isGrounded;
    public float StaminaNormalized => stamina / maximumStamina;
    public float HorizontalSpeed => horizontalVelocity.magnitude;
    public float TopSpeed => sprintSpeed;

    public void ResetMotion()
    {
        horizontalVelocity = Vector3.zero;
        verticalVelocity = 0f;
        IsSprinting = false;
        IsCrouching = false;
        IsSliding = false;
        slideRemaining = 0;
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        standingHeight = controller.height;
        stamina = maximumStamina;

        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
        }

        if (playerCamera != null)
        {
            standingCameraHeight = playerCamera.transform.localPosition.y;
        }
    }

    private void Update()
    {
        if (PhotonNetwork.InRoom && !photonView.IsMine) return;

        Keyboard keyboard = Keyboard.current;
        Vector3 input = ReadMovement(keyboard);
        bool crouchHeld = keyboard?.cKey.isPressed == true || keyboard?.leftCtrlKey.isPressed == true || keyboard?.rightCtrlKey.isPressed == true;
        if (keyboard?.cKey.wasPressedThisFrame == true || keyboard?.leftCtrlKey.wasPressedThisFrame == true || keyboard?.rightCtrlKey.wasPressedThisFrame == true) TryStartSlide();
        if (IsSliding)
        {
            slideRemaining -= Time.deltaTime;
            if (slideRemaining <= 0 || !controller.isGrounded || horizontalVelocity.magnitude < crouchSpeed)
                EndSlide();
        }
        IsCrouching = IsSliding || crouchHeld || !CanStand();
        bool wantsSprint = !IsCrouching && (aimController == null || !aimController.BlocksSprint) &&
            keyboard?.leftShiftKey.isPressed == true && input.z > 0f;
        IsSprinting = wantsSprint && !isExhausted && stamina > 0f;
        UpdateStamina();

        if (isExhausted)
        {
            IsSprinting = false;
        }

        UpdateStance();

        if (controller.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        if (controller.isGrounded && keyboard?.spaceKey.wasPressedThisFrame == true && !IsCrouching)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        Vector3 movement = (transform.right * input.x + transform.forward * input.z).normalized;
        float targetSpeed = IsCrouching ? crouchSpeed : IsSprinting ? sprintSpeed : walkSpeed;
        if (aimController != null && aimController.isActiveAndEnabled)
            targetSpeed *= aimController.MovementSpeedMultiplier;
        targetSpeed *= GetDirectionalSpeedMultiplier(input);
        Vector3 targetVelocity = movement * targetSpeed;
        float acceleration = targetVelocity.sqrMagnitude > 0f ? groundAcceleration : groundDeceleration;

        if (!controller.isGrounded)
        {
            acceleration *= airControl;
        }

        horizontalVelocity = IsSliding
            ? Vector3.MoveTowards(horizontalVelocity, Vector3.zero, slideFriction * Time.deltaTime)
            : Vector3.MoveTowards(horizontalVelocity, targetVelocity, acceleration * Time.deltaTime);
        verticalVelocity = Mathf.Max(verticalVelocity + gravity * Time.deltaTime, terminalVelocity);

        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
        CollisionFlags collisions = controller.Move(velocity * Time.deltaTime);
        if (IsSliding && (collisions & CollisionFlags.Sides) != 0) EndSlide();
    }

    private bool TryStartSlide()
    {
        if (IsSliding || !IsSprinting || !controller.isGrounded || Time.time < nextSlideTime ||
            horizontalVelocity.magnitude < slideMinimumSpeed || stamina < slideStaminaCost) return false;
        IsSliding = true;
        slideRemaining = slideDuration;
        horizontalVelocity += horizontalVelocity.normalized * slideSpeedBoost;
        stamina = Mathf.Max(0, stamina - slideStaminaCost);
        return true;
    }

    private void EndSlide()
    {
        IsSliding = false;
        slideRemaining = 0;
        nextSlideTime = Time.time + slideCooldown;
    }

    private bool CanStand()
    {
        if (controller.height >= standingHeight - .001f) return true;
        float radius = Mathf.Max(.01f, controller.radius - controller.skinWidth);
        Vector3 bottom = transform.position + transform.up * (controller.height - radius);
        Vector3 top = transform.position + transform.up * (standingHeight - radius);
        int count = Physics.OverlapCapsuleNonAlloc(bottom, top, radius, headroom, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++) if (!headroom[i].transform.IsChildOf(transform)) return false;
        return count < headroom.Length;
    }

    private void UpdateStamina()
    {
        if (IsSliding) return;
        if (IsSprinting)
        {
            stamina = Mathf.Max(0f, stamina - sprintStaminaDrain * Time.deltaTime);
            if (stamina <= 0f)
            {
                isExhausted = true;
            }

            return;
        }

        stamina = Mathf.Min(maximumStamina, stamina + staminaRecovery * Time.deltaTime);
        if (isExhausted && stamina >= maximumStamina * exhaustionRecoveryThreshold)
        {
            isExhausted = false;
        }
    }

    private float GetDirectionalSpeedMultiplier(Vector3 input)
    {
        if (input.z < 0f)
        {
            return backwardSpeedMultiplier;
        }

        return Mathf.Abs(input.x) > Mathf.Abs(input.z) ? strafeSpeedMultiplier : 1f;
    }

    private void UpdateStance()
    {
        float targetHeight = IsSliding ? slideHeight : IsCrouching ? crouchHeight : standingHeight;
        targetHeight = Mathf.Max(controller.radius * 2, targetHeight);
        controller.height = Mathf.MoveTowards(controller.height, targetHeight, stanceTransitionSpeed * Time.deltaTime);
        controller.center = new Vector3(0f, controller.height * 0.5f, 0f);

        if (playerCamera == null)
        {
            return;
        }

        Vector3 cameraPosition = playerCamera.transform.localPosition;
        float targetCameraHeight = standingCameraHeight * controller.height / standingHeight;
        cameraPosition.y = Mathf.MoveTowards(cameraPosition.y, targetCameraHeight, stanceTransitionSpeed * Time.deltaTime);
        playerCamera.transform.localPosition = cameraPosition;
    }

    private static Vector3 ReadMovement(Keyboard keyboard)
    {
        if (keyboard == null)
        {
            return Vector3.zero;
        }

        float horizontal = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float vertical = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        return new Vector3(horizontal, 0f, vertical).normalized;
    }
}
