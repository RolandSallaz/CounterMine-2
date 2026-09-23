using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Game text selected by the Yandex SDK locale. Unknown locales use English.</summary>
public static class GameLocalization
{
    [Serializable] public sealed class Entry { public string source, ru, en; }
    [Serializable] private sealed class Catalog { public Entry[] entries; }
    private static Dictionary<string, Entry> entries;
    public static string Language { get; private set; } = "en";
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        entries = null;
        Changed = null;
        Language = "en";
    }

    public static void SetLanguage(string language)
    {
        string next = Normalize(language);
        if (Language == next) return;
        Language = next;
        Changed?.Invoke();
    }

    public static string Normalize(string language) =>
        !string.IsNullOrEmpty(language) && language.Split('-', '_')[0].Equals("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";

    public static string T(string source)
    {
        if (string.IsNullOrEmpty(source)) return source ?? "";
        if (entries == null)
        {
            entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
            var asset = Resources.Load<TextAsset>("GameTranslations");
            var catalog = asset != null ? JsonUtility.FromJson<Catalog>(asset.text) : null;
            if (catalog?.entries != null)
                foreach (var entry in catalog.entries) entries[entry.source] = entry;
        }
        if (!entries.TryGetValue(source, out var item)) return source;
        return Language == "ru" ? item.ru : item.en;
    }

    public static string Format(string source, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, T(source), args);

    // Bind only authored labels. Dynamic HUD values are translated by their owner.
    public static void Bind(Text text, string source)
    {
        if (string.IsNullOrEmpty(source)) { text.text = ""; return; }
        Bind(text, () => T(source));
    }

    public static void Bind(Text text, Func<string> value)
    {
        var binding = text.GetComponent<LocalizedGameText>();
        if (binding == null) binding = text.gameObject.AddComponent<LocalizedGameText>();
        binding.SetValue(value);
    }

    public static void BindHUD(Transform root)
    {
        var staticLabels = new HashSet<string> { "TACTICAL OPERATIONS", "VITALS", "HEALTH", "STAMINA", "01  /  PRIMARY", "RMB  AIM     /     SHIFT  SPRINT" };
        foreach (var text in root.GetComponentsInChildren<Text>(true))
            if (staticLabels.Contains(text.text)) Bind(text, text.text);
    }
}
