using System;
using System.IO;
using Photon.Pun;
using UnityEditor;
using UnityEngine;

/// <summary>Opt-in live Photon smoke check; leaves the joined room and Play Mode afterwards.</summary>
[InitializeOnLoad]
public static class ValidateRoomBrowserOnline
{
    private const string Request = "Temp/validate-room-browser-online.request";
    private const string Report = "Documentation/StartMenu/online-validation.txt";
    private const string Key = "CounterMine.OnlineRoomValidation";
    private static double deadline;
    private static int step;

    static ValidateRoomBrowserOnline() => EditorApplication.update += Poll;

    private static void Poll()
    {
        if (EditorApplication.isCompiling) return;
        if (File.Exists(Request) && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            File.Delete(Request);
            SessionState.SetBool(Key, true);
            step = 0;
            deadline = 0;
            EditorApplication.EnterPlaymode();
            return;
        }
        if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying) return;
        try
        {
            if (deadline == 0) deadline = EditorApplication.timeSinceStartup + 120;
            if (EditorApplication.timeSinceStartup > deadline) throw new Exception("Timed out at step " + step);
            var lobby = UnityEngine.Object.FindFirstObjectByType<LobbyManager>();
            if (lobby == null) return;
            if (step == 0)
            {
                lobby.BrowseRooms();
                step = 1; return;
            }
            if (step == 1)
            {
                if (!lobby.RoomsReady) return;
                lobby.QuickJoin();
                step = 2; return;
            }
            if (step == 2)
            {
                if (!PhotonNetwork.InRoom) return;
                if (PhotonNetwork.OfflineMode) throw new Exception("Quick play entered an offline room");
                var room = PhotonNetwork.CurrentRoom;
                File.WriteAllText(Report, $"PASS: connected to Photon lobby, quick play entered online room {room.Name} ({room.PlayerCount}/{room.MaxPlayers}).\n");
                PhotonNetwork.LeaveRoom();
                step = 3; return;
            }
            if (PhotonNetwork.InRoom) return;
            SessionState.SetBool(Key, false);
            EditorApplication.ExitPlaymode();
        }
        catch (Exception e)
        {
            File.WriteAllText(Report, "FAIL at step " + step + ": " + e + "\nStatus: " +
                (UnityEngine.Object.FindFirstObjectByType<LobbyManager>()?.ConnectionStatus ?? "no lobby") +
                "\nPhoton state: " + PhotonNetwork.NetworkClientState + ", offline=" + PhotonNetwork.OfflineMode +
                ", in room=" + PhotonNetwork.InRoom + "\n");
            Debug.LogException(e);
            SessionState.SetBool(Key, false);
            if (PhotonNetwork.InRoom) PhotonNetwork.LeaveRoom();
            EditorApplication.ExitPlaymode();
        }
    }
}
