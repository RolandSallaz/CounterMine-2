using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>Master simulates swept ballistic segments; clients predict visuals from launch data.</summary>
[RequireComponent(typeof(PhotonView))]
[DisallowMultipleComponent]
public sealed class NetworkWeapon : MonoBehaviourPunCallbacks
{
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Transform muzzle;
    [SerializeField] private WeaponRecoilController recoil;
    [SerializeField] private PlayerHealth health;
    [SerializeField] private WeaponIdleSynchronizer weaponAnimation;
    [SerializeField] private WeaponAmmo ammo;
    [SerializeField] private Material tracerMaterial;
    [SerializeField] private LayerMask hitMask = ~0;
    [SerializeField, Min(1f)] private float range = 200f;
    [SerializeField, Min(1)] private int damage = 34;
    [SerializeField, Min(1f), InspectorName("Bullet Speed (m/s)")] private float tracerSpeed = 180f;
    [SerializeField, Min(.001f)] private float tracerWidth = .008f;
    [SerializeField, Min(.01f)] private float tracerLength = .45f;
    [SerializeField, Min(.001f)] private float bulletSize = .012f;
    [SerializeField, Min(0f)] private float gravity = 9.81f;
    [SerializeField, Range(0f, .4f)] private float maximumRewind = .25f;
    [SerializeField] private bool friendlyFire;
    [Header("Muzzle flash")]
    [SerializeField] private bool muzzleFlashEnabled = true;
    [SerializeField, Range(.1f, 3f)] private float muzzleFlashScale = 1.8f;
    [SerializeField, Range(0f, 4f)] private float muzzleFlashBrightness = 1f;
    [SerializeField, Range(.015f, .1f)] private float muzzleFlashDuration = .055f;
    [SerializeField, Min(1f)] private float impactLifetime = 15f;
    private MuzzleFlashEffect flashEffect;
    private int nextSequence;
    private int lastAcceptedSequence;
    private int lastConfirmedSequence;
    private double tokenTime;
    private float tokens = 2f;
    private readonly Dictionary<int, BulletTrail> predictions = new Dictionary<int, BulletTrail>();
    private readonly Queue<int> predictionOrder = new Queue<int>();
    private int lastLocalShotFrame = -1;
    private PlayerController movement;
    private WeaponSway weaponSway;
    private sealed class Flight
    {
        public Vector3 position, velocity;
        public double time;
        public float distance, age;
        public BulletTrail visual;
    }
    private readonly Dictionary<int, Flight> flights = new Dictionary<int, Flight>();
    private readonly List<int> flightKeys = new List<int>();
    private static double Now => PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;
    private long ShotKey(int sequence) => ((long)photonView.ViewID << 32) | (uint)sequence;

    public void SetMuzzle(Transform nextMuzzle) => muzzle = nextMuzzle;

    public bool FireBotShot(Vector3 eye, Vector3 direction, float spreadDegrees)
    {
        if (!BotController.IsBot(this) || (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) ||
            muzzle == null || health == null || health.IsDead || weaponAnimation == null || !weaponAnimation.CanFire) return false;
        if (ammo != null && !ammo.CanShoot) { ammo.HandleDryFire(); return false; }
        nextSequence = Mathf.Max(nextSequence, lastAcceptedSequence, lastConfirmedSequence) + 1;
        Vector3 deviated = Deviate(direction, spreadDegrees);
        if (!ProcessShot(null, nextSequence, Now, eye, deviated, muzzle.position)) return false;
        ammo?.Consume(); PlayMuzzleFlash(muzzle.position, deviated, nextSequence);
        return true;
    }

    private void Awake()
    {
        ammo ??= GetComponentInParent<WeaponAmmo>();
        movement = GetComponent<PlayerController>();
        weaponSway = GetComponentInChildren<WeaponSway>(true);
    }

    /// <summary>Perturbs the aim direction inside a spread cone. Master simulates from this direction, so visuals and damage stay consistent.</summary>
    private Vector3 ApplySpread(Vector3 direction)
    {
        float spread = recoil != null ? recoil.CurrentSpreadDegrees : 0f;
        if (spread <= 0f || playerCamera == null) return direction;
        return Deviate(direction, playerCamera.transform.right, playerCamera.transform.up, spread);
    }

    private static Vector3 Deviate(Vector3 direction, float spreadDegrees)
    {
        Vector3 right = Vector3.Cross(direction, Vector3.up);
        if (right.sqrMagnitude < .000001f) right = Vector3.Cross(direction, Vector3.forward);
        right.Normalize();
        return Deviate(direction, right, Vector3.Cross(right, direction).normalized, spreadDegrees);
    }

    private static Vector3 Deviate(Vector3 direction, Vector3 right, Vector3 up, float spreadDegrees)
    {
        if (spreadDegrees <= 0f) return direction;
        float radius = Mathf.Tan(spreadDegrees * Mathf.Deg2Rad);
        Vector2 disc = Random.insideUnitCircle * radius;
        Vector3 deviated = direction + right * disc.x + up * disc.y;
        return deviated.sqrMagnitude > .000001f ? deviated.normalized : direction;
    }

    public bool FireLocalShot()
    {
        if (weaponAnimation != null && !weaponAnimation.CanFire) return false;
        if ((movement != null && movement.IsSprinting) || (weaponSway != null && weaponSway.SprintAmount > .05f)) return false;
        if (ammo != null && !ammo.CanShoot) { ammo.HandleDryFire(); return false; }
        if (!photonView.IsMine || (PhotonNetwork.InRoom && photonView.OwnerActorNr != PhotonNetwork.LocalPlayer.ActorNumber) ||
            playerCamera == null || muzzle == null || (health != null && health.IsDead) || lastLocalShotFrame == Time.frameCount) return false;
        lastLocalShotFrame = Time.frameCount;
        int sequence = ++nextSequence;
        double now = PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;
        Vector3 eye = playerCamera.transform.position;
        Vector3 direction = ApplySpread(playerCamera.transform.forward);
        Vector3 start = muzzle.position;
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
        {
            if (!ProcessShot(photonView.Owner, sequence, now, eye, direction, start)) return false;
            ammo?.Consume();
            PlayMuzzleFlash(start, direction, sequence);
            return true;
        }
        var predicted = ResolveHit(eye, direction, start, now);
        predictions[sequence] = SpawnVisual(start, (predicted.point - start).normalized * tracerSpeed, sequence, 0f);
        PlayMuzzleFlash(start, direction, sequence);
        predictionOrder.Enqueue(sequence);
        while (predictionOrder.Count > 64) predictions.Remove(predictionOrder.Dequeue());
        ammo?.Consume();
        photonView.RPC(nameof(RequestShot), RpcTarget.MasterClient, sequence, now, eye, direction, start);
        return true;
    }

    [PunRPC]
    private void RequestShot(int sequence, double time, Vector3 eye, Vector3 direction, Vector3 start, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null || info.Sender.ActorNumber != photonView.OwnerActorNr) return;
        ProcessShot(info.Sender, sequence, time, eye, direction, start);
    }

    private bool AcceptShot(int sequence, double time, double now, Vector3 eye, Vector3 direction, Vector3 start)
    {
        if (sequence <= Mathf.Max(lastAcceptedSequence, lastConfirmedSequence) || sequence <= 0 ||
            double.IsNaN(time) || double.IsInfinity(time) || time < now - .75 || time > now + .1 ||
            !BulletHitUtility.IsFinite(eye) || !BulletHitUtility.IsFinite(direction) || !BulletHitUtility.IsFinite(start) ||
            direction.sqrMagnitude < .9f || direction.sqrMagnitude > 1.1f ||
            Vector3.Distance(eye, transform.position) > 3f || Vector3.Distance(start, eye) > 1f ||
            (health != null && health.IsDead)) return false;
        float rate = recoil != null ? recoil.RoundsPerMinute : 600f;
        tokens = Mathf.Min(2f, tokens + (float)System.Math.Max(0, now - tokenTime) * rate / 60f);
        tokenTime = now;
        if (tokens < .99f) return false;
        tokens = Mathf.Max(0f, tokens - 1f);
        lastAcceptedSequence = sequence;
        return true;
    }

    private bool ProcessShot(Player sender, int sequence, double time, Vector3 eye, Vector3 direction, Vector3 start)
    {
        double now = PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;
        if (!AcceptShot(sequence, time, now, eye, direction, start)) return false;
        double rewindTime = System.Math.Max(now - maximumRewind, System.Math.Min(now, time));
        // Aim convergence only determines launch direction. Damage is deferred until a swept flight segment hits.
        var aim = ResolveHit(eye, direction, start, rewindTime);
        Vector3 velocity = (aim.point - start).normalized * tracerSpeed;
        Vector3 toMuzzle = start - eye;
        var obstruction = toMuzzle.sqrMagnitude > .000001f
            ? BulletHitUtility.Cast(eye, toMuzzle.normalized, toMuzzle.magnitude, transform, rewindTime, hitMask)
            : default;
        if (obstruction.didHit) { start = eye; velocity = toMuzzle.normalized * tracerSpeed; }
        PresentConfirmedShot(sequence, rewindTime, start, velocity);
        if (PhotonNetwork.InRoom)
            photonView.RPC(nameof(ConfirmShot), RpcTarget.Others, sequence, rewindTime, start, velocity);
        return true;
    }

    private static bool SameTeam(Player a, Player b) => a != null && b != null &&
        a.CustomProperties["team"] is int teamA && b.CustomProperties["team"] is int teamB && teamA == teamB;

    private BulletHitUtility.Hit ResolveHit(Vector3 eye, Vector3 direction, Vector3 start, double time)
    {
        Vector3 toMuzzle = start - eye;
        if (toMuzzle.sqrMagnitude > .000001f)
        {
            var obstruction = BulletHitUtility.Cast(eye, toMuzzle.normalized, toMuzzle.magnitude, transform, time, hitMask);
            if (obstruction.didHit) return obstruction;
        }
        var aimHit = BulletHitUtility.Cast(eye, direction, range, transform, time, hitMask);
        Vector3 shotDirection = aimHit.point - start;
        if (shotDirection.sqrMagnitude < .000001f) return aimHit;
        return BulletHitUtility.Cast(start, shotDirection.normalized, Mathf.Min(range, shotDirection.magnitude + .01f), transform, time, hitMask);
    }

    [PunRPC]
    private void ConfirmShot(int sequence, double time, Vector3 start, Vector3 velocity, PhotonMessageInfo info)
    {
        if (info.Sender == null || !info.Sender.IsMasterClient) return;
        PresentConfirmedShot(sequence, time, start, velocity);
    }
    private void PresentConfirmedShot(int sequence, double time, Vector3 start, Vector3 velocity)
    {
        if (sequence <= lastConfirmedSequence) return;
        lastConfirmedSequence = sequence;
        // Owners already displayed the flash at input time, including the master.
        if (!photonView.IsMine) PlayMuzzleFlash(start, velocity, sequence);
        float age = (float)System.Math.Max(0, Now - time);
        if (predictions.TryGetValue(sequence, out var tracer))
        {
            if (tracer != null) tracer.ConfirmLaunch(start, velocity, age, ShotKey(sequence));
            predictions.Remove(sequence);
        }
        else if (!photonView.IsMine || !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
            tracer = SpawnVisual(start, velocity, sequence, age);
        flights[sequence] = new Flight { position = start, velocity = velocity, time = time, visual = tracer };
    }
    private BulletTrail SpawnVisual(Vector3 start, Vector3 velocity, int sequence, float age) =>
        BulletTrail.Spawn(start, velocity, gravity, range, tracerMaterial, tracerLength, tracerWidth, bulletSize, ShotKey(sequence), age);

    private void PlayMuzzleFlash(Vector3 start, Vector3 direction, int sequence)
    {
        if (!muzzleFlashEnabled) return;
        if (flashEffect == null)
        {
            var effect = new GameObject("Muzzle Flash");
            effect.transform.SetParent(transform, false);
            flashEffect = effect.AddComponent<MuzzleFlashEffect>();
        }
        flashEffect.Play(muzzle, start, direction, sequence,
            muzzleFlashScale, muzzleFlashBrightness, muzzleFlashDuration);
    }

    private void Update()
    {
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) return;
        Simulate(Now);
    }
    private void Simulate(double now)
    {
        flightKeys.Clear(); flightKeys.AddRange(flights.Keys);
        foreach (int sequence in flightKeys)
        {
            if (!flights.TryGetValue(sequence, out var flight)) continue;
            // Small swept segments prevent tunnelling through thin cover at high speed.
            while (flight.time < now)
            {
                float dt = (float)System.Math.Min(1d / 120d, now - flight.time);
                Vector3 next = BulletHitUtility.FlightPosition(flight.position, flight.velocity, gravity, dt);
                Vector3 segment = next - flight.position;
                float distance = Mathf.Min(segment.magnitude, Mathf.Max(0, range - flight.distance));
                var hit = BulletHitUtility.Cast(flight.position, segment.normalized, distance, transform, flight.time + dt, hitMask);
                flight.velocity += Vector3.down * (gravity * dt);
                flight.time += dt; flight.age += dt; flight.distance += distance;
                flight.position = hit.point;
                if (!hit.didHit && flight.distance < range && flight.age < 10f) continue;
                if (hit.player != null && (friendlyFire || health == null || BotController.TeamOf(health) == 0 || BotController.TeamOf(health) != BotController.TeamOf(hit.player)))
                {
                    int finalDamage = Mathf.Max(1, Mathf.RoundToInt(damage * hit.damageMultiplier));
                    hit.player.ApplyMasterDamage(finalDamage, flight.velocity.normalized * 4f, hit.point, photonView.Owner, BotController.IsBot(this) ? photonView.ViewID : 0);
                    ReportDamageNumber(photonView.Owner, finalDamage, hit.point, hit.damageMultiplier > 1.01f);
                }
                bool environmentHit = hit.didHit && hit.player == null;
                PresentImpact(sequence, hit.point, hit.normal, environmentHit);
                if (PhotonNetwork.InRoom) photonView.RPC(nameof(ConfirmImpact), RpcTarget.Others, sequence, hit.point, hit.normal, environmentHit);
                break;
            }
        }
    }
    [PunRPC]
    private void ConfirmImpact(int sequence, Vector3 point, Vector3 normal, bool environmentHit, PhotonMessageInfo info)
    {
        if (info.Sender != null && info.Sender.IsMasterClient) PresentImpact(sequence, point, normal, environmentHit);
    }
    /// <summary>Damage feedback goes only to the local shooter: bots' hits never spawn numbers.</summary>
    private void ReportDamageNumber(Player shooter, int amount, Vector3 point, bool crit)
    {
        if (!BulletHitUtility.IsFinite(point)) return;
        amount = Mathf.Clamp(amount, 1, 500);
        if (!PhotonNetwork.InRoom)
        {
            DamageNumber.Spawn(point, amount, crit);
            return;
        }
        if (shooter != null && shooter.IsLocal)
        {
            DamageNumber.Spawn(point, amount, crit);
            return;
        }
        if (shooter != null) photonView.RPC(nameof(ReceiveDamageNumber), shooter, amount, point, crit);
    }
    [PunRPC]
    private void ReceiveDamageNumber(int amount, Vector3 point, bool crit, PhotonMessageInfo info)
    {
        if (info.Sender == null || !info.Sender.IsMasterClient) return;
        if (!BulletHitUtility.IsFinite(point)) return;
        DamageNumber.Spawn(point, Mathf.Clamp(amount, 1, 500), crit);
    }
    private void PresentImpact(int sequence, Vector3 point, Vector3 normal, bool environmentHit)
    {
        if (!flights.TryGetValue(sequence, out var flight)) return;
        if (flight.visual != null) flight.visual.Impact(point, ShotKey(sequence));
        if (environmentHit && BulletHitUtility.IsFinite(point) && BulletHitUtility.IsFinite(normal)) BulletImpactEffect.Spawn(point, normal, sequence, impactLifetime);
        flights.Remove(sequence);
    }
    public override void OnMasterClientSwitched(Player newMasterClient)
    {
        lastAcceptedSequence = Mathf.Max(lastAcceptedSequence, lastConfirmedSequence);
        tokens = 2f;
        tokenTime = PhotonNetwork.Time;
        if (newMasterClient != PhotonNetwork.LocalPlayer) return;
        foreach (var flight in flights.Values)
        {
            float dt = (float)System.Math.Max(0, Now - flight.time);
            Vector3 displacement = flight.velocity * dt + Vector3.down * (.5f * gravity * dt * dt);
            flight.position += displacement; flight.distance += displacement.magnitude;
            flight.velocity += Vector3.down * (gravity * dt);
            flight.age += dt; flight.time = Now;
        }
    }
}
