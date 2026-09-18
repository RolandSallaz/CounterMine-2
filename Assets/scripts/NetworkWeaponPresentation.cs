using System;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine.InputSystem;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>Persistent equipped weapon/action plus lightweight presentation poses.</summary>
[DefaultExecutionOrder(200)]
[DisallowMultipleComponent]
public sealed class NetworkWeaponPresentation : MonoBehaviourPunCallbacks, IPunObservable
{
    [SerializeField] private bool debugReloadOnR = true;
    private WeaponIdleSynchronizer animationSource;
    private PlayerRagdollController ragdoll;
    private Transform[] pivots;
    private Vector3[] positions;
    private Quaternion[] rotations;
    private bool receivedPose;
    private Vector3[] smoothPositions;
    private Quaternion[] smoothRotations;
    private string StateKey => "weapon:" + photonView.ViewID;
    private bool IsBot => BotController.IsBot(this);
    private bool IsPoseSender(Player sender) => sender != null && (IsBot ? sender.IsMasterClient : sender.ActorNumber == photonView.OwnerActorNr);

    private void Awake()
    {
        animationSource = GetComponentInChildren<WeaponIdleSynchronizer>(true);
        ragdoll = GetComponent<PlayerRagdollController>();
        pivots = new[] { GetComponentInChildren<Camera>(true)?.transform,
            GetComponentInChildren<WeaponAimController>(true)?.transform,
            GetComponentInChildren<WeaponSway>(true)?.transform,
            GetComponentInChildren<WeaponRecoilController>(true)?.transform };
        positions = new Vector3[pivots.Length]; rotations = new Quaternion[pivots.Length];
        smoothPositions = new Vector3[pivots.Length]; smoothRotations = new Quaternion[pivots.Length];
        photonView.ObservedComponents ??= new System.Collections.Generic.List<Component>();
        if (!photonView.ObservedComponents.Contains(this)) photonView.ObservedComponents.Add(this);
    }

    private void Update()
    {
        if (IsBot || !debugReloadOnR || !(Application.isEditor || Debug.isDebugBuild) ||
            (PhotonNetwork.InRoom && !photonView.IsMine) || !Application.isFocused ||
            Cursor.lockState != CursorLockMode.Locked || animationSource == null || !animationSource.CanFire) return;
        if (Keyboard.current?.rKey.wasPressedThisFrame == true) PlayAction("reload");
    }

    private void Start()
    {
        if (animationSource == null) return;
        animationSource.StateChanged += PublishState;
        if (!PhotonNetwork.InRoom || photonView.IsMine) PublishState();
        else ReadState();
    }

    public bool EquipWeapon(string weaponId) => animationSource != null && animationSource.EquipWeapon(weaponId);
    public bool PlayAction(string actionId) => animationSource != null && animationSource.PlayWeaponAction(actionId);

    public void SendGrenadeThrow(double startedAt)
    {
        if (PhotonNetwork.InRoom && photonView.IsMine)
            photonView.RPC(nameof(GrenadeThrowRPC), RpcTarget.Others, startedAt);
    }

    [PunRPC]
    private void GrenadeThrowRPC(double startedAt, PhotonMessageInfo info)
    {
        if (!IsPoseSender(info.Sender)) return;
        var throwing = GetComponent<GrenadeThrowIK>();
        if (throwing == null) throwing = gameObject.AddComponent<GrenadeThrowIK>();
        throwing.Begin(startedAt);
    }

    public void PrepareForRagdoll()
    {
        // Health may restore death before Start on a late-joining client.
        if (PhotonNetwork.InRoom && !photonView.IsMine) ReadState();
    }

    private void PublishState()
    {
        if (!PhotonNetwork.InRoom || !photonView.IsMine || animationSource.WeaponId == null) return;
        var properties = new Hashtable { [StateKey] = new object[] {
            animationSource.WeaponId, animationSource.ActionId, animationSource.StartedAt, animationSource.PlaybackSpeed } };
        if (IsBot) PhotonNetwork.CurrentRoom.SetCustomProperties(properties);
        else photonView.Owner.SetCustomProperties(properties);
    }

    public override void OnPlayerPropertiesUpdate(Player targetPlayer, Hashtable changedProps)
    {
        if (targetPlayer == photonView.Owner && !photonView.IsMine && changedProps.ContainsKey(StateKey)) ReadState();
    }

    private void ReadState()
    {
        if (animationSource == null || (ragdoll != null && ragdoll.IsRagdoll)) return;
        object snapshot = IsBot ? PhotonNetwork.CurrentRoom?.CustomProperties[StateKey] : photonView.Owner?.CustomProperties[StateKey];
        if (snapshot is object[] state && state.Length == 4 &&
            state[0] is string weapon && state[1] is string action && state[2] is double time && state[3] is float speed)
            if (!animationSource.ApplyNetworkState(weapon, action, time, speed))
                Debug.LogWarning("Unknown or unavailable network weapon/action: " + weapon + "/" + action, this);
    }

    public override void OnRoomPropertiesUpdate(Hashtable changed)
    {
        if (IsBot && !photonView.IsMine && changed.ContainsKey(StateKey)) ReadState();
    }
    public override void OnMasterClientSwitched(Player next)
    {
        if (IsBot) ReadState();
    }

    public void OnPhotonSerializeView(PhotonStream stream, PhotonMessageInfo info)
    {
        for (int i = 0; i < pivots.Length; i++)
        {
            if (stream.IsWriting)
            {
                stream.SendNext(pivots[i] != null ? pivots[i].localPosition : Vector3.zero);
                stream.SendNext(pivots[i] != null ? pivots[i].localRotation : Quaternion.identity);
            }
            else
            {
                var position = (Vector3)stream.ReceiveNext();
                var rotation = (Quaternion)stream.ReceiveNext();
                if (!IsPoseSender(info.Sender)) continue;
                if (!Finite(position.x) || !Finite(position.y) || !Finite(position.z) || position.sqrMagnitude > 100 ||
                    !Finite(rotation.x) || !Finite(rotation.y) || !Finite(rotation.z) || !Finite(rotation.w)) continue;
                positions[i] = position; rotations[i] = rotation.normalized;
                if (!receivedPose) { smoothPositions[i] = positions[i]; smoothRotations[i] = rotations[i]; }
            }
        }
        if (!stream.IsWriting && IsPoseSender(info.Sender)) receivedPose = true;
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    private void LateUpdate()
    {
        if (!PhotonNetwork.InRoom || photonView.IsMine || !receivedPose || (ragdoll != null && ragdoll.IsRagdoll)) return;
        for (int i = 0; i < pivots.Length; i++)
            if (pivots[i] != null)
            {
                float blend = 1f - Mathf.Exp(-20f * Time.deltaTime);
                smoothPositions[i] = Vector3.Lerp(smoothPositions[i], positions[i], blend);
                smoothRotations[i] = Quaternion.Slerp(smoothRotations[i], rotations[i], blend);
                pivots[i].SetLocalPositionAndRotation(smoothPositions[i], smoothRotations[i]);
            }
    }

    private void OnDestroy()
    {
        if (animationSource != null) animationSource.StateChanged -= PublishState;
        if (photonView != null) photonView.ObservedComponents?.Remove(this);
    }
}
