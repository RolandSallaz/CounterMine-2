using System.Runtime.InteropServices;
using UnityEngine;
using YG;

/// <summary>The optional PluginYG language module is not required by this bridge.</summary>
public sealed class YandexLanguage : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern string CounterMineLanguage_js();
#endif
    private float nextRead;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        var root = new GameObject("CounterMineLanguage");
        DontDestroyOnLoad(root);
        root.AddComponent<YandexLanguage>();
    }

    private void OnEnable() { YG2.onGetSDKData += Read; Read(); }
    private void OnDisable() => YG2.onGetSDKData -= Read;
    private void Update()
    {
        // Also handles SDK initialization after Unity and language simulation changes.
        if (Time.unscaledTime < nextRead) return;
        nextRead = Time.unscaledTime + 1f;
        Read();
    }
    private void Read()
    {
#if UNITY_EDITOR
        GameLocalization.SetLanguage(YG2.infoYG.Simulation.language);
#elif UNITY_WEBGL
        GameLocalization.SetLanguage(CounterMineLanguage_js());
#else
        GameLocalization.SetLanguage(Application.systemLanguage == SystemLanguage.Russian ? "ru" : "en");
#endif
    }
}
