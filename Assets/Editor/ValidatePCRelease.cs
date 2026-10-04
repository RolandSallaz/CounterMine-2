using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Opt-in editor smoke test. Never builds a player or joins a room.</summary>
[InitializeOnLoad]
public static class ValidatePCRelease
{
    private const string Request = "Temp/validate-pc-release.request", Key = "CounterMine.PCReleaseCheck";
    private static int step;
    private static double next, deadline, pausedMatchTime;
    static ValidatePCRelease() { EditorApplication.update += Poll; }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Poll()
    {
        if (EditorApplication.isCompiling) return;
        if (File.Exists(Request) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(Request); SessionState.SetBool(Key, true); EditorApplication.EnterPlaymode(); return;
        }
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + 1;
        const string folder = "Documentation/ReleaseChecks";
        try
        {
            if (step == 0) { deadline = EditorApplication.timeSinceStartup + 40; step++; return; }
            Check(EditorApplication.timeSinceStartup < deadline, "Timeout at step " + step);
            var controls = UnityEngine.Object.FindFirstObjectByType<DesktopControls>();
            var lifecycle = UnityEngine.Object.FindFirstObjectByType<PlatformLifecycle>();
            if (controls == null || lifecycle == null) return;
            if (step <= 5) Check(!Photon.Pun.PhotonNetwork.IsConnected, "Unexpected network connection");
            if (step == 1)
            {
                GameLocalization.SetLanguage("ru");
                controls.GetComponentsInChildren<Button>().Single(b => b.name == "Help").onClick.Invoke();
                step++; return;
            }
            if (step == 2)
            {
                Canvas.ForceUpdateCanvases();
                Check(DesktopControls.ModalOpen, "Help did not open");
                Check(PlatformLifecycle.InputBlocked && AudioListener.pause && Time.timeScale == 0, "Offline help must pause input, audio and simulation");
                var body = controls.GetComponentsInChildren<Text>().Single(t => t.name == "Instructions");
                Check(controls.GetComponentsInChildren<Text>(true).All(t => t.GetComponent<YG.LanguageLegacy.LanguageYG>() != null), "Controls text lacks YGames translator");
                YG.YG2.SwitchLanguage("en"); controls.SendMessage("Update");
                Check(body.text.StartsWith("CONTROLS"), "Controls did not translate through YGames");
                YG.YG2.SwitchLanguage("ru"); controls.SendMessage("Update");
                Check(body.preferredHeight <= body.rectTransform.rect.height + 1, "Controls text overflows vertically");
                Directory.CreateDirectory(folder); ScreenCapture.CaptureScreenshot(folder + "/pc-controls.png");
                step++; return;
            }
            if (step == 3)
            {
                controls.GetComponentsInChildren<Button>().Single(b => b.name == "Credits").onClick.Invoke();
                step++; return;
            }
            if (step == 4)
            {
                Check(controls.GetComponentsInChildren<Text>().Any(t => t.text.Contains("CC BY 4.0")), "Music attribution missing");
                controls.GetComponentsInChildren<Button>().Single(b => b.name == "Close").onClick.Invoke();
                step++; return;
            }
            if (step == 5)
            {
                Check(!DesktopControls.ModalOpen, "Help did not close");
                lifecycle.SetBrowserFocus("false");
                Check(PlatformLifecycle.InputBlocked && AudioListener.pause && Time.timeScale == 0, "Browser blur did not pause offline game");
                lifecycle.SetBrowserFocus("true");
                if (Application.isFocused) Check(!AudioListener.pause && Time.timeScale == 1, "Focus did not restore previous state");
                Check(YandexPlayerData.IsLoaded, "Editor profile failed to load");
                lifecycle.SetBrowserFocus("false");
                UnityEngine.Object.FindFirstObjectByType<LobbyManager>().StartGame(true);
                step++; return;
            }
            Check(Photon.Pun.PhotonNetwork.OfflineMode, "Solo check must never contact Photon servers");
            if (!Photon.Pun.PhotonNetwork.InRoom || ConquestMatch.Instance.State == null) return;
            if (step == 6) { pausedMatchTime = ConquestMatch.Now; step++; return; }
            Check(Math.Abs(ConquestMatch.Now - pausedMatchTime) < .001, "Solo match clock advanced while paused");
            Check(Math.Abs(ConquestMatch.Now - Time.timeAsDouble) < .001, "Solo clock must use scaled gameplay time");
            File.WriteAllText(folder + "/pc-editor-validation.txt", "PASS: menu starts offline; help opens/closes; text fits; help and blur pause local input/audio/simulation; music attribution present; editor profile loads; solo Photon room clock stays frozen while paused. Focus restoration checked when editor is focused. No player build or external network connection.\n");
            Finish();
        }
        catch (Exception error) { Directory.CreateDirectory(folder); File.WriteAllText(folder + "/pc-editor-validation.txt", "FAIL: " + error); Debug.LogException(error); Finish(); }
    }
    private static void Finish() { SessionState.SetBool(Key, false); EditorApplication.ExitPlaymode(); }
}
