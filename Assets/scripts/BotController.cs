using Photon.Pun;
using UnityEngine;
using UnityEngine.AI;

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
    private WeaponAmmo ammo;
    private BotNavigation navigation;
    private NavMeshPath path;
    private Vector3[] corners = System.Array.Empty<Vector3>();
    private int corner, patrolIndex, burst;
    private Vector3 destination, lastSeen, stuckReference;
    private float thinkAt, planAt, fireAt, seenAt = -100, vertical, stuckAt, coverUntil;
    private bool visible, hadAuthority;
    private Vector3 mapCenter;
    [Header("Navigation")]
    [SerializeField, Min(0f)] private float patrolSpeed = 3.6f;
    [SerializeField, Min(0f)] private float combatSpeed = 2.7f;
    [SerializeField, Min(0f)] private float sprintSpeed = 7.15f;
    [Header("Combat")]
    [SerializeField, Min(1f)] private float fireRange = 40f;
    [SerializeField, Min(0f)] private float botBaseSpread = 2f;
    [SerializeField, Min(0f)] private float botSpreadPerMeter = .05f;
    [SerializeField, Min(1f)] private float memorySeconds = 6f;
    // Distributed destinations: both flanks, cargo courts, bridge and roof exits.
    private static readonly Vector3[] Patrol = {
        new Vector3(-30,0,-11.8f), new Vector3(0,6,0), new Vector3(28,0,11.8f),
        new Vector3(13.9f,0,-4), new Vector3(0,3.5f,5), new Vector3(-28,0,11.8f),
        new Vector3(8,6,0), new Vector3(30,0,-11.8f), new Vector3(-13.9f,0,4),
        new Vector3(0,3.5f,-5), new Vector3(-8,6,0), new Vector3(5,0,11.8f)
    };
    private bool Authority => !PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient;
    private void Awake()
    {
        // NavMeshPath allocates native state and cannot be created in a field initializer.
        path = new NavMeshPath();
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
        ammo = GetComponent<WeaponAmmo>();
        var spawns = FindObjectsByType<TeamSpawnPoint>(FindObjectsSortMode.None);
        foreach (var spawn in spawns) mapCenter += spawn.transform.position;
        if (spawns.Length > 0) mapCenter /= spawns.Length;
        mapCenter.y = 0;
        patrolIndex = (Slot * 5 + (Team == 2 ? 6 : 0)) % Patrol.Length;
        stuckReference = transform.position; stuckAt = Time.time + 2;
    }
    private void Update()
    {
        if (!Authority) { hadAuthority = false; return; }
        if (health == null || health.IsDead || !capsule.enabled) return;
        if (!hadAuthority)
        {
            hadAuthority = true;
            ClearPath(); target = null; visible = false; seenAt = -100;
            thinkAt = Time.time + Slot*.04f; planAt = 0; fireAt = Time.time + .5f;
        }
        navigation = BotNavigation.Ensure();
        if (navigation.Ready)
        {
            if (Time.time >= thinkAt) { thinkAt = Time.time + .3f; Sense(); }
            if (Time.time >= planAt) { planAt = Time.time + .9f + Slot*.03f; Plan(); }
            MoveAlongPath(Time.deltaTime);
            AimAndFire();
        }
        else ApplyGravity(Vector3.zero,Time.deltaTime);
    }
    private bool CanSee(PlayerHealth candidate)
    {
        if (candidate == null || candidate.IsDead || TeamOf(candidate) == Team || TeamSafeZone.Protects(candidate, Team)) return false;
        Vector3 offset = candidate.transform.position + Vector3.up*1.1f - aimCamera.transform.position;
        if (offset.sqrMagnitude > 48*48) return false;
        // Close enemies can be noticed from any direction; farther ones must be in view.
        if (offset.sqrMagnitude > 6*6 && Vector3.Dot(transform.forward, offset.normalized) < -.1f) return false;
        return BulletHitUtility.Cast(aimCamera.transform.position, offset.normalized, offset.magnitude+.5f,
            transform, PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble, ~0).player == candidate;
    }
    private void Sense()
    {
        PlayerHealth bestTarget = null;
        float best = float.PositiveInfinity;
        foreach (var candidate in PlayerHealth.ActivePlayers)
        {
            if (candidate == health || !CanSee(candidate)) continue;
            float score = (candidate.transform.position-transform.position).sqrMagnitude;
            if (candidate == target) score *= .65f;
            if (score < best) { best = score; bestTarget = candidate; }
        }
        visible = bestTarget != null;
        if (visible)
        {
            if (target != bestTarget) { fireAt = Time.time + Random.Range(.25f,.5f); burst = 0; planAt = 0; }
            target = bestTarget; lastSeen = target.transform.position; seenAt = Time.time;
        }
        else if (Time.time-seenAt > memorySeconds || target == null || target.IsDead)
        {
            target = null;
        }
    }
    private bool SetDestination(Vector3 point)
    {
        if (!navigation.Sample(transform.position,Team,1.2f,out var start)) return false;
        if (!navigation.Sample(point,Team,1.2f,out var end)) return false;
        // Script reload during Play Mode can clear this non-serialized native wrapper.
        path ??= new NavMeshPath();
        if (!NavMesh.CalculatePath(start,end,navigation.Filter(Team),path) || path.status != NavMeshPathStatus.PathComplete)
        {
            return false;
        }
        var next = path.corners;
        if (next.Length < 2) return false;
        corners = next; corner = 1; destination = end;
        return true;
    }
    private void ClearPath() { corners = System.Array.Empty<Vector3>(); corner = 0; }
    private bool HasPath => corner < corners.Length;
    private void Plan()
    {
        bool threatened = target != null && Time.time-seenAt < memorySeconds;
        bool recover = ammo != null && ammo.IsReloading || health.CurrentHealth < health.MaximumHealth*.35f;
        if (threatened)
        {
            if (recover && Time.time >= coverUntil && FindCover()) { coverUntil = Time.time + 2; return; }
            if (recover && Time.time < coverUntil) return;
            if (!visible)
            {
                // Only the last observed position is pursued, never a hidden live transform.
                if (Vector3.Distance(transform.position,lastSeen)<1.8f) { target=null; seenAt=-100; ClearPath(); }
                else if (SetDestination(lastSeen)) return;
            }
            else
            {
                Vector3 away = transform.position-lastSeen; away.y=0;
                float distance = away.magnitude;
                if (TeamSafeZone.BlocksWeapons(health) || distance > fireRange*.85f)
                {
                    if (SetDestination(lastSeen + away.normalized*12)) return;
                }
                else
                {
                    // Enemy co-located with the bot: direction is undefined, hold position and keep firing.
                    if (distance < .05f) { ClearPath(); return; }
                    Vector3 side = Vector3.Cross(Vector3.up,away.normalized) * ((Slot%2==0)?1:-1);
                    Vector3 retreat = distance<6 ? away.normalized*3 : Vector3.zero;
                    if (SetDestination(transform.position+side*Random.Range(2f,4f)+retreat) ||
                        SetDestination(transform.position-side*3+retreat)) return;
                    ClearPath(); return;
                }
            }
        }
        if (HasPath) return;
        if (ammo != null && ammo.MagAmmo < ammo.MagazineSize/2) ammo.TryStartReload();
        // Each bot cycles all sectors with a different offset, rather than camping the enemy base.
        for (int attempt=0;attempt<Patrol.Length;attempt++)
        {
            Vector3 point = mapCenter + Patrol[patrolIndex];
            patrolIndex = (patrolIndex+1)%Patrol.Length;
            if (Vector3.Distance(point,transform.position)>3 && SetDestination(point)) return;
        }
    }
    private bool FindCover()
    {
        for (int i=0;i<10;i++)
        {
            float angle = (i*36+Slot*47)*Mathf.Deg2Rad;
            Vector3 point = transform.position + new Vector3(Mathf.Cos(angle),0,Mathf.Sin(angle))*Random.Range(3f,7f);
            if (!navigation.Sample(point,Team,1.2f,out point)) continue;
            Vector3 direction = lastSeen + Vector3.up*1.1f - (point+Vector3.up*1.2f);
            if (!BulletHitUtility.CastCover(point+Vector3.up*1.2f,direction.normalized,direction.magnitude,
                transform,~0,true).didHit) continue;
            if (SetDestination(point)) return true;
        }
        return false;
    }
    private void MoveAlongPath(float deltaTime)
    {
        while (HasPath)
        {
            Vector3 delta = corners[corner]-transform.position;
            if (new Vector2(delta.x,delta.z).magnitude>.12f || Mathf.Abs(delta.y)>.6f) break;
            corner++;
        }
        Vector3 move = Vector3.zero;
        if (HasPath)
        {
            Vector3 delta = corners[corner]-transform.position; delta.y=0;
            move=delta.normalized;
            // Light separation keeps teammates from pushing into the same corner.
            Vector3 separation=Vector3.zero;
            foreach (var other in PlayerHealth.ActivePlayers)
            {
                if (other==null || other==health || other.IsDead) continue;
                Vector3 away=transform.position-other.transform.position;
                if (Mathf.Abs(away.y)>.8f) continue;
                away.y=0;float distance=away.magnitude;
                if (distance>.01f && distance<.8f) separation += away/distance*(.8f-distance);
            }
            var desired=(move+separation).normalized;
            if (navigation.Sample(transform.position,Team,.8f,out var floor))
            {
                if (!NavMesh.Raycast(floor,floor+desired*.65f,out _,navigation.Filter(Team))) move=desired;
                else if (NavMesh.Raycast(floor,floor+move*Mathf.Min(.2f,delta.magnitude*.5f),out _,navigation.Filter(Team))) move=Vector3.zero;
            }
            else { ClearPath(); move=Vector3.zero; }
            float speed = visible ? combatSpeed : Vector3.Distance(transform.position,destination)>5 ? sprintSpeed : patrolSpeed;
            if (!visible && move.sqrMagnitude>.01f) transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(move),240*deltaTime);
            move *= Mathf.Min(speed,delta.magnitude/Mathf.Max(.001f,deltaTime));
        }
        else planAt=Mathf.Min(planAt,Time.time+.15f);
        ApplyGravity(move,deltaTime);
        if (Time.time>=stuckAt)
        {
            if (HasPath && (transform.position-stuckReference).sqrMagnitude<.12f)
            {
                ClearPath(); coverUntil=0; target=null; visible=false; seenAt=-100;
                patrolIndex=(patrolIndex+3)%Patrol.Length;planAt=0;
            }
            stuckReference=transform.position;stuckAt=Time.time+2;
        }
    }
    private void ApplyGravity(Vector3 velocity,float deltaTime)
    {
        vertical=capsule.isGrounded?-2:Mathf.Max(-25,vertical-24*deltaTime);
        capsule.Move((velocity+Vector3.up*vertical)*deltaTime);
    }
    private void AimAndFire()
    {
        if (!visible || target==null) return;
        Vector3 aim=lastSeen+Vector3.up*1.1f;
        Vector3 flat=aim-transform.position;flat.y=0;
        if (flat.sqrMagnitude>.01f) transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(flat),240*Time.deltaTime);
        Vector3 eye=aimCamera.transform.position, direction=(aim-eye).normalized;
        aimCamera.transform.rotation=Quaternion.LookRotation(direction);
        if (Time.time<fireAt || flat.magnitude>fireRange || Vector3.Dot(transform.forward,flat.normalized)<.96f || !CanSee(target)) return;
        if (weapon.FireBotShot(eye,direction,botBaseSpread+botSpreadPerMeter*flat.magnitude))
        {
            burst++;
            if (burst>=3) { burst=0;fireAt=Time.time+Random.Range(.35f,.65f); }
            else fireAt=Time.time+Random.Range(.12f,.2f);
        }
        else fireAt=Time.time+.15f;
    }
}
