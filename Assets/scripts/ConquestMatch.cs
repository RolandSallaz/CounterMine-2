using System;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>One room snapshot survives late joins and master migration. No per-player capture RPCs.</summary>
public sealed class ConquestMatch : MonoBehaviourPunCallbacks
{
    public const string StateKey = "conquest/state";
    public static ConquestMatch Instance { get; private set; }
    public static double Now => PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;
    public static bool CombatAllowed => !PhotonNetwork.InRoom ||
        (Instance != null && Instance.State != null && Instance.State.Playing(Now));
    [Serializable] public sealed class Site
    {
        public string label;
        public Vector3 position;
        public float radius = 5.5f;
        public bool Contains(Vector3 feet) => Mathf.Abs(feet.y - position.y) < 1.8f &&
            new Vector2(feet.x-position.x, feet.z-position.z).sqrMagnitude <= radius*radius;
    }
    [SerializeField] private Site[] sites = {
        new Site { label = "A", position = new Vector3(-28, 0, 23) },
        new Site { label = "B", position = new Vector3(4, 4, 0) },
        new Site { label = "C", position = new Vector3(28, 0, -23) }
    };
    public Site[] Sites => sites;
    public ConquestRules State { get; private set; }
    private readonly int[] blue = new int[3], orange = new int[3];
    private double nextTick, nextPublish;
    private int observedRound;
    private bool resetPlayers;
    private readonly LineRenderer[] rings = new LineRenderer[3];
    public static void Ensure()
    {
        if (Instance != null) return;
        var go = new GameObject("Conquest Match");
        go.AddComponent<ConquestMatch>();
        DontDestroyOnLoad(go);
    }
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this; DontDestroyOnLoad(gameObject);
    }
    private void Start()
    {
        var material = Resources.Load<Material>("VFX/CaptureRing");
        for (int i = 0; i < 3; i++)
        {
            var go = new GameObject("Capture " + sites[i].label);
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>(); rings[i] = line;
            line.sharedMaterial = material; line.useWorldSpace = true; line.loop = true;
            line.positionCount = 64; line.widthMultiplier = .08f;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            for (int n = 0; n < 64; n++)
            {
                float angle = n * Mathf.PI * 2 / 64;
                line.SetPosition(n, sites[i].position + new Vector3(Mathf.Cos(angle)*sites[i].radius,.07f,Mathf.Sin(angle)*sites[i].radius));
            }
        }
        if (PhotonNetwork.InRoom) OnJoinedRoom();
    }
    public override void OnJoinedRoom()
    {
        ReadSnapshot();
        if (State == null && PhotonNetwork.IsMasterClient) BeginRound(1);
    }
    public override void OnLeftRoom() { State = null; observedRound = 0; resetPlayers = false; }
    public override void OnDisconnected(DisconnectCause cause) => OnLeftRoom();
    public override void OnMasterClientSwitched(Player player)
    {
        ReadSnapshot();
        if (!player.IsLocal) return;
        if (State == null) BeginRound(1);
        else State.SimulatedAt = Math.Min(Now, State.EndsAt);
        nextTick = nextPublish = 0;
    }
    public override void OnRoomPropertiesUpdate(Hashtable changed)
    {
        if (!changed.ContainsKey(StateKey)) return;
        // Keep the master's in-flight simulation; its own acknowledged snapshots can be older.
        if (!PhotonNetwork.IsMasterClient || State == null) ReadSnapshot();
    }
    private void ReadSnapshot()
    {
        if (!PhotonNetwork.InRoom) return;
        var value = ConquestRules.Read(PhotonNetwork.CurrentRoom.CustomProperties[StateKey]);
        if (value == null || (State != null && value.Round < State.Round)) return;
        State = value;
        ObserveRound();
    }
    private void ObserveRound()
    {
        if (observedRound == State.Round) return;
        resetPlayers = observedRound > 0;
        observedRound = State.Round;
        if (resetPlayers && RadarSkill.Instance != null) RadarSkill.Instance.ResetRound();
    }
    private void BeginRound(int number)
    {
        State = new ConquestRules(number, Now);
        ObserveRound();
        var changes = new Hashtable { [StateKey] = State.Pack() };
        MatchScore.ResetRound(changes);
        PhotonNetwork.CurrentRoom.SetCustomProperties(changes);
        nextPublish = Now + .25;
    }
    private void Update()
    {
        for (int i = 0; i < 3; i++)
            if (rings[i] != null)
            {
                rings[i].enabled = PhotonNetwork.InRoom && State != null && State.Playing(Now);
                if (State == null) continue;
                Color color = State.Contested[i] ? new Color(1,.83f,.35f,.85f) : State.Owner[i] == 1 ?
                    new Color(.25f,.73f,1,.8f) : State.Owner[i] == 2 ? new Color(1,.49f,.24f,.8f) : new Color(.8f,.85f,.9f,.7f);
                rings[i].startColor = rings[i].endColor = color;
            }
        if (!PhotonNetwork.InRoom) return;
        if (resetPlayers) { resetPlayers = false; RespawnRoundPlayers(); }
        if (!PhotonNetwork.IsMasterClient || State == null) return;
        double now = Now;
        if (now >= State.EndsAt + ConquestRules.Intermission) { BeginRound(State.Round + 1); return; }
        if (now < nextTick) return;
        nextTick = now + .1;
        Array.Clear(blue, 0, 3); Array.Clear(orange, 0, 3);
        foreach (var actor in PlayerHealth.ActivePlayers)
        {
            if (actor == null || actor.IsDead || !actor.isActiveAndEnabled) continue;
            int team = BotController.TeamOf(actor);
            if (team != 1 && team != 2) continue;
            for (int i = 0; i < 3; i++)
                if (sites[i].Contains(actor.transform.position)) (team == 1 ? blue : orange)[i]++;
        }
        State.Step(now, blue, orange);
        if (now >= nextPublish)
        {
            nextPublish = now + .25;
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [StateKey] = State.Pack() });
        }
    }
    private void RespawnRoundPlayers()
    {
        var lobby = FindFirstObjectByType<LobbyManager>();
        PlayerHealth local = null;
        foreach (var actor in FindObjectsByType<PlayerHealth>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var view = actor.GetComponent<PhotonView>();
            if (view == null) continue;
            if (BotController.IsBot(actor))
            {
                if (PhotonNetwork.IsMasterClient) PhotonNetwork.Destroy(actor.gameObject);
            }
            else if (view.IsMine) local = actor;
        }
        if (lobby != null && local != null) lobby.RespawnPlayer(local.gameObject);
    }
    public bool TryBotObjective(int team, int slot, Vector3 position, out Vector3 destination)
    {
        destination = default;
        if (State == null || !State.Playing(Now)) return false;
        int chosen = -1;
        for (int n = 0; n < 3; n++)
        {
            int i = (Math.Abs(slot) + n) % 3;
            if (State.Owner[i] != team || State.Contested[i]) { chosen = i; break; }
        }
        if (chosen < 0) return false;
        destination = sites[chosen].position + new Vector3((slot % 2 == 0 ? 1 : -1) * 1.2f, 0, 1.2f);
        return true;
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }
    private void OnDrawGizmosSelected()
    {
        if (sites == null) return;
        foreach (var site in sites)
        {
            if (site == null) continue;
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(site.position+Vector3.up*.9f,new Vector3(site.radius*2,1.8f,site.radius*2));
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() => Instance = null;
}
