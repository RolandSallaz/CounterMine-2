using System;
using System.IO;
using System.Linq;
using Photon.Pun;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Opt-in play-mode check; leaves the authored scene unchanged.</summary>
[InitializeOnLoad]
public static class ValidateStartMenu
{
    private const string Request = "Temp/validate-start-menu.request";
    private const string Key = "CounterMine.StartMenuValidation";
    private const string Folder = "Documentation/StartMenu";
    private static double next, deadline;
    private static int step;
    static ValidateStartMenu() => EditorApplication.update += Poll;

    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); }

    private static void Poll()
    {
        if (EditorApplication.isCompiling) return;
        if (File.Exists(Request) && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(Request);
            SessionState.SetBool(Key, true);
            Directory.CreateDirectory(Folder);
            EditorApplication.EnterPlaymode();
            return;
        }
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        if (EditorApplication.timeSinceStartup < next) return;
        next = EditorApplication.timeSinceStartup + .5;
        try
        {
            var lobby = UnityEngine.Object.FindFirstObjectByType<LobbyManager>();
            if (step == 0)
            {
                deadline = EditorApplication.timeSinceStartup + 45;
                step = 1; return;
            }
            Check(EditorApplication.timeSinceStartup < deadline, "Timed out at step " + step);
            if (step == 1)
            {
                var menu = UnityEngine.Object.FindFirstObjectByType<StartMenuScreen>();
                if (menu == null) return;
                Check(!PhotonNetwork.InRoom && !PhotonNetwork.IsConnected, "Menu connected before mode selection");
                Check(PlayerHealth.ActivePlayers.Count == 0, "Preview registered as a combat player");
                Check(menu.GetComponentsInChildren<Button>().Length == 2, "Expected two mode buttons");
                GameLocalization.SetLanguage("ru");
                Check(menu.GetComponentsInChildren<Text>().Any(t => t.text == "Одиночная игра"), "Russian solo label missing");
                Check(menu.GetComponentsInChildren<Text>().Any(t => t.text == "Мультиплеер"), "Russian multiplayer label missing");
                var actor = GameObject.Find("Display Player");
                Check(actor != null && actor.GetComponentsInChildren<Renderer>().Length > 0, "Display model missing");
                Check(actor.GetComponentsInChildren<MonoBehaviour>().Length == 0, "Display model has gameplay scripts");
                var shader = Resources.Load<Shader>("UI/MenuBackgroundBlur");
                Check(shader != null && !ShaderUtil.ShaderHasError(shader), "Blur shader failed");
                ScreenCapture.CaptureScreenshot(Folder + "/start-menu.png");
                step = 2; return;
            }
            if (step == 2)
            {
                Check(File.Exists(Folder + "/start-menu.png"), "Menu screenshot missing");
                var menu = UnityEngine.Object.FindFirstObjectByType<StartMenuScreen>();
                menu.GetComponentsInChildren<Button>().Single(b => b.name == "Single player").onClick.Invoke();
                step = 3; return;
            }
            if (step == 3)
            {
                Check(PhotonNetwork.OfflineMode && PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient, "Solo room not active");
                Check(UnityEngine.Object.FindFirstObjectByType<StartMenuScreen>() == null, "Menu did not close");
                Check(GameObject.Find("Menu Presentation") == null, "Preview stage leaked into match");
                if (!lobby.CanDeploy || !PlayerHealth.ActivePlayers.Any(p => p != null && BotController.IsBot(p))) return;
                lobby.Deploy(); lobby.Deploy();
                step = 4; return;
            }
            if (step == 4)
            {
                var players = PlayerHealth.ActivePlayers.Where(p => p != null && !BotController.IsBot(p)).ToArray();
                Check(players.Length == 1, "Deploy created duplicate players");
                Check(UnityEngine.Object.FindFirstObjectByType<DeploymentScreen>() == null, "Deployment remained open");
                players[0].GetComponent<PlayerDeathController>().ApplyNetworkDeath(Vector3.zero, players[0].transform.position);
                step = 5; return;
            }
            if (step == 5)
            {
                Check(UnityEngine.Object.FindFirstObjectByType<DeploymentScreen>() != null, "Death did not reopen deployment");
                if (!lobby.CanDeploy) return;
                lobby.Deploy(); step = 6; return;
            }
            Check(PlayerHealth.ActivePlayers.Count(p => p != null && !BotController.IsBot(p) && !p.IsDead) == 1, "Solo respawn failed");
            File.WriteAllText(Folder + "/validation.txt", "PASS: disconnected startup, two localized buttons, visual-only model, blur shader, screenshot, offline room, bot spawning, duplicate-deploy guard, death screen and respawn.\nOnline matchmaking and WebGL need separate live checks.\n");
            Finish();
        }
        catch (Exception e)
        {
            File.WriteAllText(Folder + "/validation.txt", "FAIL at step " + step + "\n" + e);
            Debug.LogException(e); Finish();
        }
    }

    private static void Finish()
    {
        SessionState.SetBool(Key, false); step = 0;
        EditorApplication.ExitPlaymode();
    }
}
