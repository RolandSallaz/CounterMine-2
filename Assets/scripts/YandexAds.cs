using System;
using System.Runtime.InteropServices;
using Photon.Pun;
using UnityEngine;
using UnityEngine.Scripting;
using UnityEngine.UI;
using YG;

/// <summary>Fullscreen requests at menu startup, first battle entry and local deaths.</summary>
public sealed class YandexAds : MonoBehaviour
{
    private static YandexAds instance;
    public static bool Busy { get; private set; }
    private bool startupRequested, entryRequested;
    private int request;
    private GameObject overlay;
    private Text label;
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void CounterMineAd_js(int request);
#endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { instance = null; Busy = false; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if (instance != null) return;
        var go = new GameObject("YandexAds"); DontDestroyOnLoad(go); instance = go.AddComponent<YandexAds>();
    }
    private void OnEnable()
    {
        PlayerHealth.OnKilled += OnKilled;
#if InterstitialAdv_yg
        YG2.onCloseInterAdv += PluginClosed;
        YG2.onErrorInterAdv += PluginClosed;
#endif
    }
    private void OnDisable()
    {
        PlayerHealth.OnKilled -= OnKilled;
#if InterstitialAdv_yg
        YG2.onCloseInterAdv -= PluginClosed;
        YG2.onErrorInterAdv -= PluginClosed;
#endif
        Busy = false;
    }
    private void PluginClosed() => OnAdEvent(request + ":closed");
    private void Update()
    {
        if (label != null) label.text = GameLocalization.Language == "ru" ? "РЕКЛАМА…" : "ADVERTISEMENT…";
        if (!startupRequested && YG2.isSDKEnabled && !PlatformLifecycle.InputBlocked && !YG2.isPauseGame &&
            FindFirstObjectByType<StartMenuScreen>() != null)
        { startupRequested = true; Show(); }
    }
    public static void FirstEntry()
    {
        if (instance == null || instance.entryRequested) return;
        instance.entryRequested = true;
        // If entry precedes SDK readiness, do not show a late startup ad during combat.
        instance.startupRequested = true;
        instance.Show();
    }
    private void OnKilled(PlayerHealth.KillInfo info)
    {
        if (PhotonNetwork.InRoom && PhotonNetwork.LocalPlayer != null &&
            info.victimActorNr == PhotonNetwork.LocalPlayer.ActorNumber && info.victimActorNr > 0)
            StartCoroutine(AfterDeath());
    }
    private System.Collections.IEnumerator AfterDeath()
    {
        // Let death and deployment UI finish before the ad can appear.
        yield return null;
        Show();
    }
    private void Show()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if (Busy || !YG2.isSDKEnabled || PlatformLifecycle.InputBlocked || YG2.isPauseGame) return;
        Busy = true; request++;
        SetOverlay(true);
        PlatformLifecycle.RefreshPause();
        YG2.GameplayStop();
        try
        {
#if InterstitialAdv_yg
            YG2.InterstitialAdvShow();
#else
            CounterMineAd_js(request);
#endif
        }
        catch (Exception) { OnAdEvent(request + ":closed"); }
#endif
    }
    [Preserve] public void OnAdEvent(string message)
    {
        var parts = message.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int id) || id != request || !Busy) return;
        if (parts[1] != "closed") return;
        Busy = false; SetOverlay(false); PlatformLifecycle.RefreshPause();
        // Cursor stays free: the player explicitly chooses when to deploy/resume.
    }
    private void SetOverlay(bool visible)
    {
        if (overlay == null && visible)
        {
            overlay = new GameObject("Ad overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            overlay.transform.SetParent(transform, false);
            var canvas = overlay.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000;
            var panel = new GameObject("Block input", typeof(RectTransform), typeof(Image)); panel.transform.SetParent(overlay.transform, false);
            var rect = panel.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(.02f,.03f,.04f,.96f);
            var text = new GameObject("Status", typeof(RectTransform), typeof(Text)); text.transform.SetParent(panel.transform, false);
            rect = text.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            label = text.GetComponent<Text>(); label.font = GameUIStyle.Font; label.fontSize = 24; label.alignment = TextAnchor.MiddleCenter; label.color = Color.white;
            label.text = GameLocalization.Language == "ru" ? "РЕКЛАМА…" : "ADVERTISEMENT…";
        }
        if (overlay != null) overlay.SetActive(visible);
    }
}
