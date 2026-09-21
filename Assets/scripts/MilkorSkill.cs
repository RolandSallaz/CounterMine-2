using Photon.Pun;
using UnityEngine;

/// <summary>Six-shot reward launcher. The master authorizes both the reward and every projectile.</summary>
[DisallowMultipleComponent]
public sealed class MilkorSkill : MonoBehaviourPun
{
    public const string WeaponId = "milkor";
    public const float ShotInterval = .55f;
    private WeaponIdleSynchronizer animationSource;
    private WeaponAmmo ammo;
    private PlayerHealth health;
    private Camera view;
    private string previousWeapon = "ak74";
    private double requestUntil, nextLocalShot, nextMasterShot, returnAt;
    private int localSequence, lastRequest, lastPresented;
    private MuzzleFlashEffect flash;
    private int round;
    private static double Now => PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;
    public bool Equipped => animationSource != null && animationSource.WeaponId == WeaponId;
    public int Remaining => ammo != null ? ammo.SavedAmmo(WeaponId) : 0;
    private void Awake()
    {
        animationSource = GetComponentInChildren<WeaponIdleSynchronizer>(true);
        ammo = GetComponent<WeaponAmmo>(); health = GetComponent<PlayerHealth>();
        view = GetComponentInChildren<Camera>(true);
        MilkorRewards.Ensure();
        round = ConquestMatch.Instance?.State?.Round ?? 0;
    }
    public bool TryActivate()
    {
        if (!PhotonNetwork.InRoom || !photonView.IsMine || health == null || health.IsDead ||
            !ConquestMatch.CombatAllowed || animationSource == null || Now < requestUntil) return false;
        if (Equipped && Remaining > 0) return false;
        if (Remaining > 0) return Equip();
        if (MilkorRewards.Read(photonView.OwnerActorNr).Charges <= 0) return false;
        requestUntil = Now + 3;
        photonView.RPC(nameof(RequestActivation), RpcTarget.MasterClient);
        return true;
    }
    [PunRPC]
    private void RequestActivation(PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null || info.Sender.ActorNumber != photonView.OwnerActorNr) return;
        bool accepted = ConquestMatch.CombatAllowed && health != null && !health.IsDead &&
            MilkorRewards.Activate(info.Sender.ActorNumber, photonView.ViewID);
        int rounds = accepted ? MilkorRewards.Read(info.Sender.ActorNumber).Ammo : 0;
        photonView.RPC(nameof(ReceiveActivation), info.Sender, accepted, rounds);
    }
    [PunRPC]
    private void ReceiveActivation(bool accepted, int rounds, PhotonMessageInfo info)
    {
        if (!photonView.IsMine || info.Sender == null || !info.Sender.IsMasterClient) return;
        requestUntil = 0;
        if (!accepted || health == null || health.IsDead) return;
        ammo.SetFiniteAmmo(WeaponId, Mathf.Clamp(rounds, 0, MilkorRewards.Capacity));
        Equip();
    }
    private bool Equip()
    {
        if (!Equipped) previousWeapon = animationSource.WeaponId ?? "ak74";
        returnAt = 0;
        return animationSource.EquipWeapon(WeaponId);
    }
    public bool Fire(Vector3 start, Vector3 direction)
    {
        if (!photonView.IsMine || !Equipped || !animationSource.CanFire || !ammo.CanShoot || Now < nextLocalShot ||
            !ConquestMatch.CombatAllowed || health.IsDead || TeamSafeZone.BlocksWeapons(health)) return false;
        Vector3 eye = view.transform.position;
        if (BulletHitUtility.CastCover(eye, start - eye, Vector3.Distance(start, eye), transform, ~0).didHit) return false;
        nextLocalShot = Now + ShotInterval;
        int sequence = ++localSequence;
        ammo.Consume(); Present(start, direction, sequence);
        photonView.RPC(nameof(RequestMilkorShot), RpcTarget.MasterClient, sequence, eye, start, direction);
        if (ammo.MagAmmo == 0) returnAt = Now + ShotInterval;
        return true;
    }
    [PunRPC]
    private void RequestMilkorShot(int sequence, Vector3 eye, Vector3 start, Vector3 direction, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null || info.Sender.ActorNumber != photonView.OwnerActorNr ||
            sequence <= lastRequest || Now + .03 < nextMasterShot || !ConquestMatch.CombatAllowed || health == null || health.IsDead ||
            TeamSafeZone.BlocksWeapons(health) || !BulletHitUtility.IsFinite(eye) || !BulletHitUtility.IsFinite(start) || !BulletHitUtility.IsFinite(direction) ||
            direction.sqrMagnitude < .9f || direction.sqrMagnitude > 1.1f || Vector3.Distance(start, transform.position) > 3f ||
            Vector3.Distance(eye, transform.position) > 3f || Vector3.Distance(start, eye) > 1.2f ||
            TeamSafeZone.ContainsAny(eye) || TeamSafeZone.ContainsAny(start)) return;
        if (BulletHitUtility.CastCover(eye, start-eye, Vector3.Distance(start, eye), transform, ~0).didHit) return;
        if (!MilkorRewards.SpendRound(info.Sender.ActorNumber, photonView.ViewID)) return;
        lastRequest = sequence; nextMasterShot = Now + ShotInterval;
        photonView.RPC(nameof(ReceiveMilkorShot), RpcTarget.All, sequence, start, direction.normalized, Now, Random.Range(0, int.MaxValue));
    }
    [PunRPC]
    private void ReceiveMilkorShot(int sequence, Vector3 start, Vector3 direction, double time, int seed, PhotonMessageInfo info)
    {
        if (info.Sender == null || !info.Sender.IsMasterClient) return;
        if (!photonView.IsMine) Present(start, direction, sequence);
        GrenadeProjectile.Launch(start, direction * 32f, time, photonView.Owner, seed,
            TeamSafeZone.AttackerTeam(photonView.Owner), true, transform);
    }
    private void Present(Vector3 start, Vector3 direction, int sequence)
    {
        if (sequence <= lastPresented) return;
        lastPresented = sequence;
        animationSource.WeaponRoot?.GetComponent<MilkorMechanism>()?.Shot();
        GameAudio.Shot(GameAudio.Profile(WeaponId), start, sequence, photonView.IsMine);
        if (flash == null)
        {
            var go = new GameObject("Milkor muzzle flash"); go.transform.SetParent(transform, false);
            flash = go.AddComponent<MuzzleFlashEffect>();
        }
        flash.Play(null, start, direction, sequence, .7f, 1.4f, .06f);
    }
    private void Update()
    {
        if (!photonView.IsMine || ammo == null) return;
        int currentRound = ConquestMatch.Instance?.State?.Round ?? 0;
        if (round != currentRound)
        {
            round = currentRound; ammo.SetFiniteAmmo(WeaponId, 0); requestUntil = 0;
            if (Equipped) animationSource.EquipWeapon(previousWeapon);
        }
        if (returnAt > 0 && Now >= returnAt)
        {
            returnAt = 0;
            if (Equipped && !health.IsDead) animationSource.EquipWeapon(previousWeapon);
        }
    }
}
