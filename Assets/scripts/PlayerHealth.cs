using System;
using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

[RequireComponent(typeof(PhotonView), typeof(CharacterController))]
public sealed class PlayerHealth : MonoBehaviourPunCallbacks
{
    public readonly struct AssistInfo
    {
        public readonly int actorNr, team;
        public readonly string name;
        public AssistInfo(int actorNr, string name, int team) { this.actorNr = actorNr; this.name = name; this.team = team; }
    }
    public readonly struct KillInfo
    {
        public readonly int victimActorNr;
        public readonly int killerActorNr;
        public readonly int assisterActorNr;
        public readonly string weaponId;
        public readonly string victimName;
        public readonly string killerName;
        public readonly string assisterName;
        public readonly int victimTeam;
        public readonly int killerTeam;
        public readonly int assisterTeam;
        public readonly AssistInfo[] assists;

        public KillInfo(int victimActorNr, int killerActorNr, int assisterActorNr, string victimName, string killerName, string assisterName, int victimTeam, int killerTeam, int assisterTeam, string weaponId = "", AssistInfo[] assists = null)
        {
            this.weaponId = weaponId;
            this.victimActorNr = victimActorNr;
            this.killerActorNr = killerActorNr;
            this.assisterActorNr = assisterActorNr;
            this.victimName = victimName;
            this.killerName = killerName;
            this.assisterName = assisterName;
            this.victimTeam = victimTeam;
            this.killerTeam = killerTeam;
            this.assisterTeam = assisterTeam;
            this.assists = assists ?? (assisterActorNr != 0 && assisterActorNr != -1
                ? new[] { new AssistInfo(assisterActorNr, assisterName, assisterTeam) } : Array.Empty<AssistInfo>());
        }
    }

    public static event Action<KillInfo> OnKilled;
    public readonly struct DamageInfo
    {
        public readonly PlayerHealth victim;
        public readonly int amount;
        public readonly Vector3 force;
        public readonly Vector3 point;
        public readonly int killerActorNr;
        public readonly int killerBotViewId;

        public DamageInfo(PlayerHealth victim, int amount, Vector3 force, Vector3 point, int killerActorNr, int killerBotViewId)
        {
            this.victim = victim;
            this.amount = amount;
            this.force = force;
            this.point = point;
            this.killerActorNr = killerActorNr;
            this.killerBotViewId = killerBotViewId;
        }
    }
    /// <summary>Latest fresh damage snapshot on this instance (for local hit feedback).</summary>
    public DamageInfo LastDamage { get; private set; }
    public event Action<DamageInfo> Damaged;
    public static readonly HashSet<PlayerHealth> ActivePlayers = new HashSet<PlayerHealth>();
    [SerializeField, Min(1)] private int maximumHealth = 100;
    [SerializeField] private PlayerDeathController deathController;
    [Header("Regeneration (Battlefield-style)")]
    [SerializeField, Min(0f)] private float regenDelay = 5f;
    [SerializeField, Min(0f)] private float regenPerSecond = 20f;
    [SerializeField, Min(.05f)] private float regenPushInterval = .15f;
    [Header("Assists")]
    [SerializeField, Min(1f)] private float assistWindowSeconds = 10f;
    private readonly AssistTracker recentAttackers = new AssistTracker();
    private double lastDamageTime;
    private double lastHealPushTime;
    private float healPool;
    private CharacterController capsule;
    private int revision;
    private bool snapshotSeen;
    private int registeredViewId;
    public int MaximumHealth => maximumHealth;
    public PlayerHitboxes Hitboxes { get; private set; }
    public int CurrentHealth { get; private set; }
    /// <summary>Unscaled time of the latest spawn (object creation). Used to hide fresh spawns from the radar.</summary>
    public float SpawnedAt { get; private set; }
    public bool IsDead => CurrentHealth <= 0 || (deathController != null && deathController.IsDead);
    private string PropertyKey => "hp/" + photonView.ViewID;
    private struct Sample { public double time; public Vector3 bottom, top; public float radius; }
    private readonly Sample[] history = new Sample[64];
    private int historyCount;
    private int historyNext;

    private void Awake()
    {
        capsule = GetComponent<CharacterController>();
        deathController ??= GetComponent<PlayerDeathController>();
        CurrentHealth = maximumHealth;
        Hitboxes = GetComponent<PlayerHitboxes>();
        if (Hitboxes == null) Hitboxes = gameObject.AddComponent<PlayerHitboxes>();
    }
    public override void OnEnable() { base.OnEnable(); ActivePlayers.Add(this); SpawnedAt = Time.unscaledTime; }
    public override void OnDisable() { ActivePlayers.Remove(this); base.OnDisable(); }
    private void Start() { registeredViewId = photonView.ViewID; lastDamageTime = NetworkTime; ReadSnapshot(); snapshotSeen = true; Record(NetworkTime); }
    private void LateUpdate() => Record(NetworkTime);

    private void Update()
    {
        // Only the damage authority regenerates; everyone else follows via room snapshots.
        if (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) return;
        if (IsDead || CurrentHealth >= maximumHealth || regenPerSecond <= 0f) { healPool = 0f; return; }
        double now = NetworkTime;
        if (now - lastDamageTime < regenDelay) return;
        healPool += regenPerSecond * Time.deltaTime;
        if (healPool < 1f || now - lastHealPushTime < regenPushInterval) return;
        int amount = Mathf.Min(Mathf.FloorToInt(healPool), maximumHealth - CurrentHealth);
        if (amount <= 0) { healPool = 0f; return; }
        healPool -= amount;
        lastHealPushTime = now;
        int nextRevision = revision + 1;
        ApplySnapshot(nextRevision, CurrentHealth + amount, Vector3.zero, Vector3.zero, -1);
        if (PhotonNetwork.InRoom)
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { PropertyKey, new object[] { nextRevision, CurrentHealth, Vector3.zero, Vector3.zero, -1 } } });
    }
    private static double NetworkTime => PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;

    private Sample CurrentCapsule(double time)
    {
        capsule ??= GetComponent<CharacterController>();
        Vector3 scale = transform.lossyScale;
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float halfAxis = Mathf.Max(0f, capsule.height * Mathf.Abs(scale.y) * .5f - radius);
        Vector3 center = transform.TransformPoint(capsule.center);
        return new Sample { time = time, bottom = center - transform.up * halfAxis, top = center + transform.up * halfAxis, radius = radius };
    }
    private void Record(double time)
    {
        if (historyCount > 0 && time - history[(historyNext - 1 + history.Length) % history.Length].time < 1d / 60d) return;
        history[historyNext] = CurrentCapsule(time);
        historyNext = (historyNext + 1) % history.Length;
        historyCount = Mathf.Min(historyCount + 1, history.Length);
    }
    public void GetCapsule(double time, out Vector3 bottom, out Vector3 top, out float radius)
    {
        var sample = CurrentCapsule(time);
        if (historyCount > 0)
        {
            int oldest = (historyNext - historyCount + history.Length) % history.Length;
            sample = history[oldest];
            for (int i = 1; i < historyCount; i++)
            {
                var next = history[(oldest + i) % history.Length];
                if (next.time >= time)
                {
                    float blend = (float)System.Math.Clamp((time - sample.time) / System.Math.Max(.000001, next.time - sample.time), 0, 1);
                    sample.bottom = Vector3.Lerp(sample.bottom, next.bottom, blend);
                    sample.top = Vector3.Lerp(sample.top, next.top, blend);
                    sample.radius = Mathf.Lerp(sample.radius, next.radius, blend);
                    break;
                }
                sample = next;
            }
        }
        bottom = sample.bottom; top = sample.top; radius = sample.radius;
    }

    public void ApplyMasterDamage(int amount, Vector3 force, Vector3 point, Player killer = null, int killerBotViewId = 0, string weaponId = "")
    {
        if (!ConquestMatch.CombatAllowed || (PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) || IsDead || amount <= 0) return;
        if (TeamSafeZone.Protects(this, TeamSafeZone.AttackerTeam(killer, killerBotViewId))) return;
        int nextHealth = Mathf.Max(0, CurrentHealth - amount);
        int nextRevision = revision + 1;
        int killerActorNr = killer != null ? killer.ActorNumber : -1;
        lastDamageTime = NetworkTime;
        healPool = 0f;
        int attackerId = killerBotViewId > 0 ? -killerBotViewId : killerActorNr;
        int victimId = BotController.IsBot(this) ? -photonView.ViewID : photonView.OwnerActorNr;
        recentAttackers.Record(attackerId, victimId, TeamSafeZone.AttackerTeam(killer, killerBotViewId), BotController.TeamOf(this), NetworkTime);
        int[] assisters = nextHealth == 0 ? recentAttackers.Collect(attackerId, victimId, NetworkTime, assistWindowSeconds) : Array.Empty<int>();
        int assisterActorNr = assisters.Length > 0 ? assisters[0] : -1;
        if (nextHealth == 0) recentAttackers.Clear();
        ApplySnapshot(nextRevision, nextHealth, force, point, killerActorNr, assisterActorNr, killerBotViewId, weaponId, assisters);
        if (PhotonNetwork.InRoom)
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { PropertyKey, new object[] { nextRevision, nextHealth, force, point, killerActorNr, assisterActorNr, killerBotViewId, weaponId ?? "", assisters } } });
    }
    private void ApplySnapshot(int nextRevision, int hp, Vector3 force, Vector3 point, int killerActorNr = -1, int assisterActorNr = -1, int killerBotViewId = 0, string weaponId = "", int[] assisters = null)
    {
        if (nextRevision <= revision) return;
        bool wasAlive = CurrentHealth > 0;
        // First snapshot only syncs state for late joiners; it is not fresh damage.
        bool fresh = snapshotSeen || !PhotonNetwork.InRoom;
        int previousHealth = CurrentHealth;
        revision = nextRevision;
        snapshotSeen = true;
        CurrentHealth = Mathf.Clamp(hp, 0, maximumHealth);
        if (CurrentHealth >= maximumHealth) recentAttackers.Clear();
        int taken = previousHealth - CurrentHealth;
        if (taken > 0 && fresh)
        {
            LastDamage = new DamageInfo(this, taken, force, point, killerActorNr, killerBotViewId);
            try { Damaged?.Invoke(LastDamage); } catch (Exception e) { Debug.LogException(e); }
        }
        if (CurrentHealth == 0 && wasAlive)
        {
            // Initial state for late joiners must not replay rewards or old kill notifications.
            if (fresh)
                try { OnKilled?.Invoke(BuildKillInfo(killerActorNr, assisterActorNr, killerBotViewId, weaponId, assisters)); } catch (Exception e) { Debug.LogException(e); }
            deathController?.ApplyNetworkDeath(force, point);
        }
    }
    private void ReadSnapshot()
    {
        if (!PhotonNetwork.InRoom) return;
        if (!(PhotonNetwork.CurrentRoom.CustomProperties[PropertyKey] is object[] state)) return;
        if (state.Length >= 6 &&
            state[0] is int version6 && state[1] is int hp6 && state[2] is Vector3 force6 && state[3] is Vector3 point6 &&
            state[4] is int killer6 && state[5] is int assister6)
        {
            ApplySnapshot(version6, hp6, force6, point6, killer6, assister6, state.Length >= 7 && state[6] is int botView ? botView : 0, state.Length >= 8 ? state[7] as string ?? "" : "", state.Length >= 9 ? state[8] as int[] : null);
            return;
        }
        if (state.Length == 5 &&
            state[0] is int version5 && state[1] is int hp5 && state[2] is Vector3 force5 && state[3] is Vector3 point5 && state[4] is int killerActorNr)
        {
            ApplySnapshot(version5, hp5, force5, point5, killerActorNr, -1);
            return;
        }
        // Backward compatibility with rooms written before killer tracking.
        if (state.Length == 4 &&
            state[0] is int version && state[1] is int hp && state[2] is Vector3 force && state[3] is Vector3 point)
            ApplySnapshot(version, hp, force, point, -1, -1);
    }
    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged.ContainsKey(PropertyKey)) ReadSnapshot();
    }
    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        // New authority restarts the regen delay conservatively instead of healing instantly.
        lastDamageTime = NetworkTime;
        healPool = 0f;
        ReadSnapshot();
    }

    private KillInfo BuildKillInfo(int killerActorNr, int assisterActorNr, int killerBotViewId = 0, string weaponId = "", int[] assisters = null)
    {
        int victimActorNr = photonView != null ? photonView.OwnerActorNr : -1;
        Player victimPlayer = photonView != null ? photonView.Owner : null;
        Player killerPlayer = null;
        Player assisterPlayer = null;
        if (PhotonNetwork.InRoom)
        {
            if (killerActorNr > 0) killerPlayer = PhotonNetwork.CurrentRoom.GetPlayer(killerActorNr);
            if (assisterActorNr > 0) assisterPlayer = PhotonNetwork.CurrentRoom.GetPlayer(assisterActorNr);
        }
        string victimName = DisplayName(victimPlayer, victimActorNr);
        var victimBot = GetComponent<BotController>();
        if (victimBot != null) { victimName = victimBot.DisplayName; victimActorNr = -photonView.ViewID; }
        string killerName = DisplayName(killerPlayer, killerActorNr);
        var killerBot = killerBotViewId > 0 ? PhotonView.Find(killerBotViewId)?.GetComponent<BotController>() : null;
        if (killerBot != null) { killerName = killerBot.DisplayName; killerActorNr = -killerBotViewId; }
        string assisterName = assisterActorNr > 0 ? DisplayName(assisterPlayer, assisterActorNr) : "";
        var contributors = new List<AssistInfo>();
        foreach (int actor in assisters ?? (assisterActorNr != -1 && assisterActorNr != 0 ? new[] { assisterActorNr } : Array.Empty<int>()))
        {
            if (actor < -1)
            {
                var bot = PhotonView.Find(-actor)?.GetComponent<BotController>();
                if (bot != null) contributors.Add(new AssistInfo(actor, bot.DisplayName, bot.Team));
            }
            else if (actor > 0)
            {
                var player = PhotonNetwork.InRoom ? PhotonNetwork.CurrentRoom.GetPlayer(actor) : null;
                if (player != null) contributors.Add(new AssistInfo(actor, DisplayName(player, actor), TeamOf(player)));
            }
        }
        if (contributors.Count > 0) { assisterActorNr = contributors[0].actorNr; assisterName = contributors[0].name; }
        return new KillInfo(victimActorNr, killerActorNr, assisterActorNr, victimName, killerName, assisterName,
            victimBot != null ? victimBot.Team : TeamOf(victimPlayer), killerBot != null ? killerBot.Team : TeamOf(killerPlayer),
            contributors.Count > 0 ? contributors[0].team : TeamOf(assisterPlayer), weaponId, contributors.ToArray());
    }

    private static string DisplayName(Player player, int actorNr)
    {
        if (player != null && !string.IsNullOrEmpty(player.NickName)) return player.NickName;
        return actorNr > 0 ? $"Player {actorNr}" : "World";
    }

    private static int TeamOf(Player player)
    {
        if (player != null && player.CustomProperties.TryGetValue("team", out object value) && value is int team)
            return team;
        return 0;
    }

    private void OnDestroy()
    {
        ActivePlayers.Remove(this);
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && registeredViewId != 0)
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { "hp/" + registeredViewId, null } });
    }
}
