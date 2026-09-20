using Photon.Pun;
using UnityEngine;

/// <summary>Shows the grenade in the left hand while the throw animation plays and launches
/// the projectile on release. Owner requests, master authorizes, everyone simulates.</summary>
[DisallowMultipleComponent]
public sealed class GrenadeThrower : MonoBehaviourPun
{
    [SerializeField, Min(1f)] private float throwSpeed = 15f;
    [SerializeField, Range(0f, 1f)] private float upwardBias = .35f;
    [SerializeField] private Vector3 handOffset = new Vector3(0f, -.03f, .04f);
    [Header("Stock")]
    [SerializeField, Min(1)] private int maxGrenades = 3;
    [SerializeField, Min(1f)] private float regenSeconds = 30f;
    private GrenadeThrowIK throwAnim;
    private WeaponHandIK hands;
    private PlayerHealth health;
    private Camera view;
    private GameObject handGrenade;
    private bool wasPlaying;
    private int grenades;
    private double nextRegenAt = double.PositiveInfinity;
    private static GameObject grenadeModel;
    private static double Now => PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;
    public int Grenades => grenades;
    public int MaxGrenades => maxGrenades;
    public bool HasGrenades => grenades > 0;
    private bool IsOwner => !PhotonNetwork.InRoom || photonView.IsMine;

    private void Awake()
    {
        grenades = maxGrenades;
        throwAnim = GetComponent<GrenadeThrowIK>();
        hands = GetComponentInChildren<WeaponHandIK>(true);
        health = GetComponent<PlayerHealth>();
        view = GetComponentInChildren<Camera>(true);
    }
    private void OnEnable() { if (throwAnim != null) throwAnim.Released += OnReleased; }
    private void OnDisable()
    {
        if (throwAnim != null) throwAnim.Released -= OnReleased;
        ClearHand();
    }
    private void Update()
    {
        if (IsOwner && grenades < maxGrenades && Now >= nextRegenAt)
        {
            grenades++;
            nextRegenAt = grenades < maxGrenades ? Now + regenSeconds : double.PositiveInfinity;
        }
        if (throwAnim == null) return;
        bool playing = throwAnim.IsPlaying;
        if (playing && !wasPlaying) AttachHand();
        else if (!playing && wasPlaying) ClearHand();
        wasPlaying = playing;
    }
    private static GameObject Model()
    {
        if (grenadeModel == null) grenadeModel = Resources.Load<GameObject>("Grenade/grenade");
        return grenadeModel;
    }
    private void AttachHand()
    {
        ClearHand();
        var model = Model();
        var hand = hands != null ? hands.LeftHand : null;
        if (model == null || hand == null) return;
        handGrenade = Instantiate(model, hand, false);
        handGrenade.transform.localPosition = handOffset;
        handGrenade.transform.localRotation = Quaternion.identity;
        handGrenade.transform.localScale = Vector3.one;
        foreach (var collider in handGrenade.GetComponentsInChildren<Collider>(true)) Destroy(collider);
    }
    private void ClearHand()
    {
        if (handGrenade == null) return;
        Destroy(handGrenade);
        handGrenade = null;
    }
    private void OnReleased()
    {
        Vector3 handPos = handGrenade != null ? handGrenade.transform.position
            : transform.position + Vector3.up * 1.4f;
        ClearHand();
        // The animation also plays on remote viewers; only the owner launches.
        if (!IsOwner || !ConquestMatch.CombatAllowed) return;
        if (TeamSafeZone.BlocksWeapons(health) || TeamSafeZone.ContainsAny(handPos)) return;
        if (health != null && health.IsDead) return;
        if (grenades <= 0) return;
        double now = Now;
        grenades--;
        if (grenades < maxGrenades && double.IsPositiveInfinity(nextRegenAt)) nextRegenAt = now + regenSeconds;
        Vector3 direction = view != null ? view.transform.forward : transform.forward;
        if (direction.sqrMagnitude < .000001f) direction = Vector3.forward;
        direction.Normalize();
        Vector3 velocity = (direction + Vector3.up * upwardBias).normalized * throwSpeed;
        Vector3 start = handPos + direction * .1f;
        if (!BulletHitUtility.IsFinite(start) || !BulletHitUtility.IsFinite(velocity)) return;
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
        {
            int seed = Random.Range(0, int.MaxValue);
            GrenadeProjectile.Launch(start, velocity, now, photonView.Owner, seed);
            if (PhotonNetwork.InRoom)
                photonView.RPC(nameof(BroadcastGrenade), RpcTarget.Others, start, velocity, now, seed, TeamSafeZone.AttackerTeam(photonView.Owner));
            return;
        }
        photonView.RPC(nameof(RequestGrenade), RpcTarget.MasterClient, start, velocity);
    }
    [PunRPC]
    private void RequestGrenade(Vector3 start, Vector3 velocity, PhotonMessageInfo info)
    {
        if (!ConquestMatch.CombatAllowed || !PhotonNetwork.IsMasterClient || info.Sender == null ||
            info.Sender.ActorNumber != photonView.OwnerActorNr) return;
        if (health == null || health.IsDead || TeamSafeZone.BlocksWeapons(health) || TeamSafeZone.ContainsAny(start)) return;
        double now = PhotonNetwork.Time;
        if (!BulletHitUtility.IsFinite(start) || !BulletHitUtility.IsFinite(velocity)) return;
        if (velocity.sqrMagnitude > 25f * 25f) return;
        if (Vector3.Distance(start, transform.position) > 4f) return;
        int seed = Random.Range(0, int.MaxValue);
        GrenadeProjectile.Launch(start, velocity, now, photonView.Owner, seed);
        photonView.RPC(nameof(BroadcastGrenade), RpcTarget.Others, start, velocity, now, seed, TeamSafeZone.AttackerTeam(photonView.Owner));
    }
    [PunRPC]
    private void BroadcastGrenade(Vector3 start, Vector3 velocity, double throwTime, int seed, int sourceTeam, PhotonMessageInfo info)
    {
        if (info.Sender == null || !info.Sender.IsMasterClient) return;
        if (!BulletHitUtility.IsFinite(start) || !BulletHitUtility.IsFinite(velocity)) return;
        GrenadeProjectile.Launch(start, velocity, throwTime, null, seed, sourceTeam);
    }
}
