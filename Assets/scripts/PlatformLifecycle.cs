using Photon.Pun;
using UnityEngine;
using UnityEngine.Scripting;
using YG;

/// <summary>Independent SDK, browser-focus and menu pause reasons. Online peers keep simulating while local input is blocked.</summary>
[DefaultExecutionOrder(-1000)]
public sealed class PlatformLifecycle:MonoBehaviour
{
    private static PlatformLifecycle instance;
    private bool sdkPaused, browserFocused=true, heldPause, previousAudio;
    private bool heldTime;
    private float previousTime;
    private bool ready;
    public static bool InputBlocked => !Application.isFocused || DesktopControls.ModalOpen || YandexAds.Busy ||
        (instance!=null&&(instance.sdkPaused||!instance.browserFocused));
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        if(instance!=null)return;
        var go=new GameObject("PlatformLifecycle");DontDestroyOnLoad(go);instance=go.AddComponent<PlatformLifecycle>();
    }
    private void OnEnable(){instance=this;sdkPaused=YG2.isPauseGame;browserFocused=Application.isFocused;YG2.onPauseGame+=OnSdkPause;}
    private void OnDisable(){YG2.onPauseGame-=OnSdkPause;Restore();if(instance==this)instance=null;}
    private void OnSdkPause(bool paused){sdkPaused=paused;ApplyPause();}
    private void OnApplicationFocus(bool focused){browserFocused=focused;ApplyPause();}
    private void OnApplicationPause(bool paused){browserFocused=!paused&&Application.isFocused;ApplyPause();}
    [Preserve] public void SetBrowserFocus(string focused){browserFocused=focused=="true";ApplyPause();}
    public static void RefreshPause() { if (instance != null) instance.ApplyPause(); }
    private void Update()
    {
        ApplyPause();
        if(!ready&&YG2.isSDKEnabled&&FindFirstObjectByType<StartMenuScreen>()!=null)
        {
            // The menu is interactive; profile loading/recovery is shown separately.
            Canvas.ForceUpdateCanvases();YG2.GameReadyAPI();ready=true;
        }
        bool playing=false;
        if(!InputBlocked&&Cursor.lockState==CursorLockMode.Locked&&ConquestMatch.CombatAllowed)
            foreach(var player in PlayerHealth.ActivePlayers)
                if(player!=null&&!player.IsDead&&!BotController.IsBot(player)&&player.photonView.IsMine){playing=true;break;}
        if(YG2.isSDKEnabled){if(playing)YG2.GameplayStart();else YG2.GameplayStop();}
    }
    private void ApplyPause()
    {
        bool pause=InputBlocked;
        if(pause&&!heldPause){previousAudio=AudioListener.pause;heldPause=true;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
        if(pause)
        {
            AudioListener.pause=true;
            if((!PhotonNetwork.InRoom||PhotonNetwork.OfflineMode)&&!heldTime){previousTime=Time.timeScale;Time.timeScale=0;heldTime=true;}
            if(heldTime&&PhotonNetwork.InRoom&&!PhotonNetwork.OfflineMode){Time.timeScale=previousTime;heldTime=false;}
        }
        else Restore();
    }
    private void Restore()
    {
        if(heldPause){AudioListener.pause=previousAudio;heldPause=false;}
        if(heldTime){Time.timeScale=previousTime;heldTime=false;}
    }
}
