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

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        characterController = GetComponent<CharacterController>();
        playerCamera ??= GetComponentInChildren<Camera>(true);
    }

    private void LateUpdate()
    {
        if (!photonView.IsMine || playerCamera == null)
        {
            return;
        }

        Vector3 horizontalVelocity = characterController.velocity;
        horizontalVelocity.y = 0f;
        if (!playerController.IsGrounded || horizontalVelocity.sqrMagnitude < 0.01f)
        {
            bobTime = 0f;
            return;
        }

        float frequency = playerController.IsSprinting ? sprintFrequency : walkFrequency;
        float amplitude = playerController.IsSprinting ? sprintAmplitude : walkAmplitude;
        if (playerController.IsCrouching)
        {
            amplitude *= 0.45f;
        }

        bobTime += Time.deltaTime * frequency;
        Vector3 cameraPosition = playerCamera.transform.localPosition;
        cameraPosition.y += Mathf.Sin(bobTime) * amplitude;
        playerCamera.transform.localPosition = cameraPosition;
    }
}
