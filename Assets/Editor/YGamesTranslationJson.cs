using System.Text;
using Newtonsoft.Json.Linq;
using UnityEditor;
using YG.LanguageLegacy;

/// <summary>Package JSON dependency for PluginYG2's firstpass auto-translation component.</summary>
[InitializeOnLoad]
public static class YGamesTranslationJson
{
    static YGamesTranslationJson() => LanguageYG.ParseTranslationResponse = Parse;
    public static string Parse(string response)
    {
        var segments = JArray.Parse(response);
        var result = new StringBuilder();
        foreach (var segment in segments[0])
            if (segment[0] != null) result.Append(segment[0].ToString());
        return result.ToString();
    }
}
