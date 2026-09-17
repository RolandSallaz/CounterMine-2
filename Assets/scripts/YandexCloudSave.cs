using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>Thin Yandex cloud-saves bridge (player.getData/setData, synced across devices).
/// Outside WebGL builds it falls back to PlayerPrefs, so everything is testable in the editor.
/// Async reads return via SendMessage to an internal receiver; a timeout guards missing SDK responses.</summary>
public static class YandexCloudSave
{
    private const float ResponseTimeout = 6f;

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void CloudSaveSet_js(string key, string json);
    [DllImport("__Internal")] private static extern void CloudSaveGet_js(string key, string callbackObject, string callbackMethod);
    [DllImport("__Internal")] private static extern void YandexPlayerName_js(string callbackObject, string callbackMethod);
#endif

    public static bool IsCloudPlatform =>
#if UNITY_WEBGL && !UNITY_EDITOR
        true;
#else
        false;
#endif

    public static void SaveJson(string key, string json)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try { CloudSaveSet_js(key, json); }
        catch (Exception e) { Debug.LogWarning($"[CloudSave] set failed: {e.Message}"); }
#else
        PlayerPrefs.SetString(key, json);
        PlayerPrefs.Save();
#endif
    }

    public static void LoadJson(string key, string defaultValue, Action<string> callback)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Receiver.Instance.BeginLoad(key, defaultValue, callback);
#else
        callback?.Invoke(PlayerPrefs.GetString(key, defaultValue));
#endif
    }

    public static void SaveObject<T>(string key, T data) => SaveJson(key, JsonUtility.ToJson(data));

    public static void LoadObject<T>(string key, Action<T> callback) where T : new()
    {
        LoadJson(key, "", json =>
        {
            if (string.IsNullOrEmpty(json)) { callback?.Invoke(new T()); return; }
            try { callback?.Invoke(JsonUtility.FromJson<T>(json)); }
            catch (Exception e)
            {
                Debug.LogWarning($"[CloudSave] parse failed for '{key}': {e.Message}");
                callback?.Invoke(new T());
            }
        });
    }

    /// <summary>Public Yandex player name (empty outside Yandex WebGL or when unavailable).</summary>
    public static void RequestPlayerName(Action<string> callback)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Receiver.Instance.BeginNameRequest(callback);
#else
        callback?.Invoke("");
#endif
    }

    private sealed class Receiver : MonoBehaviour
    {
        private static Receiver instance;
        public static Receiver Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("YandexCloudSave");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<Receiver>();
                }
                return instance;
            }
        }

        private Action<string> pendingLoad;
        private string pendingDefault = "";
        private Action<string> pendingName;
        private int loadToken;
        private int nameToken;

        public void BeginLoad(string key, string defaultValue, Action<string> callback)
        {
            pendingLoad = callback;
            pendingDefault = defaultValue;
            int token = ++loadToken;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { CloudSaveGet_js(key, gameObject.name, nameof(OnCloudData)); }
            catch (Exception e)
            {
                Debug.LogWarning($"[CloudSave] get failed: {e.Message}");
                FinishLoad(token, "");
                return;
            }
            StartCoroutine(WaitTimeout(token, true));
#else
            FinishLoad(token, defaultValue);
#endif
        }

        public void BeginNameRequest(Action<string> callback)
        {
            pendingName = callback;
            int token = ++nameToken;
#if UNITY_WEBGL && !UNITY_EDITOR
            try { YandexPlayerName_js(gameObject.name, nameof(OnPlayerName)); }
            catch (Exception e)
            {
                Debug.LogWarning($"[CloudSave] name request failed: {e.Message}");
                FinishName(token, "");
                return;
            }
            StartCoroutine(WaitTimeout(token, false));
#else
            FinishName(token, "");
#endif
        }

        // Called from JS via SendMessage. Must stay public with a single string argument.
        public void OnCloudData(string json) => FinishLoad(loadToken, json ?? "");
        public void OnPlayerName(string playerName) => FinishName(nameToken, playerName ?? "");

        private IEnumerator WaitTimeout(int token, bool isLoad)
        {
            yield return new WaitForSecondsRealtime(ResponseTimeout);
            if (isLoad) FinishLoad(token, "");
            else FinishName(token, "");
        }

        private void FinishLoad(int token, string json)
        {
            if (token != loadToken || pendingLoad == null) return;
            var callback = pendingLoad;
            pendingLoad = null;
            loadToken++;
            callback.Invoke(string.IsNullOrEmpty(json) ? pendingDefault : json);
        }

        private void FinishName(int token, string playerName)
        {
            if (token != nameToken || pendingName == null) return;
            var callback = pendingName;
            pendingName = null;
            nameToken++;
            callback.Invoke(playerName);
        }
    }
}
