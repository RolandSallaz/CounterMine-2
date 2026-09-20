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
            RadarSkill.Bind(GetComponent<PlayerHealth>(), playerCamera);
            var hud = GetComponentInChildren<PlayerHUD>(true);
            if (hud == null)
            {
                var prefab = Resources.Load<GameObject>("UI/PlayerHUD");
                if (prefab != null) hud = Instantiate(prefab, transform, false).GetComponent<PlayerHUD>();
            }
            if (hud != null)
            {
                hud.gameObject.SetActive(true);
                hud.Bind(GetComponent<PlayerHealth>());
                if (hud.GetComponent<ConquestHUD>() == null) hud.gameObject.AddComponent<ConquestHUD>();
            }
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
            if (local && playerCamera.GetComponentInChildren<AudioListener>(true) == null) playerCamera.gameObject.AddComponent<AudioListener>();
            foreach (var listener in playerCamera.GetComponentsInChildren<AudioListener>(true)) listener.enabled = local;
        }
        if (GetComponent<NetworkWeaponPresentation>() == null) gameObject.AddComponent<NetworkWeaponPresentation>();
        if (GetComponent<PlayerModelPresentation>() == null) gameObject.AddComponent<PlayerModelPresentation>();
        if (GetComponent<PlayerWalkAnimation>() == null) gameObject.AddComponent<PlayerWalkAnimation>();
        if (GetComponent<PlayerAudio>() == null) gameObject.AddComponent<PlayerAudio>();
        if (GetComponent<WeaponAmmo>() == null) gameObject.AddComponent<WeaponAmmo>();
        if (GetComponent<GrenadeThrowIK>() == null) gameObject.AddComponent<GrenadeThrowIK>();
        if (GetComponent<GrenadeThrower>() == null) gameObject.AddComponent<GrenadeThrower>();
        if (thirdPersonModel != null) thirdPersonModel.SetActive(true);
    }
}
