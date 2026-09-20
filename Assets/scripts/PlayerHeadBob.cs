using Photon.Pun;
using UnityEngine;

[RequireComponent(typeof(PlayerController), typeof(PhotonView))]
public class PlayerHeadBob : MonoBehaviourPun
{
    [SerializeField] private Camera playerCamera;
    [SerializeField, Min(0f)] private float walkFrequency = 10f;
    [SerializeField, Min(0f)] private float sprintFrequency = 15f;
    [SerializeField, Min(0f)] private float walkAmplitude = 0.045f;
    [SerializeField, Min(0f)] private float sprintAmplitude = 0.08f;

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
        float aimAmount = aim != null ? aim.AimAmount : 0f;
        float frequency = playerController.IsSprinting ? sprintFrequency * .8f : walkFrequency;
        // Hip-fire walking was still driven by the faster camera bob, independently
        // of WeaponSway. Keep the existing ADS cadence and blend into slower walking.
        if (!playerController.IsSprinting) frequency *= Mathf.Lerp(.5f, 1f, aimAmount);
        // Camera children include the weapon and FPS arms, so this supplies one
        // shared, subtle bob without pulling the hands away from their grips.
        float amplitude = (playerController.IsSprinting ? sprintAmplitude : walkAmplitude) * .28f;
        amplitude *= Mathf.InverseLerp(.05f, 1f, horizontalVelocity.magnitude);
        if (!playerController.IsGrounded || playerController.IsSliding || (weaponAnimation != null && weaponAnimation.IsPlayingAction)) amplitude = 0;
        amplitude *= Mathf.Lerp(1f, .15f, aimAmount);
        if (playerController.IsCrouching)
        {
            amplitude *= 0.45f;
        }

        currentAmplitude = Mathf.Lerp(currentAmplitude, amplitude, 1f - Mathf.Exp(-12f * Time.deltaTime));
        bobTime += Time.deltaTime * frequency * Mathf.Clamp(horizontalVelocity.magnitude / 4.8f, .4f, 1.5f);
        Vector3 cameraPosition = playerCamera.transform.localPosition;
        cameraPosition.y += Mathf.Sin(bobTime) * currentAmplitude;
        playerCamera.transform.localPosition = cameraPosition;
    }
}
