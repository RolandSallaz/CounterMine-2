using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

/// <summary>Master-authoritative grenade: every client simulates the same ballistic flight
/// locally, only the master's copy deals damage. Explosion is a procedural fireball.</summary>
public sealed class GrenadeProjectile : MonoBehaviour
{
    private const float Gravity = 9.81f;
    private const float Fuse = 2.4f;
    private const float BlastRadius = 13f;
    private const float FullDamageFraction = .5f;
    private const float MaxDamage = 100f;
    private const float MinDamage = 50f;
    private static GameObject grenadeModel;
    /// <summary>All live grenades on this client (visual copies included).</summary>
    public static readonly HashSet<GrenadeProjectile> Active = new HashSet<GrenadeProjectile>();

    private Vector3 position, velocity;
    private double throwTime;
    private Player killer;
    private int seed;
    private bool authority;
    private GameObject visual;
    /// <summary>Current simulated position (follows bounces, valid until the blast).</summary>
    public Vector3 Position => position;

    private void OnDestroy() => Active.Remove(this);

    public static void Launch(Vector3 start, Vector3 velocity, double throwTime, Player killer, int seed)
    {
        var go = new GameObject("Grenade");
        go.transform.position = start;
        var projectile = go.AddComponent<GrenadeProjectile>();
        projectile.position = start;
        projectile.velocity = velocity;
        projectile.throwTime = throwTime;
        projectile.killer = killer;
        projectile.seed = seed;
        projectile.authority = !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;
        Active.Add(projectile);
        if (grenadeModel == null) grenadeModel = Resources.Load<GameObject>("Grenade/grenade");
        if (grenadeModel != null)
        {
            projectile.visual = Instantiate(grenadeModel, start, Random.rotationUniform);
            projectile.visual.transform.SetParent(go.transform, true);
            foreach (var collider in projectile.visual.GetComponentsInChildren<Collider>(true)) Destroy(collider);
        }
    }

    private void Update()
    {
        double now = PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;
        if (now >= throwTime + Fuse) { Explode(); return; }
        Simulate(Time.deltaTime);
    }

    private void Simulate(float dt)
    {
        if (velocity.sqrMagnitude > .000001f)
        {
            float remaining = dt;
            while (remaining > 0f)
            {
                float h = Mathf.Min(1f / 120f, remaining);
                remaining -= h;
                Vector3 next = position + velocity * h + Vector3.down * (.5f * Gravity * h * h);
                Vector3 segment = next - position;
                float distance = segment.magnitude;
                if (distance > .000001f && CastWorld(position, segment / distance, distance,
                    out Vector3 point, out Vector3 normal))
                {
                    position = point + normal * .02f;
                    Vector3 reflected = velocity;
                    float into = Vector3.Dot(reflected, normal);
                    if (into < 0f) reflected -= normal * (into * 1.42f);
                    reflected *= .7f;
                    velocity = reflected.magnitude < 1f ? Vector3.zero : reflected;
                }
                else position = next;
                velocity += Vector3.down * (Gravity * h);
                if (velocity.sqrMagnitude < .000001f) break;
            }
            transform.position = position;
            if (visual != null && velocity.sqrMagnitude > 1f)
                visual.transform.Rotate(new Vector3(7f, 3f, 5f) * (Time.deltaTime * velocity.magnitude * .2f), Space.Self);
        }
        else transform.position = position;
    }

    private static bool CastWorld(Vector3 origin, Vector3 direction, float distance,
        out Vector3 point, out Vector3 normal)
    {
        // The shared query handles saturated hit buffers without dropping nearby walls.
        var hit = BulletHitUtility.CastCover(origin, direction, distance, null, ~0);
        point = hit.point;
        normal = hit.normal;
        return hit.didHit;
    }

    public static bool IsBlastBlocked(Vector3 origin, Vector3 target)
    {
        Vector3 delta = target - origin;
        float distance = delta.magnitude;
        if (distance <= .001f) return false;
        Vector3 direction = delta / distance;
        // Reverse cast catches one-sided meshes and explosions originating inside solid cover.
        return BulletHitUtility.CastCover(origin, direction, distance, null, ~0).didHit ||
            BulletHitUtility.CastCover(target, -direction, distance, null, ~0).didHit;
    }

    private void Explode()
    {
        Active.Remove(this);
        ExplosionFlash.Spawn(position, seed);
        if (authority) DealDamage(position);
        Destroy(gameObject);
    }

    private void DealDamage(Vector3 at)
    {
        int throwerTeam = 0;
        if (killer != null && killer.CustomProperties["team"] is int team) throwerTeam = team;
        // Visual lift must never move the damage origin through a ceiling or low cover.
        Vector3 blastOrigin = at;
        // Snapshot to avoid mutation during iteration.
        var victims = new List<PlayerHealth>(PlayerHealth.ActivePlayers);
        foreach (var victim in victims)
        {
            if (victim == null || victim.IsDead) continue;
            var capsule = victim.GetComponent<CharacterController>();
            Vector3 chest = capsule != null && capsule.enabled ? capsule.bounds.center : victim.transform.position + Vector3.up * 1.2f;
            Vector3 toVictim = chest - blastOrigin;
            float distance = toVictim.magnitude;
            if (distance > BlastRadius) continue;
            int victimTeam = BotController.TeamOf(victim);
            bool isThrower = killer != null && victim.photonView != null &&
                !BotController.IsBot(victim) && victim.photonView.OwnerActorNr == killer.ActorNumber;
            // Own grenade always hurts its thrower; other teammates are still spared.
            if (!isThrower && throwerTeam != 0 && victimTeam != 0 && throwerTeam == victimTeam) continue;
            if (IsBlastBlocked(blastOrigin, chest)) continue;
            float fullRadius = BlastRadius * FullDamageFraction;
            float fall = distance <= fullRadius ? 1f
                : 1f - (distance - fullRadius) / (BlastRadius - fullRadius);
            int damage = Mathf.Max(1, Mathf.RoundToInt(Mathf.Lerp(MinDamage, MaxDamage, fall)));
            // Heavy blast impulse: survivors ignore it, kills fling the ragdoll away from the blast.
            Vector3 flat = distance > .001f ? toVictim / distance : Vector3.zero;
            Vector3 force = (flat + Vector3.up * .7f).normalized * 45f;
            victim.ApplyMasterDamage(damage, force, chest, killer, 0, "grenade");
        }
    }
}
