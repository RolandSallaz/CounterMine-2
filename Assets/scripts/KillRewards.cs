using Photon.Pun;
using UnityEngine;

/// <summary>Match rewards for the local player: coins for kills/assists plus local K/D/A counters.
/// Subscribes once (idempotent) — hook it from lobby start. Needs no scene objects.</summary>
public static class KillRewards
{
    public const int KillReward = 10;
    public const int AssistReward = 5;

    public static int Kills { get; private set; }
    public static int Deaths { get; private set; }
    public static int Assists { get; private set; }

    private static bool subscribed;

    public static void EnsureSubscribed()
    {
        if (subscribed) return;
        subscribed = true;
        Kills = Deaths = Assists = 0;
        PlayerHealth.OnKilled += HandleKill;
    }

    private static void HandleKill(PlayerHealth.KillInfo info)
    {
        if (!PhotonNetwork.InRoom) return;
        int me = PhotonNetwork.LocalPlayer.ActorNumber;
        bool suicide = info.killerActorNr <= 0 || info.killerActorNr == info.victimActorNr;
        if (!suicide && info.killerActorNr == me)
        {
            Kills++;
            YandexPlayerData.Current.AddMoney(KillReward);
        }
        if (info.victimActorNr == me) Deaths++;
        if (!suicide && info.assisterActorNr > 0 && info.assisterActorNr == me && info.assisterActorNr != info.killerActorNr)
        {
            Assists++;
            YandexPlayerData.Current.AddMoney(AssistReward);
        }
    }
}
