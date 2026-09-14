using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
[DefaultExecutionOrder(-75)]
public class PlayerController : MonoBehaviourPun
{
    [Header("Movement")]
    [SerializeField, Min(0f)] private float walkSpeed = 3.2f;
    [SerializeField, Min(0f)] private float sprintSpeed = 5.5f;
    [SerializeField, Min(0f)] private float crouchSpeed = 1.7f;
    [SerializeField, Min(0f)] private float groundAcceleration = 18f;
    [SerializeField, Min(0f)] private float groundDeceleration = 24f;
    [SerializeField, Range(0f, 1f)] private float airControl = 0.35f;
    [SerializeField, Range(0f, 1f)] private float strafeSpeedMultiplier = 0.8f;
    [SerializeField, Range(0f, 1f)] private float backwardSpeedMultiplier = 0.7f;

    [Header("Stamina")]
    [SerializeField, Min(1f)] private float maximumStamina = 100f;
    [SerializeField, Min(0f)] private float sprintStaminaDrain = 18f;
    [SerializeField, Min(0f)] private float staminaRecovery = 22f;
    [SerializeField, Range(0f, 1f)] private float exhaustionRecoveryThreshold = 0.2f;

    [Header("Jumping")]
    [SerializeField, Min(0f)] private float jumpHeight = 1.15f;
    [SerializeField] private float gravity = -24f;
    [SerializeField] private float terminalVelocity = -45f;

    [Header("Stance")]
    [SerializeField, Min(0.1f)] private float crouchHeight = 1.15f;
    [SerializeField, Min(0f)] private float stanceTransitionSpeed = 7f;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private WeaponAimController aimController;

    private CharacterController controller;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private float standingHeight;
    private float standingCameraHeight;
    private float stamina;
    private bool isExhausted;

    public bool IsSprinting { get; private set; }
    public bool IsCrouching { get; private set; }
    public bool IsGrounded => controller.isGrounded;
    public float StaminaNormalized => stamina / maximumStamina;

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
        if (!photonView.IsMine) return;

        Keyboard keyboard = Keyboard.current;
        Vector3 input = ReadMovement(keyboard);
        IsCrouching = keyboard?.cKey.isPressed == true;
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

        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, acceleration * Time.deltaTime);
        verticalVelocity = Mathf.Max(verticalVelocity + gravity * Time.deltaTime, terminalVelocity);

        Vector3 velocity = horizontalVelocity + Vector3.up * verticalVelocity;
        controller.Move(velocity * Time.deltaTime);
    }

    private void UpdateStamina()
    {
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
        float targetHeight = IsCrouching ? crouchHeight : standingHeight;
        controller.height = Mathf.MoveTowards(controller.height, targetHeight, stanceTransitionSpeed * Time.deltaTime);
        controller.center = new Vector3(0f, controller.height * 0.5f, 0f);

        if (playerCamera == null)
        {
            return;
        }

        Vector3 cameraPosition = playerCamera.transform.localPosition;
        float targetCameraHeight = IsCrouching ? standingCameraHeight * crouchHeight / standingHeight : standingCameraHeight;
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
