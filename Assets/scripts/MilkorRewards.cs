using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>Master-owned reward ledger, persisted in room properties across respawns/master changes.</summary>
public sealed class MilkorRewards : MonoBehaviourPunCallbacks
{
    public const int RequiredKills = 10;
    public const int Capacity = 6;
    private static MilkorRewards instance;
    private readonly Dictionary<int, MilkorRewardState> pending = new Dictionary<int, MilkorRewardState>();
    private static int Round => ConquestMatch.Instance?.State?.Round ?? 0;
    private static string Key(int actor) => "milkor/reward/" + actor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() => instance = null;
    public static void Ensure()
    {
        if (instance != null) return;
        instance = new GameObject("Milkor Rewards").AddComponent<MilkorRewards>();
        DontDestroyOnLoad(instance.gameObject);
    }
    public override void OnEnable() { base.OnEnable(); PlayerHealth.OnKilled += Record; }
    public override void OnDisable() { PlayerHealth.OnKilled -= Record; base.OnDisable(); }
    public override void OnJoinedRoom() => pending.Clear();
    public override void OnLeftRoom() => pending.Clear();
    public override void OnDisconnected(DisconnectCause cause) => pending.Clear();
    public override void OnMasterClientSwitched(Player player) => pending.Clear();
    public static MilkorRewardState Read(int actor)
    {
        Ensure();
        if (!PhotonNetwork.InRoom) return new MilkorRewardState(Round);
        if (PhotonNetwork.IsMasterClient && instance.pending.TryGetValue(actor, out var cached) && cached.Round == Round) return cached;
        return MilkorRewardState.Read(PhotonNetwork.CurrentRoom.CustomProperties[Key(actor)], Round);
    }
    private static void Publish(int actor, MilkorRewardState state)
    {
        instance.pending[actor] = state;
        PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { [Key(actor)] = state.Pack() });
    }
    private void Record(PlayerHealth.KillInfo info)
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || !ConquestMatch.CombatAllowed ||
            !RadarSkill.CountsAsKill(info, info.killerActorNr)) return;
        var killer = PhotonNetwork.CurrentRoom.GetPlayer(info.killerActorNr);
        if (!(killer?.CustomProperties[NetworkWeaponPresentation.SkillLoadoutKey] is string[] skills) ||
            !System.Array.Exists(skills, id => id == MilkorSkill.WeaponId)) return;
        var state = Read(info.killerActorNr);
        state.RecordKill();
        Publish(info.killerActorNr, state);
    }
    public static bool Activate(int actor, int viewId)
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return false;
        var state = Read(actor);
        if (!state.Activate(viewId)) return false;
        Publish(actor, state); return true;
    }
    public static bool SpendRound(int actor, int viewId)
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return false;
        var state = Read(actor);
        if (!state.Spend(viewId)) return false;
        Publish(actor, state); return true;
    }
}

public sealed class MilkorRewardState
{
    public int Round { get; private set; }
    public int Kills { get; private set; }
    public int Charges { get; private set; }
    public int Ammo { get; private set; }
    public int ViewId { get; private set; }
    public MilkorRewardState(int round) { Round = round; }
    public void RecordKill() { if (++Kills >= MilkorRewards.RequiredKills) { Kills = 0; Charges++; } }
    public bool Activate(int viewId)
    {
        if (viewId <= 0) return false;
        if (ViewId == viewId && Ammo > 0) return true;
        if (Charges <= 0) return false;
        Charges--; Ammo = MilkorRewards.Capacity; ViewId = viewId; return true;
    }
    public bool Spend(int viewId)
    {
        if (ViewId != viewId || Ammo <= 0) return false;
        Ammo--; return true;
    }
    public int[] Pack() => new[] { Round, Kills, Charges, Ammo, ViewId };
    public static MilkorRewardState Read(object value, int round)
    {
        if (!(value is int[] data) || data.Length != 5 || data[0] != round) return new MilkorRewardState(round);
        return new MilkorRewardState(round) { Kills = Mathf.Clamp(data[1], 0, 9), Charges = Mathf.Max(0, data[2]),
            Ammo = Mathf.Clamp(data[3], 0, 6), ViewId = data[4] };
    }
}
