using UnityEngine;

/// <summary>Moves the common first-person pivot without modifying animated bones.</summary>
[DisallowMultipleComponent]
public sealed class WeaponSway : MonoBehaviour
{
    [SerializeField] private PlayerCameraLook cameraLook;
    [SerializeField] private WeaponAimController aimController;
    [SerializeField, Min(1f)] private float fullSwayTurnSpeed = 240f;
    [SerializeField, Min(0f)] private float maximumPositionOffset = 0.015f;
    [SerializeField] private Vector3 maximumRotation = new Vector3(2f, 3f, 1.5f);
    [SerializeField, Min(0.1f)] private float smoothness = 14f;

    private Vector3 restPosition;
    private Quaternion restRotation;
    private Vector2 sway;
    private bool hasRestPose;
    public Vector3 NeutralLocalPosition => hasRestPose ? restPosition : transform.localPosition;
    public Quaternion NeutralLocalRotation => hasRestPose ? restRotation : transform.localRotation;

    private void Awake()
    {
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        hasRestPose = true;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        Vector2 velocity = cameraLook != null && cameraLook.isActiveAndEnabled && Application.isFocused
            ? cameraLook.LookDeltaThisFrame / dt : Vector2.zero;
        Step(velocity, dt);
    }

    private void Step(Vector2 turnVelocity, float deltaTime)
    {
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
        transform.localPosition = restPosition;
        transform.localRotation = restRotation;
    }
}
