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
    [Header("Patrol (no target)")]
    [SerializeField, Min(0f)] private float patrolSpeed = 1.7f;
    [SerializeField, Min(0f)] private float combatSpeed = 2.3f;
    [SerializeField, Min(1f)] private float patrolMinDistance = 6f;
    [SerializeField, Min(1f)] private float patrolMaxDistance = 20f;
    [Header("Objective: push the enemy base")]
    [SerializeField, Min(1f)] private float baseHoldRadius = 5f;
    [Header("Sprint")]
    [SerializeField, Min(0f)] private float sprintSpeed = 4.5f;
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
    private bool detourNext;

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
            target = null; float best = 40f * 40f;
            foreach (var candidate in PlayerHealth.ActivePlayers)
            {
                if (candidate == null || candidate == health || candidate.IsDead || TeamOf(candidate) == Team) continue;
                float distance = (candidate.transform.position - transform.position).sqrMagnitude;
                if (distance < best) { best = distance; target = candidate; }
            }
        }
        moveDirection = Vector3.zero;
        float speed = combatSpeed;
        bool sprinting = false;
        if (target != null && !target.IsDead)
        {
            Vector3 aim = target.transform.position + Vector3.up * 1.1f;
            Vector3 flat = aim - transform.position; flat.y = 0;
            if (flat.sqrMagnitude > .01f)
                transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat), 180f * Time.deltaTime);
            Vector3 eye = aimCamera.transform.position;
            Vector3 direction = (aim - eye).normalized;
            aimCamera.transform.rotation = Quaternion.LookRotation(direction);
            var sight = BulletHitUtility.Cast(eye, direction, 40f, transform, PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble, ~0);
            bool visible = sight.player == target;
            if (flat.magnitude > (visible ? 9f : 2f))
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
            // No enemy in sight: push the enemy base. Hold around it once reached,
            // take a random detour only to unstick from geometry.
            if (!hasObjective) FindObjective();
            float objectiveDistance = hasObjective ? FlatDistance(transform.position, objectivePoint) : 0f;
            bool holdingBase = hasObjective && objectiveDistance <= baseHoldRadius;
            Vector3 flatPatrol = hasPatrolPoint ? patrolPoint - transform.position : Vector3.zero;
            flatPatrol.y = 0;
            if (!hasPatrolPoint || flatPatrol.magnitude < 1.2f)
            {
                if (hasPatrolPoint) idleUntil = Time.time + Random.Range(1f, 3f);
                hasPatrolPoint = false;
                if (hasObjective && !holdingBase && !detourNext)
                {
                    patrolPoint = objectivePoint;
                    hasPatrolPoint = true;
                }
                else
                {
                    Vector3 anchor;
                    float minDistance, maxDistance;
                    if (detourNext || !hasObjective)
                    { anchor = transform.position; minDistance = patrolMinDistance; maxDistance = patrolMaxDistance; }
                    else
                    { anchor = objectivePoint; minDistance = 1.5f; maxDistance = baseHoldRadius; }
                    hasPatrolPoint = TryPickPatrolPoint(anchor, minDistance, maxDistance, out patrolPoint);
                }
                detourNext = false;
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
                    // Barely moved while trying to walk: stuck on geometry, detour once, then resume the push.
                    if ((transform.position - stuckReference).sqrMagnitude < .09f) { hasPatrolPoint = false; detourNext = true; }
                    stuckReference = transform.position;
                    stuckCheckAt = Time.time + 1.5f;
                }
            }
        }
        if (sprinting) speed = sprintSpeed;
        if (moveDirection.sqrMagnitude > .01f)
        {
            Vector3 origin = transform.position + Vector3.up * .6f;
            var obstacle = BulletHitUtility.Cast(origin, moveDirection.normalized, 1.1f, transform, Time.timeAsDouble, ~0);
            if (obstacle.didHit) moveDirection = Vector3.Cross(Vector3.up, obstacle.normal).normalized * (Slot % 2 == 0 ? 1 : -1);
            if (!Physics.Raycast(origin + moveDirection.normalized * .8f, Vector3.down, 1.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) moveDirection = Vector3.zero;
        }
        vertical = capsule.isGrounded ? -2f : Mathf.Max(-25f, vertical - 24f * Time.deltaTime);
        capsule.Move((moveDirection * speed + Vector3.up * vertical) * Time.deltaTime);
    }

    /// <summary>Random reachable ground point around a center, same sampling as the room spawner.</summary>
    private bool TryPickPatrolPoint(Vector3 center, float minDistance, float maxDistance, out Vector3 point)
    {
        point = transform.position;
        maxDistance = Mathf.Max(minDistance, maxDistance);
        for (int attempt = 0; attempt < 12; attempt++)
        {
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            float distance = Random.Range(minDistance, maxDistance);
            Vector3 probe = center + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * distance;
            if (!Physics.Raycast(probe + Vector3.up * 5f, Vector3.down, out var ground, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || ground.normal.y < .7f) continue;
            Vector3 candidate = ground.point + Vector3.up * .08f;
            if (Physics.CheckCapsule(candidate + Vector3.up * .3f, candidate + Vector3.up * 1.5f, .26f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            // Skip points behind walls: the bot should be able to walk there in a straight line.
            Vector3 toCandidate = candidate - transform.position; toCandidate.y = 0;
            if (toCandidate.magnitude > 2f)
            {
                Vector3 origin = transform.position + Vector3.up * .6f;
                var wall = BulletHitUtility.Cast(origin, toCandidate.normalized, toCandidate.magnitude, transform, Time.timeAsDouble, ~0);
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
