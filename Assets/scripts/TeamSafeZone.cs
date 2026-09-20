using System.Collections.Generic;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Scene-authored team sanctuary. Solid to enemies, permeable to its own team.</summary>
[RequireComponent(typeof(BoxCollider))]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class TeamSafeZone : MonoBehaviour
{
    [SerializeField, Range(1, 2)] private int team = 1;
    [SerializeField, Min(.1f)] private float revealDistance = 5f;
    private static readonly HashSet<TeamSafeZone> Active = new HashSet<TeamSafeZone>();
    private BoxCollider box;
    private GameObject visual;
    private Dictionary<PlayerHealth, CharacterController> players = new Dictionary<PlayerHealth, CharacterController>();
    private List<PlayerHealth> stale = new List<PlayerHealth>();
    public int Team => team;
    public BoxCollider Volume => box != null ? box : GetComponent<BoxCollider>();

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        box.isTrigger = false;
        // Ignore generic ground/placement probes; weapon queries explicitly include all layers.
        gameObject.layer = 2;
        var material = Resources.Load<Material>("VFX/SafeZoneBarrier");
        if (material == null) return;
        visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Proximity Barrier";
        var collider = visual.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        visual.transform.SetParent(transform, false);
        visual.transform.localPosition = box.center;
        visual.transform.localScale = box.size;
        var renderer = visual.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_Tint", team == 1 ? new Color(.15f, .65f, 1f, 1f) : new Color(1f, .32f, .12f, 1f));
        properties.SetFloat("_RevealDistance", revealDistance);
        renderer.SetPropertyBlock(properties);
    }

    private void OnEnable()
    {
        box = GetComponent<BoxCollider>();
        box.enabled = true;
        Active.Add(this);
        if (visual != null) visual.SetActive(true);
        Update();
    }

    private void Update()
    {
        foreach (var player in PlayerHealth.ActivePlayers)
        {
            if (player == null) continue;
            if (!players.TryGetValue(player, out var controller))
            {
                controller = player.GetComponent<CharacterController>();
                players[player] = controller;
            }
            if (controller == null || !controller.enabled) continue;
            bool friendly = BotController.TeamOf(player) == team;
            SetPassage(controller, friendly);
        }
        stale.Clear();
        foreach (var pair in players)
            if (pair.Key == null || !PlayerHealth.ActivePlayers.Contains(pair.Key)) stale.Add(pair.Key);
        foreach (var player in stale) players.Remove(player);
    }

    private void SetPassage(CharacterController controller, bool friendly)
    {
        if (Physics.GetIgnoreCollision(box, controller) != friendly)
            Physics.IgnoreCollision(box, controller, friendly);
    }

    private void OnDisable()
    {
        Active.Remove(this);
        if (box != null) box.enabled = false;
        if (visual != null) visual.SetActive(false);
        players.Clear();
    }

    public bool Contains(Vector3 point)
    {
        var volume = Volume;
        Vector3 local = transform.InverseTransformPoint(point) - volume.center;
        Vector3 half = volume.size * .5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    public static bool ContainsAny(Vector3 point)
    {
        foreach (var zone in Active) if (zone != null && zone.Contains(point)) return true;
        return false;
    }

    public static bool IsEnemyArea(Vector3 point, int visitorTeam)
    {
        foreach (var zone in Active)
            if (zone != null && zone.team != visitorTeam && zone.Contains(point)) return true;
        return false;
    }

    public static bool BlocksWeapons(PlayerHealth player)
    {
        if (player == null) return false;
        var capsule = player.GetComponent<CharacterController>();
        Vector3 center = capsule != null && capsule.enabled ? capsule.bounds.center : player.transform.position + Vector3.up;
        return ContainsAny(center) || ContainsAny(player.transform.position);
    }

    public static bool Protects(PlayerHealth victim, int attackerTeam)
    {
        if (victim == null) return false;
        int victimTeam = BotController.TeamOf(victim);
        if (attackerTeam == victimTeam && attackerTeam != 0) return false;
        var capsule = victim.GetComponent<CharacterController>();
        Vector3 center = capsule != null && capsule.enabled ? capsule.bounds.center : victim.transform.position + Vector3.up;
        foreach (var zone in Active)
            if (zone != null && zone.team == victimTeam && (zone.Contains(center) || zone.Contains(victim.transform.position))) return true;
        return false;
    }

    public static int AttackerTeam(Photon.Realtime.Player owner, int botViewId = 0)
    {
        if (botViewId > 0)
        {
            var view = PhotonView.Find(botViewId);
            var bot = view != null ? view.GetComponent<BotController>() : null;
            if (bot != null) return bot.Team;
        }
        return owner != null && owner.CustomProperties["team"] is int value ? value : 0;
    }

    private void OnDrawGizmos()
    {
        var volume = Volume;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = team == 1 ? new Color(.15f, .65f, 1f, .8f) : new Color(1f, .32f, .12f, .8f);
        Gizmos.DrawWireCube(volume.center, volume.size);
        Gizmos.matrix = Matrix4x4.identity;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => Active.Clear();
}
