using Photon.Pun;
using UnityEngine;

[RequireComponent(typeof(PhotonView))]
public class PlayerOwnershipSetup : MonoBehaviourPun
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private GameObject thirdPersonModel;

    private void Awake()
    {
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
        }

        if (playerCamera != null)
        {
            playerCamera.gameObject.SetActive(photonView.IsMine);
        }
        if (thirdPersonModel != null) thirdPersonModel.SetActive(!photonView.IsMine);
    }
}
