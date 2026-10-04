using UnityEngine;
using YG;

/// <summary>Connects PluginYG2 language events to live game text.</summary>
public sealed class YandexLanguage : MonoBehaviour
{
#if UNITY_EDITOR
    private string simulationLanguage;
#endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        var root = new GameObject("Game language / PluginYG2");
        DontDestroyOnLoad(root);
        root.AddComponent<YandexLanguage>();
    }
    private void OnEnable()
    {
        YG2.onSwitchLang += GameLocalization.ApplyLanguage;
        YG2.onGetSDKData += Read;
        Read();
    }
    private void OnDisable()
    {
        YG2.onSwitchLang -= GameLocalization.ApplyLanguage;
        YG2.onGetSDKData -= Read;
    }
    private void Read()
    {
#if UNITY_EDITOR
        simulationLanguage = YG2.infoYG.Simulation.language;
#endif
        GameLocalization.ApplyLanguage(YG2.lang);
    }
#if UNITY_EDITOR
    private void Update()
    {
        if (simulationLanguage == YG2.infoYG.Simulation.language) return;
        simulationLanguage = YG2.infoYG.Simulation.language;
        YG2.GetLanguage();
    }
#endif
}
