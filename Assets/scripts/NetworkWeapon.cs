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
    [SerializeField, Min(1f)] private float fullDamageRange = 60f;
    [SerializeField, Range(.05f, 1f)] private float minimumDamageFraction = .5f;
    [SerializeField, Min(1f), InspectorName("Bullet Speed (m/s)")] private float tracerSpeed = 900f;
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
    private int lastSoundSequence;
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
        public float distance, age, gravity, range, fullDamageRange;
        public int minDamage;
        public BulletTrail visual;
        public string weaponId;
        public int damage;
    }
    private readonly Dictionary<int, Flight> flights = new Dictionary<int, Flight>();
    private readonly List<int> flightKeys = new List<int>();
    private static double Now => PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode ? PhotonNetwork.Time : Time.timeAsDouble;
    private long ShotKey(int sequence) => ((long)photonView.ViewID << 32) | (uint)sequence;

    // Reserve sixteen projectile IDs per trigger pull; ammo, recoil and audio remain per shot.
    public static int ProjectileId(int sequence, int pellet) => unchecked(sequence * 16 + pellet);
    private WeaponIdleSynchronizer.WeaponEntry ShotEntry(string id) => weaponAnimation != null ? weaponAnimation.FindPreviewWeapon(id) : null;
    public static Vector3 PelletVelocity(Vector3 velocity, int sequence, int index, int count, float degrees)
    {
        if (count <= 1 || degrees <= 0 || velocity.sqrMagnitude < .000001f) return velocity;
        Vector3 forward = velocity.normalized;
        Vector3 right = Vector3.Cross(forward, Mathf.Abs(forward.y) > .99f ? Vector3.forward : Vector3.up).normalized;
        Vector3 up = Vector3.Cross(right, forward);
        float angle = index * 2.39996323f + (sequence % 997) * 1.618034f;
        float radius = Mathf.Sqrt((index + .5f) / count) * Mathf.Tan(degrees * Mathf.Deg2Rad);
        return (forward + radius * (Mathf.Cos(angle) * right + Mathf.Sin(angle) * up)).normalized * velocity.magnitude;
    }

    private float maximumMuzzleReach = 1f;
    public void SetMuzzle(Transform nextMuzzle, float reach = 1f)
    { muzzle = nextMuzzle; maximumMuzzleReach = Mathf.Clamp(reach, 1f, 1.6f); }
    public void SetDamage(int amount) => damage = Mathf.Max(1,amount);
    /// <summary>Per-weapon ballistics applied on equip: muzzle velocity, gravity, full-damage distance, maximum range and damage floor.</summary>
    public void SetBallistics(float muzzleVelocity, float bulletGravity, float fullDamage, float maxRange, float minFraction)
    {
        tracerSpeed = Mathf.Max(1f, muzzleVelocity);
        gravity = Mathf.Max(0f, bulletGravity);
        fullDamageRange = Mathf.Max(1f, fullDamage);
        range = Mathf.Max(fullDamageRange, maxRange);
        minimumDamageFraction = Mathf.Clamp(minFraction, .05f, 1f);
    }
    /// <summary>Linear damage falloff: full damage inside fullRange, down to damage*minFraction at maxRange.</summary>
    public static int DamageAtDistance(int baseDamage, float minFraction, float fullRange, float maxRange, float distance)
    {
        int minDamage = Mathf.Max(1, Mathf.RoundToInt(baseDamage * Mathf.Clamp(minFraction, .05f, 1f)));
        if (distance <= fullRange || maxRange <= fullRange) return Mathf.Max(1, baseDamage);
        float t = Mathf.Clamp01((distance - fullRange) / (maxRange - fullRange));
        return Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(baseDamage, minDamage, t)));
    }

    public bool FireBotShot(Vector3 eye, Vector3 direction, float spreadDegrees)
    {
        if (!ConquestMatch.CombatAllowed || TeamSafeZone.BlocksWeapons(health)) return false;
        if (!BotController.IsBot(this) || (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) ||
            muzzle == null || health == null || health.IsDead || weaponAnimation == null) return false;
        if (ammo != null && ammo.IsReloading) ammo.TryInterruptReloadForShot();
        if (!weaponAnimation.CanFire) return false;
        if (ammo != null && !ammo.CanShoot) { ammo.HandleDryFire(); return false; }
        nextSequence = Mathf.Max(nextSequence, lastAcceptedSequence, lastConfirmedSequence) + 1;
        Vector3 deviated = Deviate(direction, spreadDegrees);
        if (!ProcessShot(null, nextSequence, Now, eye, deviated, muzzle.position,
            damage, tracerSpeed, gravity, fullDamageRange, range,
            Mathf.Max(1, Mathf.RoundToInt(damage * minimumDamageFraction)),
            weaponAnimation != null ? weaponAnimation.WeaponId : "ak74")) return false;
        PlayMuzzleFlash(muzzle.position, deviated, nextSequence); ammo?.Consume();
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
        if (PlatformLifecycle.InputBlocked) return false;
        if (!ConquestMatch.CombatAllowed || TeamSafeZone.BlocksWeapons(health)) return false;
        if ((movement != null && movement.IsSprinting) || (weaponSway != null && weaponSway.SprintAmount > .05f)) return false;
        if (!photonView.IsMine || (PhotonNetwork.InRoom && photonView.OwnerActorNr != PhotonNetwork.LocalPlayer.ActorNumber) ||
            playerCamera == null || muzzle == null || (health != null && health.IsDead) || lastLocalShotFrame == Time.frameCount) return false;
        if (TeamSafeZone.ContainsAny(playerCamera.transform.position) || TeamSafeZone.ContainsAny(muzzle.position)) return false;
        if (ammo != null && ammo.IsReloading) ammo.TryInterruptReloadForShot();
        if (weaponAnimation != null && !weaponAnimation.CanFire) return false;
        if (ammo != null && !ammo.CanShoot) { ammo.HandleDryFire(); return false; }
        lastLocalShotFrame = Time.frameCount;
        if (weaponAnimation != null && weaponAnimation.WeaponId == MilkorSkill.WeaponId)
        {
            var launcher = GetComponent<MilkorSkill>();
            return launcher != null && launcher.Fire(muzzle.position, ApplySpread(playerCamera.transform.forward));
        }
        int sequence = ++nextSequence;
        double now = PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode ? PhotonNetwork.Time : Time.timeAsDouble;
        Vector3 eye = playerCamera.transform.position;
        Vector3 direction = ApplySpread(playerCamera.transform.forward);
        Vector3 start = muzzle.position;
        if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
        {
            if (!ProcessShot(photonView.Owner, sequence, now, eye, direction, start,
                damage, tracerSpeed, gravity, fullDamageRange, range,
                Mathf.Max(1, Mathf.RoundToInt(damage * minimumDamageFraction)),
                weaponAnimation != null ? weaponAnimation.WeaponId : "ak74")) return false;
            PlayMuzzleFlash(start, direction, sequence);
            ammo?.Consume();
            return true;
        }
        var predicted = ResolveHit(eye, direction, start, now, range);
        // Чисто локальный показ: не-мастер сразу видит цифру по врагу, без ожидания мастера и без RPC.
        var shotEntry = ShotEntry(weaponAnimation != null ? weaponAnimation.WeaponId : "ak74");
        int pellets = Mathf.Clamp(shotEntry != null ? shotEntry.pelletCount : 1, 1, 16);
        if (pellets == 1 && !BotController.IsBot(this) && predicted.player != null && IsEnemyHit(predicted.player))
        {
            int falloff = DamageAtDistance(damage, minimumDamageFraction, fullDamageRange, range, Vector3.Distance(start, predicted.point));
            int preview = Mathf.Max(1, Mathf.RoundToInt(falloff * predicted.damageMultiplier));
            DamageNumber.Spawn(predicted.point, Mathf.Clamp(preview, 1, 500), predicted.damageMultiplier > 1.01f);
        }
        for (int i = 0; i < pellets; i++)
        {
            int key = ProjectileId(sequence, i);
            Vector3 velocity = PelletVelocity((predicted.point - start).normalized * tracerSpeed, sequence, i, pellets, shotEntry != null ? shotEntry.pelletSpreadDegrees : 0);
            predictions[key] = SpawnVisual(start, velocity, key, 0f, gravity, range);
            predictionOrder.Enqueue(key);
        }
        PlayMuzzleFlash(start, direction, sequence);
        while (predictionOrder.Count > 128) predictions.Remove(predictionOrder.Dequeue());
        ammo?.Consume();
        int minDamage = Mathf.Max(1, Mathf.RoundToInt(damage * minimumDamageFraction));
        photonView.RPC(nameof(RequestShot), RpcTarget.MasterClient, sequence, now, eye, direction, start,
            damage, tracerSpeed, gravity, fullDamageRange, range, minDamage, weaponAnimation != null ? weaponAnimation.WeaponId : "ak74");
        return true;
    }

    [PunRPC]
    private void RequestShot(int sequence, double time, Vector3 eye, Vector3 direction, Vector3 start,
        int shotDamage, float shotVelocity, float shotGravity, float shotFullRange, float shotMaxRange, int shotMinDamage, string shotWeaponId, PhotonMessageInfo info)
    {
        if (!PhotonNetwork.IsMasterClient || info.Sender == null || info.Sender.ActorNumber != photonView.OwnerActorNr) return;
        if (string.IsNullOrEmpty(shotWeaponId) || shotWeaponId.Length > 32) return;
        var entry = ShotEntry(shotWeaponId);
        if (entry == null || shotWeaponId == MilkorSkill.WeaponId) return;
        // Damage and projectile count come from our catalog, including shotgun per-pellet damage.
        shotDamage = entry.damage; shotVelocity = entry.muzzleVelocity; shotGravity = entry.bulletGravity;
        shotFullRange = entry.fullDamageRange; shotMaxRange = entry.maximumRange;
        shotMinDamage = Mathf.Max(1, Mathf.RoundToInt(entry.damage * entry.minimumDamageFraction));
        ProcessShot(info.Sender, sequence, time, eye, direction, start,
            shotDamage, shotVelocity, shotGravity, shotFullRange, shotMaxRange, shotMinDamage, shotWeaponId);
    }

    private bool AcceptShot(int sequence, double time, double now, Vector3 eye, Vector3 direction, Vector3 start)
    {
        if (weaponAnimation != null && weaponAnimation.WeaponId == MilkorSkill.WeaponId) return false;
        if (!ConquestMatch.CombatAllowed || TeamSafeZone.BlocksWeapons(health) || TeamSafeZone.ContainsAny(eye) || TeamSafeZone.ContainsAny(start)) return false;
        if (sequence <= Mathf.Max(lastAcceptedSequence, lastConfirmedSequence) || sequence <= 0 ||
            double.IsNaN(time) || double.IsInfinity(time) || time < now - .75 || time > now + .1 ||
            !BulletHitUtility.IsFinite(eye) || !BulletHitUtility.IsFinite(direction) || !BulletHitUtility.IsFinite(start) ||
            direction.sqrMagnitude < .9f || direction.sqrMagnitude > 1.1f ||
            Vector3.Distance(eye, transform.position) > 3f || Vector3.Distance(start, eye) > maximumMuzzleReach ||
            (health != null && health.IsDead)) return false;
        float rate = recoil != null ? recoil.RoundsPerMinute : 600f;
        tokens = Mathf.Min(2f, tokens + (float)System.Math.Max(0, now - tokenTime) * rate / 60f);
        tokenTime = now;
        if (tokens < .99f) return false;
        tokens = Mathf.Max(0f, tokens - 1f);
        lastAcceptedSequence = sequence;
        return true;
    }

    private bool ProcessShot(Player sender, int sequence, double time, Vector3 eye, Vector3 direction, Vector3 start,
        int shotDamage, float shotVelocity, float shotGravity, float shotFullRange, float shotMaxRange, int shotMinDamage, string shotWeaponId)
    {
        double now = PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode ? PhotonNetwork.Time : Time.timeAsDouble;
        if (!AcceptShot(sequence, time, now, eye, direction, start)) return false;
        double rewindTime = System.Math.Max(now - maximumRewind, System.Math.Min(now, time));
        // Aim convergence only determines launch direction. Damage is deferred until a swept flight segment hits.
        var aim = ResolveHit(eye, direction, start, rewindTime, shotMaxRange);
        Vector3 velocity = (aim.point - start).normalized * shotVelocity;
        Vector3 toMuzzle = start - eye;
        var obstruction = toMuzzle.sqrMagnitude > .000001f
            ? BulletHitUtility.Cast(eye, toMuzzle.normalized, toMuzzle.magnitude, transform, rewindTime, hitMask)
            : default;
        if (obstruction.didHit) { start = eye; velocity = toMuzzle.normalized * shotVelocity; }
        PresentConfirmedShot(sequence, rewindTime, start, velocity, shotGravity, shotMaxRange, shotDamage, shotFullRange, shotMinDamage, shotWeaponId);
        if (PhotonNetwork.InRoom)
            photonView.RPC(nameof(ConfirmShot), RpcTarget.Others, sequence, rewindTime, start, velocity, shotGravity, shotMaxRange, shotDamage, shotFullRange, shotMinDamage, shotWeaponId);
        return true;
    }

    private BulletHitUtility.Hit ResolveHit(Vector3 eye, Vector3 direction, Vector3 start, double time, float maxRange)
    {
        Vector3 toMuzzle = start - eye;
        if (toMuzzle.sqrMagnitude > .000001f)
        {
            var obstruction = BulletHitUtility.Cast(eye, toMuzzle.normalized, toMuzzle.magnitude, transform, time, hitMask);
            if (obstruction.didHit) return obstruction;
        }
        var aimHit = BulletHitUtility.Cast(eye, direction, maxRange, transform, time, hitMask);
        Vector3 shotDirection = aimHit.point - start;
        if (shotDirection.sqrMagnitude < .000001f) return aimHit;
        return BulletHitUtility.Cast(start, shotDirection.normalized, Mathf.Min(maxRange, shotDirection.magnitude + .01f), transform, time, hitMask);
    }

    [PunRPC]
    private void ConfirmShot(int sequence, double time, Vector3 start, Vector3 velocity, float shotGravity, float shotMaxRange, int shotDamage, float shotFullRange, int shotMinDamage, string shotWeaponId, PhotonMessageInfo info)
    {
        if (info.Sender == null || !info.Sender.IsMasterClient) return;
        shotGravity = Mathf.Clamp(shotGravity, 0f, 25f);
        shotMaxRange = Mathf.Clamp(shotMaxRange, 10f, 500f);
        PresentConfirmedShot(sequence, time, start, velocity, shotGravity, shotMaxRange, shotDamage, shotFullRange, shotMinDamage, shotWeaponId);
    }
    private void PresentConfirmedShot(int sequence, double time, Vector3 start, Vector3 velocity,
        float flightGravity, float flightRange, int flightDamage, float flightFullRange, int flightMinDamage, string flightWeaponId)
    {
        if (sequence <= lastConfirmedSequence) return;
        lastConfirmedSequence = sequence;
        // Owners already displayed the flash at input time, including the master.
        if (!photonView.IsMine) PlayMuzzleFlash(start, velocity, sequence);
        float age = (float)System.Math.Max(0, Now - time);
        var entry = ShotEntry(flightWeaponId);
        int pellets = Mathf.Clamp(entry != null ? entry.pelletCount : 1, 1, 16);
        for (int i = 0; i < pellets; i++)
        {
            int key = ProjectileId(sequence, i);
            Vector3 pelletVelocity = PelletVelocity(velocity, sequence, i, pellets, entry != null ? entry.pelletSpreadDegrees : 0);
            if (predictions.TryGetValue(key, out var tracer))
            {
                if (tracer != null) tracer.ConfirmLaunch(start, pelletVelocity, age, ShotKey(key));
                predictions.Remove(key);
            }
            else if (!photonView.IsMine || !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient)
                tracer = SpawnVisual(start, pelletVelocity, key, age, flightGravity, flightRange);
            flights[key] = new Flight { position = start, velocity = pelletVelocity, time = time, visual = tracer,
                damage = flightDamage, weaponId = flightWeaponId, gravity = flightGravity, range = flightRange,
                fullDamageRange = flightFullRange, minDamage = flightMinDamage };
        }
    }

    private BulletTrail SpawnVisual(Vector3 start, Vector3 velocity, int sequence, float age, float visualGravity, float visualRange) =>
        BulletTrail.Spawn(start, velocity, visualGravity, visualRange, tracerMaterial, tracerLength, tracerWidth, bulletSize, ShotKey(sequence), age);

    private void PlayMuzzleFlash(Vector3 start, Vector3 direction, int sequence)
    {
        if (sequence > lastSoundSequence)
        {
            lastSoundSequence = sequence;
            weaponAnimation?.PlayShotBolt();
            GameAudio.Shot(weaponAnimation != null ? weaponAnimation.AudioProfile : GameAudio.Profile("ak74"), start, sequence,
                !BotController.IsBot(this) && (!PhotonNetwork.InRoom || photonView.IsMine));
        }
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
                Vector3 next = BulletHitUtility.FlightPosition(flight.position, flight.velocity, flight.gravity, dt);
                Vector3 segment = next - flight.position;
                float distance = Mathf.Min(segment.magnitude, Mathf.Max(0, flight.range - flight.distance));
                var hit = BulletHitUtility.Cast(flight.position, segment.normalized, distance, transform, flight.time + dt, hitMask);
                // A collision can end the sweep before its endpoint. Falloff must
                // use the distance to the actual impact, not the entire time step.
                float travelled = hit.didHit ? Vector3.Distance(flight.position, hit.point) : distance;
                flight.velocity += Vector3.down * (flight.gravity * dt);
                flight.time += dt; flight.age += dt; flight.distance += travelled;
                flight.position = hit.point;
                if (!hit.didHit && flight.distance < flight.range && flight.age < 10f) continue;
                if (hit.player != null && (friendlyFire || health == null || BotController.TeamOf(health) == 0 || BotController.TeamOf(health) != BotController.TeamOf(hit.player)))
                {
                    // Distance falloff towards the shooter's precomputed floor, then hit-zone multiplier.
                    int baseAtRange = flight.damage;
                    if (flight.distance > flight.fullDamageRange && flight.range > flight.fullDamageRange)
                        baseAtRange = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(flight.damage, flight.minDamage,
                            Mathf.Clamp01((flight.distance - flight.fullDamageRange) / (flight.range - flight.fullDamageRange)))));
                    int finalDamage = Mathf.Max(1, Mathf.RoundToInt(baseAtRange * hit.damageMultiplier));
                    hit.player.ApplyMasterDamage(finalDamage, flight.velocity.normalized * 4f, hit.point, photonView.Owner, BotController.IsBot(this) ? photonView.ViewID : 0, flight.weaponId);
                    ReportDamageNumber(photonView.Owner, hit.player, finalDamage, hit.point, hit.damageMultiplier > 1.01f);
                    if (PhotonNetwork.InRoom && photonView.Owner != null && !photonView.Owner.IsLocal &&
                        !BotController.IsBot(this) && IsEnemyHit(hit.player) && (ShotEntry(flight.weaponId)?.pelletCount ?? 1) > 1)
                        photonView.RPC(nameof(ConfirmPelletDamage), photonView.Owner, finalDamage, hit.point, hit.damageMultiplier > 1.01f);
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
    [PunRPC]
    private void ConfirmPelletDamage(int amount, Vector3 point, bool critical, PhotonMessageInfo info)
    {
        if (photonView.IsMine && info.Sender != null && info.Sender.IsMasterClient && BulletHitUtility.IsFinite(point))
            DamageNumber.Spawn(point, Mathf.Clamp(amount, 1, 500), critical);
    }
    /// <summary>Damage numbers are purely local: only the local shooter sees them, and only for enemy hits. No RPC.</summary>
    private void ReportDamageNumber(Player shooter, PlayerHealth victim, int amount, Vector3 point, bool crit)
    {
        if (!BulletHitUtility.IsFinite(point) || victim == null) return;
        if (BotController.IsBot(this)) return;
        if (!IsEnemyHit(victim)) return;
        amount = Mathf.Clamp(amount, 1, 500);
        if (!PhotonNetwork.InRoom)
        {
            DamageNumber.Spawn(point, amount, crit);
            return;
        }
        if (shooter == null || !shooter.IsLocal) return;
        DamageNumber.Spawn(point, amount, crit);
    }
    /// <summary>True when the victim is an enemy of the local shooter: same non-zero team or self hits are hidden.</summary>
    private bool IsEnemyHit(PlayerHealth victim)
    {
        if (victim == null || victim == health) return false;
        if (health == null) return true;
        int myTeam = BotController.TeamOf(health);
        if (TeamSafeZone.Protects(victim, myTeam)) return false;
        int victimTeam = BotController.TeamOf(victim);
        if (myTeam != 0 && victimTeam != 0 && myTeam == victimTeam) return false;
        return true;
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
        tokenTime = Now;
        if (newMasterClient != PhotonNetwork.LocalPlayer) return;
        foreach (var flight in flights.Values)
        {
            float dt = (float)System.Math.Max(0, Now - flight.time);
            Vector3 displacement = flight.velocity * dt + Vector3.down * (.5f * flight.gravity * dt * dt);
            flight.position += displacement; flight.distance += displacement.magnitude;
            flight.velocity += Vector3.down * (flight.gravity * dt);
            flight.age += dt; flight.time = Now;
        }
    }
}
