using Photon.Pun;
using UnityEngine;

/// <summary>Local feedback from fresh, authoritative health changes, never predicted traces.</summary>
public static class CombatHitAudio
{
    private static float nextHurt, nextHit;
    private static int hurtVariant, hitVariant;
    private static readonly string[] HurtSounds = { "Combat/hurt", "Combat/hurt1", "Combat/hurt2" };
    private static readonly string[] HitSounds = { "Combat/hit", "Combat/hit1", "Combat/hit2" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { nextHurt = nextHit = 0f; hurtVariant = hitVariant = 0; }

    public static void OnDamage(PlayerHealth.DamageInfo damage)
    {
        var victim = damage.victim;
        // Discard hidden-tab events rather than queueing sounds to play on return.
        if (victim == null || damage.amount <= 0 || PlatformLifecycle.InputBlocked || AudioListener.pause) return;
        float now = Time.unscaledTime;
        bool localVictim = !BotController.IsBot(victim) && (!PhotonNetwork.InRoom || victim.photonView.IsMine);
        if (localVictim)
        {
            if (now < nextHurt) return;
            nextHurt = now + .10f;
            GameAudio.Effect(HurtSounds[hurtVariant++ % HurtSounds.Length], Vector3.zero, .62f, 1f, true);
            return;
        }
        var shooter = PhotonNetwork.LocalPlayer;
        if (!PhotonNetwork.InRoom || shooter == null || damage.killerBotViewId != 0 ||
            damage.killerActorNr != shooter.ActorNumber) return;
        int ourTeam = TeamSafeZone.AttackerTeam(shooter);
        if (ourTeam != 0 && ourTeam == BotController.TeamOf(victim)) return;
        if (now < nextHit) return;
        nextHit = now + .065f;
        GameAudio.Effect(HitSounds[hitVariant++ % HitSounds.Length], Vector3.zero, .48f, 1f, true);
    }
}
