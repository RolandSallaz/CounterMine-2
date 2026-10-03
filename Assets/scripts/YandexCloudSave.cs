using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>Cloud reads must succeed before the profile can be edited. JS keeps an account-scoped durable write queue.</summary>
public static class YandexCloudSave
{
    public static string State { get; private set; } = "loading";
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void CloudSaveSet_js(string key, string json);
    [DllImport("__Internal")] private static extern void CloudSaveGet_js(string key, string obj, string method, int request);
    [DllImport("__Internal")] private static extern void CloudSaveResolve_js(string key, int useLocal);
    [DllImport("__Internal")] private static extern void YandexPlayerName_js(string obj, string method);
#endif
    public static bool IsCloudPlatform =>
#if UNITY_WEBGL && !UNITY_EDITOR
        true;
#else
        false;
#endif
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { State="loading"; }
    public static void SaveJson(string key,string json)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        try { CloudSaveSet_js(key,json); }
        catch(Exception) { State="retrying"; }
#else
        PlayerPrefs.SetString(key,json);PlayerPrefs.Save();State="saved";
#endif
    }
    public static void LoadJson(string key,string defaultValue,Action<string> callback)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Receiver.Instance.BeginLoad(key,defaultValue,callback);
#else
        State="saved";callback?.Invoke(PlayerPrefs.GetString(key,defaultValue));
#endif
    }
    public static void ResolveConflict(bool useLocal)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        if(State=="conflict")CloudSaveResolve_js(YandexPlayerData.SaveKey,useLocal?1:0);
#endif
    }
    public static void SaveObject<T>(string key,T data)=>SaveJson(key,JsonUtility.ToJson(data));
    public static void LoadObject<T>(string key,Action<T> callback) where T:new()
    {
        LoadJson(key,"",json=>
        {
            T value;
            try { value=string.IsNullOrEmpty(json)?new T():JsonUtility.FromJson<T>(json); }
            catch(Exception) { State="invalid";return; }
            if(value==null){State="invalid";return;}
            callback?.Invoke(value);
        });
    }
    public static void RequestPlayerName(Action<string> callback)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        Receiver.Instance.BeginName(callback);
#else
        callback?.Invoke("");
#endif
    }
    [Serializable] private sealed class Reply { public int request; public bool ok; public string value,error; }
    [Preserve] private sealed class Receiver:MonoBehaviour
    {
        private static Receiver instance;
        public static Receiver Instance
        {
            get
            {
                if(instance==null){var go=new GameObject("YandexCloudSave");DontDestroyOnLoad(go);instance=go.AddComponent<Receiver>();}
                return instance;
            }
        }
        private Action<string> load,name;
        private string key,fallback;
        private int request;
        private Coroutine readLoop;
        public void BeginLoad(string nextKey,string defaultValue,Action<string> callback)
        {
            if(readLoop!=null)StopCoroutine(readLoop);
            key=nextKey;fallback=defaultValue;load=callback;State="loading";
            readLoop=StartCoroutine(ReadLoop());
        }
        private IEnumerator ReadLoop()
        {
            while(load!=null)
            {
                if(State=="conflict"){yield return new WaitForSecondsRealtime(.5f);continue;}
                request++;
#if UNITY_WEBGL && !UNITY_EDITOR
                try { CloudSaveGet_js(key,gameObject.name,nameof(OnCloudData),request); }
                catch(Exception) { State="retrying"; }
#endif
                yield return new WaitForSecondsRealtime(20f);
                if(load!=null&&State!="conflict")State="retrying";
            }
            readLoop=null;
        }
        [Preserve] public void OnCloudData(string json)
        {
            Reply reply;
            try {reply=JsonUtility.FromJson<Reply>(json);}catch(Exception){State="retrying";return;}
            if(reply==null||reply.request!=request||load==null)return;
            if(!reply.ok){State=reply.error=="conflict"?"conflict":"retrying";return;}
            var callback=load;load=null;
            callback.Invoke(string.IsNullOrEmpty(reply.value)?fallback:reply.value);
        }
        [Preserve] public void OnSaveStatus(string state) { State=state; }
        public void BeginName(Action<string> callback)
        {
            name=callback;
#if UNITY_WEBGL && !UNITY_EDITOR
            try {YandexPlayerName_js(gameObject.name,nameof(OnPlayerName));}catch(Exception){OnPlayerName("");}
#endif
            StartCoroutine(NameTimeout());
        }
        private IEnumerator NameTimeout(){yield return new WaitForSecondsRealtime(15);OnPlayerName("");}
        [Preserve] public void OnPlayerName(string value){var callback=name;name=null;callback?.Invoke(value??"");}
    }
}
