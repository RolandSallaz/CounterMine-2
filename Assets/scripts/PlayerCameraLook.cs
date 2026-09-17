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

    private WeaponIdleSynchronizer weaponAnimation;
    private float pitch;
    private float lean;

    public Vector2 LookDeltaThisFrame { get; private set; }

    private void Awake()
    {
        weaponAnimation = GetComponentInChildren<WeaponIdleSynchronizer>(true);
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

    private void LateUpdate()
    {
        if (!PhotonNetwork.InRoom || photonView.IsMine) ApplyRotation();
    }

    private void ApplyRotation()
    {
        if (playerCamera != null)
        {
            Quaternion animated = weaponAnimation != null ? weaponAnimation.CameraRotationOffset : Quaternion.identity;
            playerCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, -lean * maximumLeanRoll) * animated;
        }
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }
}
