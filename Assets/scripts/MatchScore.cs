using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

/// <summary>Master-owned match totals persisted in room properties, independent of player lives.</summary>
public sealed class MatchScore : MonoBehaviourPunCallbacks
{
    public sealed class Entry
    {
        public string key, name;
        public int team, kills, deaths, assists, score;
        public object[] Pack() => new object[] { name, team, kills, deaths, assists, score };
        public static Entry Read(string key, object value)
        {
            if (!(value is object[] a) || a.Length != 6 || !(a[0] is string name) || !(a[1] is int team) ||
                !(a[2] is int kills) || !(a[3] is int deaths) || !(a[4] is int assists) || !(a[5] is int score)) return null;
            return new Entry { key = key, name = name, team = team, kills = kills, deaths = deaths, assists = assists, score = score };
        }
    }
    private const string Prefix = "score/";
    private static MatchScore instance;
    private readonly Dictionary<string, Entry> authority = new Dictionary<string, Entry>();
    private readonly Dictionary<string, Entry> roster = new Dictionary<string, Entry>();

    public static void Ensure()
    {
        if (instance != null) return;
        instance = new GameObject("Match Score").AddComponent<MatchScore>();
        DontDestroyOnLoad(instance.gameObject);
        if (PhotonNetwork.InRoom) instance.OnJoinedRoom();
    }
    public override void OnEnable() { base.OnEnable(); PlayerHealth.OnKilled += HandleKill; }
    public override void OnDisable() { PlayerHealth.OnKilled -= HandleKill; base.OnDisable(); }
    private void Update() => KillRewards.FlushPendingMoney();
    private void OnDestroy() { if (instance == this) instance = null; }
    public override void OnJoinedRoom()
    {
        LoadAuthority();
        KillRewards.ResetMatch();
        SyncLocal(false);
    }
    public override void OnLeftRoom() { authority.Clear(); KillRewards.ResetMatch(); }
    public override void OnDisconnected(DisconnectCause cause) { authority.Clear(); KillRewards.ResetMatch(); }
    public override void OnMasterClientSwitched(Player next) { if (next.IsLocal) LoadAuthority(); }
    private void LoadAuthority()
    {
        authority.Clear();
        if (!PhotonNetwork.InRoom) return;
        foreach (var pair in PhotonNetwork.CurrentRoom.CustomProperties)
            if (pair.Key is string key && key.StartsWith(Prefix, StringComparison.Ordinal))
            {
                var entry = Entry.Read(key.Substring(Prefix.Length), pair.Value);
                if (entry != null) authority[entry.key] = entry;
            }
    }
    public override void OnRoomPropertiesUpdate(Hashtable changed)
    {
        if (PhotonNetwork.InRoom && changed.ContainsKey(Prefix + "p" + PhotonNetwork.LocalPlayer.ActorNumber)) SyncLocal(true);
    }
    private void SyncLocal(bool award)
    {
        if (!PhotonNetwork.InRoom) return;
        string key = "p" + PhotonNetwork.LocalPlayer.ActorNumber;
        var entry = Entry.Read(key, PhotonNetwork.CurrentRoom.CustomProperties[Prefix + key]);
        if (entry != null) KillRewards.ApplyTotals(entry.kills, entry.deaths, entry.assists, entry.score, award);
    }
    public static bool Rewardable(PlayerHealth.KillInfo info) =>
        info.killerActorNr != 0 && info.killerActorNr != -1 && info.killerActorNr != info.victimActorNr &&
        (info.killerTeam == 0 || info.victimTeam == 0 || info.killerTeam != info.victimTeam);

    public static bool AssistRewardable(PlayerHealth.KillInfo info, PlayerHealth.AssistInfo contributor) =>
        Rewardable(info) && contributor.actorNr != 0 && contributor.actorNr != -1 &&
        contributor.actorNr != info.killerActorNr && contributor.actorNr != info.victimActorNr &&
        (contributor.team == 0 || info.victimTeam == 0 || contributor.team != info.victimTeam);

    private static string Key(int actor)
    {
        if (actor > 0) return "p" + actor;
        if (actor >= -1) return null;
        var view = PhotonView.Find(-actor);
        var bot = view != null ? view.GetComponent<BotController>() : null;
        return bot != null ? "b" + bot.Slot : null;
    }
    private Entry Get(int actor, string name, int team)
    {
        string key = Key(actor);
        if (key == null) return null;
        if (!authority.TryGetValue(key, out var entry)) authority[key] = entry = new Entry { key = key };
        entry.name = name;
        entry.team = team;
        return entry;
    }
    private void HandleKill(PlayerHealth.KillInfo info)
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
        var updates = new Hashtable();
        var victim = Get(info.victimActorNr, info.victimName, info.victimTeam);
        if (victim != null) { victim.deaths++; updates[Prefix + victim.key] = victim.Pack(); }
        if (Rewardable(info))
        {
            var killer = Get(info.killerActorNr, info.killerName, info.killerTeam);
            if (killer != null) { killer.kills++; killer.score += KillRewards.KillReward; updates[Prefix + killer.key] = killer.Pack(); }
            var awarded = new HashSet<int>();
            foreach (var contributor in info.assists ?? Array.Empty<PlayerHealth.AssistInfo>())
            {
                if (!AssistRewardable(info, contributor) || !awarded.Add(contributor.actorNr)) continue;
                var assist = Get(contributor.actorNr, contributor.name, contributor.team);
                if (assist != null) { assist.assists++; assist.score += KillRewards.AssistReward; updates[Prefix + assist.key] = assist.Pack(); }
            }
        }
        if (updates.Count > 0) PhotonNetwork.CurrentRoom.SetCustomProperties(updates);
    }

    public static int Compare(Entry a, Entry b)
    {
        int result = b.score.CompareTo(a.score);
        if (result == 0) result = b.kills.CompareTo(a.kills);
        if (result == 0) result = a.deaths.CompareTo(b.deaths);
        return result != 0 ? result : string.CompareOrdinal(a.key, b.key);
    }
    public static void ReadRoster(List<Entry> output)
    {
        output.Clear();
        if (instance == null || !PhotonNetwork.InRoom) return;
        var rows = instance.roster;
        rows.Clear();
        foreach (var player in PhotonNetwork.PlayerList)
        {
            string key = "p" + player.ActorNumber;
            var row = Entry.Read(key, PhotonNetwork.CurrentRoom.CustomProperties[Prefix + key]) ?? new Entry { key = key };
            row.name = string.IsNullOrWhiteSpace(player.NickName) ? "Player " + player.ActorNumber : player.NickName;
            row.team = player.CustomProperties["team"] is int team ? team : 0;
            rows[key] = row;
        }
        // Bot slots retain their total across death/respawn and the one-second corpse cleanup.
        foreach (var pair in PhotonNetwork.CurrentRoom.CustomProperties)
            if (pair.Key is string key && key.StartsWith(Prefix + "b", StringComparison.Ordinal))
            {
                var row = Entry.Read(key.Substring(Prefix.Length), pair.Value);
                if (row != null) rows[row.key] = row;
            }
        foreach (var player in PlayerHealth.ActivePlayers)
        {
            var bot = player != null ? player.GetComponent<BotController>() : null;
            if (bot == null) continue;
            string key = "b" + bot.Slot;
            if (!rows.TryGetValue(key, out var row)) rows[key] = row = new Entry { key = key };
            row.name = bot.DisplayName; row.team = bot.Team;
        }
        output.AddRange(rows.Values);
        output.Sort(Compare);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() { instance = null; }
}
