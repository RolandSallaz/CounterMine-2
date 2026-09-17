using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public sealed class BotRoomSpawner : MonoBehaviourPunCallbacks
{
    [SerializeField, Min(1f)] private float corpseLifetime = 5f;
    private float checkAt;
    private readonly System.Collections.Generic.Dictionary<BotController, float> deathTimes = new System.Collections.Generic.Dictionary<BotController, float>();
    public override void OnJoinedRoom() => checkAt = Time.time + 1f;
    public override void OnMasterClientSwitched(Player newMasterClient) => checkAt = Time.time + 1f;
    private void Update()
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient) return;
        SweepCorpses();
        if (Time.time < checkAt) return;
        checkAt = Time.time + 3f;
        if (Resources.Load<GameObject>("Bot") == null) return;
        var bots = FindObjectsByType<BotController>(FindObjectsSortMode.None);
        var occupied = new bool[6];
        int team1 = 0, team2 = 0;
        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (player.CustomProperties["team"] is int t) { if (t == 2) team2++; else team1++; }
        }
        foreach (var bot in bots)
        {
            if (bot.Slot >= 0 && bot.Slot < 6) occupied[bot.Slot] = true;
            if (bot.Team == 2) team2++; else team1++;
        }
        var points = FindObjectsByType<TeamSpawnPoint>(FindObjectsSortMode.None);
        Vector3 fallback = Vector3.zero;
        foreach (var player in PlayerHealth.ActivePlayers) if (player != null && !BotController.IsBot(player)) { fallback = player.transform.position; break; }
        for (int slot = 0; slot < 6; slot++)
        {
            if (occupied[slot]) continue;
            // Each new bot joins the currently weaker side.
            int team = team2 < team1 ? 2 : 1;
            if (team == 2) team2++; else team1++;
            Vector3 origin = fallback;
            foreach (var point in points) if (point.Team == team) { origin = point.transform.position; break; }
            for (int attempt = 0; attempt < 24; attempt++)
            {
                float angle = (slot * 60f + attempt * 37f) * Mathf.Deg2Rad;
                Vector3 probe = origin + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * (5f + attempt * .4f);
                if (!Physics.Raycast(probe + Vector3.up * 5f, Vector3.down, out var ground, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || ground.normal.y < .7f) continue;
                Vector3 position = ground.point + Vector3.up * .08f;
                if (Physics.CheckCapsule(position + Vector3.up * .3f, position + Vector3.up * 1.5f, .26f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                PhotonNetwork.InstantiateRoomObject("Bot", position, Quaternion.Euler(0, slot * 60, 0), 0, new object[] { slot, team });
                break;
            }
        }
    }

    /// <summary>Master removes bot corpses after <see cref="corpseLifetime"/> so slots free up for respawn.
    /// Lives here (not in BotController) because ragdoll disables the bot's own components on death.</summary>
    private void SweepCorpses()
    {
        var bots = FindObjectsByType<BotController>(FindObjectsSortMode.None);
        var seen = new System.Collections.Generic.HashSet<BotController>();
        var expired = new System.Collections.Generic.List<BotController>();
        foreach (var bot in bots)
        {
            if (bot == null) continue;
            seen.Add(bot);
            var health = bot.GetComponent<PlayerHealth>();
            if (health != null && !health.IsDead) continue;
            if (!deathTimes.TryGetValue(bot, out float diedAt)) deathTimes[bot] = Time.time;
            else if (Time.time - diedAt >= corpseLifetime) expired.Add(bot);
        }
        // Forget bots that vanished without us (destroyed externally).
        var forgotten = new System.Collections.Generic.List<BotController>();
        foreach (var pair in deathTimes) if (!seen.Contains(pair.Key)) forgotten.Add(pair.Key);
        foreach (var bot in forgotten) deathTimes.Remove(bot);
        foreach (var corpse in expired)
        {
            deathTimes.Remove(corpse);
            if (corpse != null) PhotonNetwork.Destroy(corpse.gameObject);
        }
    }
}
