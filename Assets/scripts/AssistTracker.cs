using System;
using System.Collections.Generic;

/// <summary>Tracks every enemy contributor using positive player IDs and negative bot view IDs.</summary>
public sealed class AssistTracker
{
    private readonly Dictionary<int, double> hits = new Dictionary<int, double>();
    public void Record(int attacker, int victim, int attackerTeam, int victimTeam, double time)
    {
        if (attacker == 0 || attacker == -1 || attacker == victim ||
            (attackerTeam != 0 && attackerTeam == victimTeam)) return;
        hits[attacker] = time;
    }
    public int[] Collect(int killer, int victim, double now, double window)
    {
        var result = new List<int>();
        foreach (var pair in hits)
            if (pair.Key != killer && pair.Key != victim && now - pair.Value <= window)
                result.Add(pair.Key);
        result.Sort((a, b) => hits[b].CompareTo(hits[a]));
        return result.Count == 0 ? Array.Empty<int>() : result.ToArray();
    }
    public void Clear() => hits.Clear();
}
