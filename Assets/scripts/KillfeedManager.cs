using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CS-style killfeed. Normal scene-driven UI: assign <see cref="feedRoot"/> container
/// (top-right corner) and <see cref="entryPrefab"/> in the inspector.
/// Listens to <see cref="PlayerHealth.OnKilled"/> and spawns entries that fade out.
/// No runtime canvas/prefab creation — all visuals come from the scene/prefab.
/// </summary>
public sealed class KillfeedManager : MonoBehaviour
{
    public static KillfeedManager Instance { get; private set; }

    [Header("Scene references")]
    [SerializeField] private RectTransform feedRoot;
    [SerializeField] private KillfeedEntry entryPrefab;

    [Header("Feed")]
    [SerializeField, Min(1)] private int maxEntries = 5;
    [SerializeField, Min(1f)] private float entryLifetime = 6f;
    [SerializeField, Min(0f)] private float fadeDuration = 0.8f;

    [Header("Team colors")]
    [SerializeField] private Color team1Color = new Color(0.38f, 0.68f, 1f);
    [SerializeField] private Color team2Color = new Color(1f, 0.75f, 0.3f);
    [SerializeField] private Color neutralColor = Color.white;

    private readonly List<KillfeedEntry> entries = new List<KillfeedEntry>();

    /// <summary>Manual posting, e.g. for debug or non-PlayerHealth kills. Requires a scene instance.</summary>
    public static void PostKill(string killerName, string victimName, int killerTeam = 0, int victimTeam = 0)
    {
        if (Instance == null)
        {
            Debug.LogWarning("KillfeedManager: no scene instance. Add KillfeedManager to the HUD canvas.");
            return;
        }
        Instance.AddEntry(killerName, victimName, killerTeam, victimTeam);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("KillfeedManager: duplicate instance, destroying the new one.", this);
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void OnEnable() => PlayerHealth.OnKilled += HandleKill;
    private void OnDisable() => PlayerHealth.OnKilled -= HandleKill;

    private void HandleKill(PlayerHealth.KillInfo info)
    {
        AddEntry(info.killerName, info.victimName, info.killerTeam, info.victimTeam,
            info.killerActorNr, info.victimActorNr, info.assisterName, info.assisterTeam, GameAudio.WeaponName(info.weaponId));
    }

    public void AddEntry(string killerName, string victimName, int killerTeam, int victimTeam,
        int killerActorNr = -2, int victimActorNr = -2, string assisterName = null, int assisterTeam = 0, string weaponName = "Unknown")
    {
        if (feedRoot == null || entryPrefab == null)
        {
            Debug.LogWarning("KillfeedManager: assign Feed Root and Entry Prefab in the inspector.", this);
            return;
        }

        bool suicide = string.IsNullOrEmpty(killerName) || (killerActorNr == -1 || killerActorNr == 0) ||
                       (killerActorNr == victimActorNr && killerActorNr != -2);
        string killerText = !suicide && !string.IsNullOrEmpty(assisterName) ? killerName + " + " + assisterName : killerName;

        var entry = Instantiate(entryPrefab, feedRoot, false);
        entry.transform.SetAsLastSibling();
        entry.Configure(killerText, victimName, TeamColor(killerTeam), TeamColor(victimTeam), suicide, weaponName);

        entries.Add(entry);
        while (entries.Count > Mathf.Max(1, maxEntries))
        {
            var oldest = entries[0];
            entries.RemoveAt(0);
            if (oldest != null) Destroy(oldest.gameObject);
        }

        StartCoroutine(ExpireEntry(entry, entryLifetime));
    }

    private IEnumerator ExpireEntry(KillfeedEntry entry, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (entry == null) yield break;
        var group = entry.Group;
        if (group != null && fadeDuration > 0f)
        {
            float t = 0f;
            while (entry != null && t < fadeDuration)
            {
                t += Time.deltaTime;
                group.alpha = 1f - Mathf.Clamp01(t / fadeDuration);
                yield return null;
            }
        }
        entries.Remove(entry);
        if (entry != null) Destroy(entry.gameObject);
    }

    private Color TeamColor(int team)
    {
        return team == 1 ? team1Color : team == 2 ? team2Color : neutralColor;
    }

    [ContextMenu("Test Kill Entry")]
    private void TestEntry()
    {
        AddEntry("Player 1", "Player 2", 1, 2, 1, 2);
    }
}
