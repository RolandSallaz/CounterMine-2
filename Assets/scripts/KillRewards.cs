using System.Collections.Generic;
using UnityEngine;

/// <summary>Local wallet and notifications derived once from authoritative match totals.</summary>
public static class KillRewards
{
    public const int KillReward = 10;
    public const int AssistReward = 5;
    public static int Kills { get; private set; }
    public static int Deaths { get; private set; }
    public static int Assists { get; private set; }
    public static int Score { get; private set; }
    public readonly struct Notice
    {
        public readonly bool kill;
        public readonly int amount, count;
        public readonly float time;
        public Notice(bool kill, int count, int amount) { this.kill = kill; this.count = count; this.amount = amount; time = Time.unscaledTime; }
    }
    private static readonly Queue<Notice> notices = new Queue<Notice>();
    private static int pendingMoney;
    public static void EnsureSubscribed() => MatchScore.Ensure();
    public static void ResetMatch() { Kills = Deaths = Assists = Score = 0; notices.Clear(); }
    public static void ApplyTotals(int kills, int deaths, int assists, int score, bool award)
    {
        if (award)
        {
            // A repeated or older snapshot must never lower our payout baseline.
            kills = Mathf.Max(Kills, kills);
            deaths = Mathf.Max(Deaths, deaths);
            assists = Mathf.Max(Assists, assists);
            score = Mathf.Max(Score, score);
            Reward(true, Mathf.Max(0, kills - Kills));
            Reward(false, Mathf.Max(0, assists - Assists));
        }
        Kills = kills; Deaths = deaths; Assists = assists; Score = score;
        FlushPendingMoney();
    }
    private static void Reward(bool kill, int count)
    {
        if (count == 0) return;
        int amount = count * (kill ? KillReward : AssistReward);
        pendingMoney += amount;
        notices.Enqueue(new Notice(kill, count, amount));
        while (notices.Count > 8) notices.Dequeue();
    }
    public static bool TryTakeNotice(out Notice notice)
    {
        while (notices.Count > 0)
        {
            notice = notices.Dequeue();
            if (Time.unscaledTime - notice.time < 5f) return true;
        }
        notice = default;
        return false;
    }
    public static void FlushPendingMoney()
    {
        if (pendingMoney <= 0 || !YandexPlayerData.IsLoaded) return;
        int amount = pendingMoney;
        pendingMoney = 0;
        YandexPlayerData.Current.AddMoney(amount);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatic() { ResetMatch(); pendingMoney = 0; }
}
