using Photon.Pun;
using UnityEngine;

/// <summary>Room-owned combat actor. Only the master runs decisions and movement.</summary>
[DefaultExecutionOrder(-90)]
public sealed class BotController : MonoBehaviourPun
{
    public int Slot => photonView.InstantiationData is object[] d && d.Length > 0 && d[0] is int n ? n : 0;
    public int Team => photonView.InstantiationData is object[] d && d.Length > 1 && d[1] is int n ? n : 2;
    public string DisplayName => "BOT " + (Slot + 1).ToString("00");
    public static bool IsBot(Component actor) => actor != null && actor.GetComponentInParent<BotController>() != null;
    public static int TeamOf(PlayerHealth actor)
    {
        var bot = actor.GetComponent<BotController>();
        return bot != null ? bot.Team : actor.photonView.Owner?.CustomProperties["team"] is int team ? team : 0;
    }
    private CharacterController capsule;
    private PlayerHealth health, target;
    private NetworkWeapon weapon;
    private Camera aimCamera;
    private float thinkAt, fireAt, vertical;
    private Vector3 moveDirection;
    private static readonly float[] AvoidAngles = { 25f, -25f, 50f, -50f, 85f, -85f, 130f };
    private float visibilityAt, patrolRetryAt, objectiveRetryAt;
    private bool targetVisible;
    [Header("Patrol (no target)")]
    [SerializeField, Min(0f)] private float patrolSpeed = 1.7f;
    [SerializeField, Min(0f)] private float combatSpeed = 2.3f;
    [SerializeField, Min(1f)] private float patrolMinDistance = 6f;
    [SerializeField, Min(1f)] private float patrolMaxDistance = 20f;
    [Header("Objective: push the enemy base, then roam")]
    [SerializeField, Min(1f)] private float baseHoldRadius = 5f;
    [SerializeField, Min(1f)] private float roamMinDistance = 10f;
    [SerializeField, Min(1f)] private float roamMaxDistance = 30f;
    [Header("Wall avoidance")]
    [SerializeField, Min(.5f)] private float avoidDistance = 2.4f;
    [Header("Sprint")]
    [SerializeField, Min(0f)] private float sprintSpeed = 7.15f;
    [SerializeField, Min(1f)] private float sprintDistance = 12f;
    [Header("Spread (degrees)")]
    [SerializeField, Min(0f)] private float botBaseSpread = 2f;
    [SerializeField, Min(0f)] private float botSpreadPerMeter = .05f;
    [SerializeField, Min(1f)] private float botSprintSpreadMultiplier = 2f;
    [Header("Engagement")]
    [SerializeField, Min(1f)] private float fireRange = 28f;
    private Vector3 patrolPoint;
    private bool hasPatrolPoint;
    private float idleUntil;
    private float stuckCheckAt;
    private Vector3 stuckReference;
    private Vector3 objectivePoint;
    private bool hasObjective;
    private bool reachedBase;
    private int stuckCount;

    private void Awake()
    {
        capsule = GetComponent<CharacterController>(); health = GetComponent<PlayerHealth>();
        weapon = GetComponent<NetworkWeapon>(); aimCamera = GetComponentInChildren<Camera>(true);
        // These behaviours read the human's keyboard/mouse or manipulate its view.
        foreach (var component in GetComponentsInChildren<MonoBehaviour>(true))
            if (component is PlayerController || component is PlayerCameraLook || component is PlayerHeadBob ||
                component is PlayerLeanController || component is WeaponAimController || component is WeaponSway ||
                component is WeaponRecoilController) component.enabled = false;
        foreach (var camera in GetComponentsInChildren<Camera>(true)) camera.enabled = false;
        foreach (var listener in GetComponentsInChildren<AudioListener>(true)) listener.enabled = false;
    }
    private void Start()
    {
        name = DisplayName;
        foreach (var colors in GetComponentsInChildren<CharacterTeamColors>(true)) colors.ApplyTeam(Team);
        fireAt = Time.time + 1.5f + Slot * .15f;
        FindObjective();
    }
    /// <summary>Nearest enemy team spawn: the push objective. Spawn points are static, so one lookup is enough.</summary>
    private void FindObjective()
    {
        if (Time.time < objectiveRetryAt) return;
        objectiveRetryAt = Time.time + 2f;
        hasObjective = false;
        float best = float.PositiveInfinity;
        foreach (var point in FindObjectsByType<TeamSpawnPoint>(FindObjectsSortMode.None))
        {
            if (point == null || point.Team == Team) continue;
            float distance = (point.transform.position - transform.position).sqrMagnitude;
            if (distance < best) { best = distance; objectivePoint = point.transform.position; hasObjective = true; }
        }
    }
    private void Update()
    {
        if ((PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) || health.IsDead || !capsule.enabled) return;
        if (Time.time >= thinkAt)
        {
            thinkAt = Time.time + .25f;
            var previousTarget = target;
            target = null; float best = 40f * 40f;
            foreach (var candidate in PlayerHealth.ActivePlayers)
            {
                if (candidate == null || candidate == health || candidate.IsDead || TeamOf(candidate) == Team) continue;
                float distance = (candidate.transform.position - transform.position).sqrMagnitude;
                if (distance < best) { best = distance; target = candidate; }
            }
            if (target != previousTarget) { visibilityAt = 0; targetVisible = false; }
        }
        moveDirection = Vector3.zero;
        float speed = combatSpeed;
        bool sprinting = false;
        if (target != null && !target.IsDead)
        {
            stuckCount = 0;
            hasPatrolPoint = false;
            Vector3 aim = target.transform.position + Vector3.up * 1.1f;
            Vector3 flat = aim - transform.position; flat.y = 0;
            if (flat.sqrMagnitude > .01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), 180f * Time.deltaTime);
            Vector3 eye = aimCamera.transform.position;
            Vector3 direction = (aim - eye).normalized;
            aimCamera.transform.rotation = Quaternion.LookRotation(direction);
            if (Time.time >= visibilityAt)
            {
                visibilityAt = Time.time + .15f;
                targetVisible = BulletHitUtility.Cast(eye, direction, 40f, transform, PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble, ~0).player == target;
            }
            bool visible = targetVisible;
            if (flat.magnitude > (visible ? 9f : 2f) || TeamSafeZone.BlocksWeapons(health))
            {
                moveDirection = flat.normalized;
                // Distant chase: sprint to close the gap, slow down for strafe and fire range.
                sprinting = flat.magnitude > sprintDistance;
            }
            else if (visible)
            {
                // Strafe while firing, drifting closer beyond point-blank so the bot never stands still.
                Vector3 strafe = transform.right * Mathf.Sin(Time.time * .65f + Slot * 2);
                Vector3 approach = flat.magnitude > 4f ? flat.normalized * .4f : Vector3.zero;
                moveDirection = (strafe + approach).normalized;
            }
            if (visible && flat.magnitude <= fireRange && Time.time >= fireAt && Vector3.Dot(transform.forward, flat.normalized) > .96f)
            {
                fireAt = Time.time + Random.Range(.25f, .5f);
                // Cone spread in degrees: base + distance growth, doubled on the run.
                float spread = (botBaseSpread + botSpreadPerMeter * flat.magnitude) * (sprinting ? botSprintSpreadMultiplier : 1f);
                weapon.FireBotShot(eye, direction, spread);
            }
        }
        else
        {
            // Phase 1: push the enemy base. Phase 2 (reached once): roam the map freely.
            // A random detour is only used to unstick from geometry.
            if (!hasObjective) FindObjective();
            if (hasObjective && !reachedBase && FlatDistance(transform.position, objectivePoint) <= baseHoldRadius)
                reachedBase = true;
            Vector3 flatPatrol = hasPatrolPoint ? patrolPoint - transform.position : Vector3.zero;
            flatPatrol.y = 0;
            if (!hasPatrolPoint || flatPatrol.magnitude < 1.2f)
            {
                if (hasPatrolPoint) { idleUntil = Time.time + Random.Range(.4f, 1.2f); stuckCount = 0; }
                hasPatrolPoint = false;
                if (hasObjective && !reachedBase && stuckCount < 2)
                {
                    patrolPoint = objectivePoint;
                    hasPatrolPoint = true;
                }
                else
                {
                    Vector3 anchor = transform.position;
                    float minDistance, maxDistance;
                    if (!hasObjective || reachedBase) { minDistance = roamMinDistance; maxDistance = roamMaxDistance; }
                    else { minDistance = patrolMinDistance; maxDistance = patrolMaxDistance; }
                    hasPatrolPoint = TryPickPatrolPoint(anchor, minDistance, maxDistance, out patrolPoint);
                }
                stuckReference = transform.position;
                stuckCheckAt = Time.time + 1.5f;
            }
            else if (Time.time >= idleUntil)
            {
                moveDirection = flatPatrol.normalized;
                speed = patrolSpeed;
                // Long push to the enemy base: sprint, stroll near hold points.
                sprinting = flatPatrol.magnitude > sprintDistance;
                if (flatPatrol.sqrMagnitude > .01f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flatPatrol), 180f * Time.deltaTime);
                if (Time.time >= stuckCheckAt)
                {
                    // Barely moved while trying to walk: drop the point, a new one gets picked above.
                    // Two stucks in a row force a wide roam point to escape dead ends.
                    if ((transform.position - stuckReference).sqrMagnitude < .09f) { hasPatrolPoint = false; stuckCount++; }
                    stuckReference = transform.position;
                    stuckCheckAt = Time.time + 1.5f;
                }
            }
        }
        if (sprinting) speed = sprintSpeed;
        if (moveDirection.sqrMagnitude > .01f)
        {
            // Whisker avoidance: straight ahead first, then fan out. Beats wall sliding in corners.
            moveDirection = Steer(moveDirection);
            if (moveDirection.sqrMagnitude > .01f)
            {
                Vector3 origin = transform.position + Vector3.up * .6f;
                if (!Physics.Raycast(origin + moveDirection.normalized * .8f, Vector3.down, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) moveDirection = Vector3.zero;
            }
        }
        vertical = capsule.isGrounded ? -2f : Mathf.Max(-25f, vertical - 24f * Time.deltaTime);
        capsule.Move((moveDirection * speed + Vector3.up * vertical) * Time.deltaTime);
    }

    /// <summary>Steer around walls: forward probe, then whiskers at increasing angles. Returns zero when boxed in.</summary>
    private Vector3 Steer(Vector3 desired)
    {
        Vector3 direction = desired.normalized;
        Vector3 origin = transform.position + Vector3.up * .6f;
        if (!BulletHitUtility.CastCover(origin, direction, avoidDistance, transform, ~0, true).didHit) return direction;
        float side = Slot % 2 == 0 ? 1f : -1f;
        foreach (float angle in AvoidAngles)
        {
            Vector3 candidate = Quaternion.Euler(0f, angle * side, 0f) * direction;
            if (!BulletHitUtility.CastCover(origin, candidate, avoidDistance, transform, ~0, true).didHit) return candidate;
        }
        return Vector3.zero;
    }

    /// <summary>Random reachable ground point around a center, same sampling as the room spawner.</summary>
    private bool TryPickPatrolPoint(Vector3 center, float minDistance, float maxDistance, out Vector3 point)
    {
        point = transform.position;
        if (Time.time < patrolRetryAt) return false;
        patrolRetryAt = Time.time + .5f;
        maxDistance = Mathf.Max(minDistance, maxDistance);
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance = Random.Range(minDistance, maxDistance);
            Vector3 probe = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
            if (!Physics.Raycast(probe + Vector3.up * 5f, Vector3.down, out var ground, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || ground.normal.y < .7f) continue;
            Vector3 candidate = ground.point + Vector3.up * .08f;
            if (TeamSafeZone.IsEnemyArea(candidate, Team)) continue;
            if (Physics.CheckCapsule(candidate + Vector3.up * .3f, candidate + Vector3.up * 1.5f, .26f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            // Skip points behind walls: the bot should be able to walk there in a straight line.
            Vector3 toCandidate = candidate - transform.position; toCandidate.y = 0;
            if (toCandidate.magnitude > 2f)
            {
                Vector3 origin = transform.position + Vector3.up * .6f;
                var wall = BulletHitUtility.CastCover(origin, toCandidate.normalized, toCandidate.magnitude, transform, ~0, true);
                if (wall.didHit) continue;
            }
            point = candidate;
            return true;
        }
        return false;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0; b.y = 0;
        return (a - b).magnitude;
    }
}
