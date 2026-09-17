using Animancer;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(AnimancerComponent), typeof(CharacterController), typeof(PhotonView))]
public class PlayerAnimancerController : MonoBehaviourPun, IPunObservable
{
    private enum AnimationKind : byte
    {
        Idle,
        Walk,
        Run,
        Fire,
        Crouch,
        CrouchWalk,
        Slide
    }

    [SerializeField] private AnimancerComponent animancer;
    [SerializeField] private AnimationClip idleClip;
    [SerializeField] private AnimationClip walkClip;
    [SerializeField] private AnimationClip runClip;
    [SerializeField] private AnimationClip fireClip;
    [SerializeField] private WeaponRecoilController weaponRecoil;
    [SerializeField, Min(0f)] private float fadeDuration = 0.15f;
    [SerializeField, Min(0f)] private float movementThreshold = 0.1f;

    private CharacterController characterController;
    private PlayerController playerController;
    private AnimationKind currentAnimation;
    private float actionEndTime;
    public bool IsCrouching => currentAnimation == AnimationKind.Crouch || currentAnimation == AnimationKind.CrouchWalk || currentAnimation == AnimationKind.Slide;
    public bool IsSliding => currentAnimation == AnimationKind.Slide;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        playerController = GetComponent<PlayerController>();
        animancer ??= GetComponent<AnimancerComponent>();
    }

    private void Start()
    {
        Play(AnimationKind.Idle);
    }

    private void Update()
    {
        if (PhotonNetwork.InRoom && !photonView.IsMine)
        {
            return;
        }

        if (weaponRecoil == null && Mouse.current?.leftButton.wasPressedThisFrame == true)
        {
            PlayWeaponFire();
            return;
        }

        if (Time.time >= actionEndTime)
        {
            Play(GetLocomotionAnimation());
        }
    }

    [PunRPC]
    private void PlayFire()
    {
        if (fireClip == null)
        {
            return;
        }

        actionEndTime = Time.time + fireClip.length;
        Play(AnimationKind.Fire);
    }

    public void PlayWeaponFire()
    {
        if (!isActiveAndEnabled || !photonView.IsMine) return;
        PlayFire();
        if (PhotonNetwork.InRoom) photonView.RPC(nameof(PlayFire), RpcTarget.Others);
    }

    private AnimationKind GetLocomotionAnimation()
    {
        if (playerController != null && playerController.IsSliding) return AnimationKind.Slide;
        Vector3 horizontalVelocity = characterController.velocity;
        horizontalVelocity.y = 0f;
        if (playerController != null && playerController.IsCrouching)
            return horizontalVelocity.sqrMagnitude > movementThreshold * movementThreshold ? AnimationKind.CrouchWalk : AnimationKind.Crouch;
        if (horizontalVelocity.sqrMagnitude <= movementThreshold * movementThreshold)
        {
            return AnimationKind.Idle;
        }

        return playerController != null && playerController.IsSprinting ? AnimationKind.Run : AnimationKind.Walk;
    }

    private void Play(AnimationKind animation)
    {
        bool unchanged = animation == currentAnimation;
        currentAnimation = animation;
        // The first-person arms/weapon pair has its own synchronized playback.
        // Do not initialize the third-person graph while that model is disabled.
        if (animancer == null || animancer.Animator == null || !animancer.Animator.isActiveAndEnabled)
        {
            return;
        }

        if (unchanged && animancer.IsPlaying())
        {
            return;
        }

        AnimationClip clip = animation switch
        {
            AnimationKind.Walk => walkClip,
            AnimationKind.Run => runClip,
            AnimationKind.Fire => fireClip,
            _ => idleClip
        };

        if (clip == null)
        {
            return;
        }

        animancer.Play(clip, fadeDuration);
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        if (stream.IsWriting)
        {
            stream.SendNext((byte)currentAnimation);
            return;
        }

        AnimationKind receivedAnimation = (AnimationKind)(byte)stream.ReceiveNext();
        if (!photonView.IsMine)
        {
            Play(receivedAnimation);
        }
    }
}
