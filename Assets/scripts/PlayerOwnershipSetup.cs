using Photon.Pun;
using UnityEngine;

[RequireComponent(typeof(PhotonView))]
public class PlayerOwnershipSetup : MonoBehaviourPun
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private GameObject thirdPersonModel;

    private void Awake() => ApplyOwnership();
    private void Start()
    {
        ApplyOwnership();
        if (!BotController.IsBot(this) && (!PhotonNetwork.InRoom || photonView.IsMine))
        {
            var prefab = Resources.Load<GameObject>("UI/PlayerHUD");
            if (prefab != null) Instantiate(prefab, transform, false).GetComponent<PlayerHUD>().Bind(GetComponent<PlayerHealth>());
        }
    }

    private void ApplyOwnership()
    {
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
        }

        if (playerCamera != null)
        {
            bool local = !BotController.IsBot(this) && (!PhotonNetwork.InRoom || photonView.IsMine);
            playerCamera.gameObject.SetActive(true);
            playerCamera.enabled = local;
            foreach (var listener in playerCamera.GetComponentsInChildren<AudioListener>(true)) listener.enabled = local;
        }
        if (GetComponent<NetworkWeaponPresentation>() == null) gameObject.AddComponent<NetworkWeaponPresentation>();
        if (GetComponent<PlayerModelPresentation>() == null) gameObject.AddComponent<PlayerModelPresentation>();
        if (GetComponent<PlayerWalkAnimation>() == null) gameObject.AddComponent<PlayerWalkAnimation>();
        if (GetComponent<WeaponAmmo>() == null) gameObject.AddComponent<WeaponAmmo>();
        if (thirdPersonModel != null) thirdPersonModel.SetActive(true);
    }
}
