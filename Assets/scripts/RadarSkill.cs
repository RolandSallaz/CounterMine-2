using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.InputSystem;

/// <summary>Local match reward. Lives outside the player so respawns retain progress.</summary>
[DefaultExecutionOrder(750)]
public sealed class RadarSkill : MonoBehaviourPunCallbacks
{
    public const int RequiredKills = 5;
    public const float Duration = 15f;
    public const int SlotCount = 3;
    public enum SkillKind { None, Radar, Milkor }
    private readonly SkillKind[] slots = { SkillKind.Radar, SkillKind.Milkor, SkillKind.None };
    public SkillKind SkillAt(int slot) => slot >= 0 && slot < SlotCount ? slots[slot] : SkillKind.None;
    public static RadarSkill Instance { get; private set; }
    private readonly RadarChargeProgress charge = new RadarChargeProgress();
    public int Progress => charge.Kills;
    public int Charges => charge.Charges;
    public float Remaining => charge.Remaining(Time.unscaledTimeAsDouble);
    public bool Active => Remaining > 0f;
    private float nextScan;
    private PlayerHealth owner;
    private Camera ownerCamera;
    private Material material;
    private readonly Dictionary<PlayerHealth, List<Proxy>> targets = new Dictionary<PlayerHealth, List<Proxy>>();
    private readonly List<PlayerHealth> removed = new List<PlayerHealth>();
    private sealed class Proxy { public SkinnedMeshRenderer source; public MeshRenderer overlay; public Mesh mesh; public bool ready; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() => Instance = null;

    public static void Bind(PlayerHealth player, Camera camera)
    {
        if (player == null || BotController.IsBot(player) || (PhotonNetwork.InRoom && !player.photonView.IsMine)) return;
        if (Instance == null)
        {
            Instance = new GameObject("Local Radar Skill").AddComponent<RadarSkill>();
            DontDestroyOnLoad(Instance.gameObject);
        }
        Instance.owner = player; Instance.ownerCamera = camera; Instance.nextScan = 0f;
    }
    public override void OnEnable()
    {
        base.OnEnable();
        PlayerHealth.OnKilled += HandleKill;
        RenderPipelineManager.beginCameraRendering += BeginCamera;
        RenderPipelineManager.endCameraRendering += EndCamera;
    }
    public override void OnDisable()
    {
        PlayerHealth.OnKilled -= HandleKill;
        RenderPipelineManager.beginCameraRendering -= BeginCamera;
        RenderPipelineManager.endCameraRendering -= EndCamera;
        ClearTargets(); base.OnDisable();
    }
    private void HandleKill(PlayerHealth.KillInfo info)
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.LocalPlayer == null) return;
        if (!charge.Record(info, PhotonNetwork.LocalPlayer.ActorNumber, Time.unscaledTimeAsDouble)) return;
        nextScan = 0f;
        GameAudio.Effect("UI/click", Vector3.zero, .5f, 1f, true);
    }
    public static bool CountsAsKill(PlayerHealth.KillInfo info, int localActor) =>
        localActor > 0 && info.killerActorNr == localActor && info.victimActorNr != localActor &&
        info.victimActorNr != 0 && info.victimActorNr != -1 && info.killerTeam != 0 && info.victimTeam != 0 &&
        info.killerTeam != info.victimTeam;

    private bool CanShow => Active && PhotonNetwork.InRoom && owner != null && !owner.IsDead &&
        owner.photonView.IsMine && !BotController.IsBot(owner) && ownerCamera != null && ownerCamera.enabled;
    private bool Enemy(PlayerHealth candidate) => candidate != null && candidate != owner && !candidate.IsDead &&
        candidate.gameObject.activeInHierarchy && BotController.TeamOf(owner) != 0 && BotController.TeamOf(candidate) != 0 &&
        BotController.TeamOf(candidate) != BotController.TeamOf(owner) &&
        Time.unscaledTime - candidate.SpawnedAt >= 1f;

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (Application.isFocused && Cursor.lockState == CursorLockMode.Locked && keyboard != null)
        {
            if (keyboard.digit3Key.wasPressedThisFrame) TryActivateSlot(0);
            if (keyboard.digit4Key.wasPressedThisFrame) TryActivateSlot(1);
            if (keyboard.digit5Key.wasPressedThisFrame) TryActivateSlot(2);
        }
        if (!CanShow) { if (targets.Count != 0) ClearTargets(); return; }
        if (Time.unscaledTime < nextScan) return;
        nextScan = Time.unscaledTime + .25f;
        removed.Clear();
        foreach (var pair in targets) if (!Enemy(pair.Key)) removed.Add(pair.Key);
        foreach (var key in removed) { DestroyProxies(targets[key]); targets.Remove(key); }
        foreach (var candidate in PlayerHealth.ActivePlayers)
            if (Enemy(candidate) && !targets.ContainsKey(candidate)) AddTarget(candidate);
    }
    public bool TryActivateSlot(int slot)
    {
        if (!PhotonNetwork.InRoom || owner == null || owner.IsDead || !owner.photonView.IsMine ||
            BotController.IsBot(owner) || ownerCamera == null || !ownerCamera.enabled) return false;
        switch (SkillAt(slot))
        {
            case SkillKind.Milkor:
                return owner.GetComponent<MilkorSkill>()?.TryActivate() == true;
            case SkillKind.Radar:
                if (!charge.TryActivate(Time.unscaledTimeAsDouble)) return false;
                nextScan = 0;
                GameAudio.Effect("UI/click", Vector3.zero, .5f, 1f, true);
                return true;
            default: return false;
        }
    }
    private void AddTarget(PlayerHealth target)
    {
        var skeleton = target.GetComponent<PlayerRagdollController>()?.SkeletonRoot;
        if (skeleton == null) return;
        if (material == null)
        {
            var shader = Resources.Load<Shader>("VFX/RadarHighlight");
            if (shader == null) return;
            material = new Material(shader) { name = "Local Radar Highlight" };
        }
        var proxies = new List<Proxy>();
        foreach (var source in skeleton.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (source.sharedMesh == null) continue;
            var go = new GameObject("Radar " + source.name);
            go.transform.SetParent(transform, false);
            go.layer = source.gameObject.layer;
            var mesh = new Mesh { name = "Radar Pose " + source.name }; mesh.MarkDynamic();
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var overlay = go.AddComponent<MeshRenderer>();
            overlay.enabled = false;
            overlay.allowOcclusionWhenDynamic = false;
            overlay.shadowCastingMode = ShadowCastingMode.Off; overlay.receiveShadows = false;
            overlay.lightProbeUsage = LightProbeUsage.Off; overlay.reflectionProbeUsage = ReflectionProbeUsage.Off;
            var materials = new Material[source.sharedMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            overlay.sharedMaterials = materials;
            proxies.Add(new Proxy { source = source, overlay = overlay, mesh = mesh });
        }
        targets[target] = proxies;
    }
    private void LateUpdate()
    {
        if (!CanShow) return;
        foreach (var pair in targets)
        foreach (var proxy in pair.Value)
        {
            proxy.ready = false;
            if (!Enemy(pair.Key) || proxy.source == null || proxy.overlay == null || !proxy.source.enabled ||
                !proxy.source.gameObject.activeInHierarchy) continue;
            // Bake after both the animation clock and hand IK; no second skinning pass at camera render time.
            BakeSilhouette(proxy.source, proxy.mesh, proxy.overlay.transform);
            proxy.ready = true;
        }
    }
    private static void BakeSilhouette(SkinnedMeshRenderer source, Mesh mesh, Transform output)
    {
        source.BakeMesh(mesh, false);
        output.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        output.localScale = source.transform.lossyScale;
    }
    private void BeginCamera(ScriptableRenderContext context, Camera camera)
    {
        bool show = CanShow && camera == ownerCamera;
        foreach (var pair in targets)
        foreach (var proxy in pair.Value)
        {
            if (proxy.overlay == null) continue;
            bool visible = show && proxy.ready && Enemy(pair.Key) && proxy.source != null && proxy.source.enabled && proxy.source.gameObject.activeInHierarchy;
            proxy.overlay.enabled = visible;
        }
    }
    private void EndCamera(ScriptableRenderContext context, Camera camera)
    {
        foreach (var pair in targets) foreach (var proxy in pair.Value) if (proxy.overlay != null) proxy.overlay.enabled = false;
    }
    private static void DestroyProxies(List<Proxy> proxies)
    {
        foreach (var proxy in proxies)
        {
            if (proxy.overlay != null) { proxy.overlay.enabled = false; Destroy(proxy.overlay.gameObject); }
            if (proxy.mesh != null) Destroy(proxy.mesh);
        }
    }
    private void ClearTargets()
    {
        foreach (var pair in targets) DestroyProxies(pair.Value);
        targets.Clear();
    }
    private void ResetMatch(bool forgetOwner = true)
    {
        charge.Reset(); ClearTargets();
        if (forgetOwner) { owner = null; ownerCamera = null; }
    }
    public void ResetRound() => ResetMatch(false);
    public override void OnLeftRoom() => ResetMatch();
    public override void OnJoinedRoom() => ResetMatch(false);
    public override void OnDisconnected(DisconnectCause cause) => ResetMatch();
    private void OnDestroy()
    {
        ClearTargets(); if (material != null) Destroy(material);
        if (Instance == this) Instance = null;
    }
}

/// <summary>Match progress is independent of player life and render objects.</summary>
public sealed class RadarChargeProgress
{
    public int Kills { get; private set; }
    public int Charges { get; private set; }
    private double expiresAt;
    public float Remaining(double now) => Mathf.Max(0f, (float)(expiresAt - now));
    public bool Record(PlayerHealth.KillInfo info, int localActor, double now)
    {
        if (!RadarSkill.CountsAsKill(info, localActor)) return false;
        if (++Kills < RadarSkill.RequiredKills) return false;
        Kills = 0; Charges++; return true;
    }
    public bool TryActivate(double now)
    {
        if (Charges <= 0 || Remaining(now) > 0) return false;
        Charges--; expiresAt = now + RadarSkill.Duration; return true;
    }
    public void Reset() { Kills = 0; Charges = 0; expiresAt = 0; }
}
