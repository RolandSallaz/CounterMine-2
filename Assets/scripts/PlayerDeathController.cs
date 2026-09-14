using Photon.Pun;
using RootMotion.Dynamics;
using UnityEngine;

[RequireComponent(typeof(PhotonView))]
public class PlayerDeathController : MonoBehaviourPun
{
    [SerializeField] private CharacterController characterController;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerCameraLook cameraLook;
    [SerializeField] private PlayerHeadBob headBob;
    [SerializeField] private PlayerLeanController leanController;
    [SerializeField] private PlayerAnimancerController animationController;
    [SerializeField] private PuppetMaster puppetMaster;
    [SerializeField] private Rigidbody ragdollImpactBody;
    [SerializeField] private Transform ragdollCameraAnchor;

    public bool IsDead { get; private set; }
    public Transform RagdollCameraAnchor => ragdollCameraAnchor;

    public void Die(Vector3 impactForce, Vector3 impactPoint)
    {
        if (!photonView.IsMine || IsDead)
        {
            return;
        }

        photonView.RPC(nameof(DieNetworked), RpcTarget.All, impactForce, impactPoint);
    }

    [PunRPC]
    private void DieNetworked(Vector3 impactForce, Vector3 impactPoint, PhotonMessageInfo info)
    {
        if (info.Sender == null || (!info.Sender.IsMasterClient && info.Sender.ActorNumber != photonView.OwnerActorNr)) return;
        ApplyNetworkDeath(impactForce, impactPoint);
    }

    public void ApplyNetworkDeath(Vector3 impactForce, Vector3 impactPoint)
    {
        if (IsDead)
        {
            return;
        }

        IsDead = true;
        characterController.enabled = false;
        playerController.enabled = false;
        cameraLook.enabled = false;
        headBob.enabled = false;
        leanController.enabled = false;
        animationController.enabled = false;

        if (puppetMaster == null)
        {
            return;
        }

        puppetMaster.Kill();

        if (ragdollImpactBody != null)
        {
            ragdollImpactBody.AddForceAtPosition(impactForce, impactPoint, ForceMode.Impulse);
        }
    }
}
