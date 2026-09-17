using Photon.Pun;
using UnityEngine;

[RequireComponent(typeof(PlayerController), typeof(PhotonView))]
public class PlayerHeadBob : MonoBehaviourPun
{
    [SerializeField] private Camera playerCamera;
    [SerializeField, Min(0f)] private float walkFrequency = 8f;
    [SerializeField, Min(0f)] private float sprintFrequency = 12f;
    [SerializeField, Min(0f)] private float walkAmplitude = 0.035f;
    [SerializeField, Min(0f)] private float sprintAmplitude = 0.055f;

    private PlayerController playerController;
    private CharacterController characterController;
    private float bobTime;
    private float currentAmplitude;
    private WeaponAimController aim;
    private WeaponIdleSynchronizer weaponAnimation;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        characterController = GetComponent<CharacterController>();
        playerCamera ??= GetComponentInChildren<Camera>(true);
        aim = GetComponentInChildren<WeaponAimController>(true);
        weaponAnimation = GetComponentInChildren<WeaponIdleSynchronizer>(true);
    }

    private void LateUpdate()
    {
        if ((PhotonNetwork.InRoom && !photonView.IsMine) || playerCamera == null)
        {
            return;
        }

        Vector3 horizontalVelocity = characterController.velocity;
        horizontalVelocity.y = 0f;
        float frequency = playerController.IsSprinting ? sprintFrequency * .5f : walkFrequency;
        // Camera children include the weapon and FPS arms, so this supplies one
        // shared, subtle bob without pulling the hands away from their grips.
        float amplitude = (playerController.IsSprinting ? sprintAmplitude : walkAmplitude) * .28f;
        amplitude *= Mathf.InverseLerp(.05f, 1f, horizontalVelocity.magnitude);
        if (!playerController.IsGrounded || playerController.IsSliding || (weaponAnimation != null && weaponAnimation.IsPlayingAction)) amplitude = 0;
        amplitude *= Mathf.Lerp(1f, .15f, aim != null ? aim.AimAmount : 0f);
        if (playerController.IsCrouching)
        {
            amplitude *= 0.45f;
        }

        currentAmplitude = Mathf.Lerp(currentAmplitude, amplitude, 1f - Mathf.Exp(-12f * Time.deltaTime));
        bobTime += Time.deltaTime * frequency * Mathf.Clamp(horizontalVelocity.magnitude / 3.2f, .4f, 1.5f);
        Vector3 cameraPosition = playerCamera.transform.localPosition;
        cameraPosition.y += Mathf.Sin(bobTime) * currentAmplitude;
        playerCamera.transform.localPosition = cameraPosition;
    }
}
