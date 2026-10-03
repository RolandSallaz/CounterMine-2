using System;
using System.Collections.Generic;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class LobbyManager : MonoBehaviourPunCallbacks
{
    private const string GameVersion = "CounterMine-0.3-pellets";
    private const string TeamProperty = "team";

    [SerializeField] private string roomPrefix = "CounterMine";
    [SerializeField] private byte maxPlayers = 16;
    [SerializeField] private string playerPrefabResourceName = "Player";
    [SerializeField] private Vector3 fallbackSpawnPosition = new Vector3(0f, 2f, 0f);
    [SerializeField] private Vector3 mapCenter = Vector3.zero;
    [SerializeField] private bool autoJoinOnStart = true;

    private System.Func<string> status = () => GameLocalization.T("Choose a team.");
    private bool playerSpawned;
    private int selectedTeam;
    private readonly Dictionary<string, RoomInfo> rooms = new Dictionary<string, RoomInfo>(StringComparer.Ordinal);
    private enum OnlineAction { None, QuickJoin, JoinRoom, CreateRoom }
    private OnlineAction pendingAction;
    private string pendingRoomName;
    private bool generatedRoomName;
    private int quickJoinRetries;
    private int createRetries;
    private bool browsingRooms;
    private int roomListRevision;
    public int RoomListRevision => roomListRevision;
    public bool RoomsReady => PhotonNetwork.InLobby;
    public IEnumerable<RoomInfo> Rooms => rooms.Values;
    private DeploymentScreen deploymentScreen;
    private StartMenuScreen startMenu;
    private bool modeSelected;
    private bool singlePlayer;
    private GameObject pendingCorpse;
    private float deployAvailableAt;
    public bool CanDeploy => !YandexAds.Busy && !playerSpawned && PhotonNetwork.InRoom && YandexPlayerData.IsLoaded && Time.unscaledTime >= deployAvailableAt;
    public string ConnectionStatus => status();

    private void Awake() => PhotonNetwork.EnsureInitialized();

    private void Start()
    {
        if (GetComponent<BotRoomSpawner>() == null) gameObject.AddComponent<BotRoomSpawner>();
        PhotonNetwork.AutomaticallySyncScene = true;
        ConquestMatch.Ensure();
        KillRewards.EnsureSubscribed();
        YandexPlayerData.Load();
        startMenu = StartMenuScreen.Create(this, Resources.Load<GameObject>(playerPrefabResourceName), GetMenuPosition());
#if UNITY_WEBGL && !UNITY_EDITOR
        YandexCloudSave.RequestPlayerName(playerName =>
        {
            if (!string.IsNullOrEmpty(playerName)) PhotonNetwork.NickName = playerName;
        });
#endif
    }

    private Vector3 GetMenuPosition()
    {
        foreach (var point in FindObjectsByType<TeamSpawnPoint>(FindObjectsSortMode.InstanceID))
            if (point.Team == 1) return point.transform.position;
        return fallbackSpawnPosition;
    }

    public void StartGame(bool offline)
    {
        if (modeSelected) return;
        if (YandexAds.Busy) return;
        if (!offline) { QuickJoin(); return; }
        YandexAds.FirstEntry();
        modeSelected = true;
        singlePlayer = offline;
        if (startMenu != null) { startMenu.gameObject.SetActive(false); Destroy(startMenu.gameObject); }
        ShowDeploymentScreen();
        Connect();
    }

    private void SelectTeam(int team)
    {
        GameAudio.Effect("UI/click", Vector3.zero, .5f, 1f, true);
        selectedTeam = team;
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { TeamProperty, team } });
        status = () => GameLocalization.Format("Выбрана команда {0}. Подключение к Photon...", team);
        Connect();
    }

    public void Connect()
    {
        if (!modeSelected && !browsingRooms) return;
        if (singlePlayer)
        {
            // OfflineMode invokes OnConnectedToMaster synchronously. Bots and match rules
            // then use the same room lifecycle without contacting Photon servers.
            if (!PhotonNetwork.OfflineMode && PhotonNetwork.NetworkClientState != ClientState.Disconnected &&
                PhotonNetwork.NetworkClientState != ClientState.PeerCreated)
            { PhotonNetwork.Disconnect(); return; }
            if (!PhotonNetwork.OfflineMode) PhotonNetwork.OfflineMode = true;
            else if (!PhotonNetwork.InRoom) PhotonNetwork.CreateRoom("Solo");
            return;
        }
        if (PhotonNetwork.IsConnected)
        {
            if (PhotonNetwork.InLobby) RunPendingAction();
            else if (PhotonNetwork.IsConnectedAndReady) PhotonNetwork.JoinLobby(TypedLobby.Default);
            return;
        }
        if (PhotonNetwork.NetworkClientState != ClientState.Disconnected && PhotonNetwork.NetworkClientState != ClientState.PeerCreated) return;

        status = () => GameLocalization.T("Connecting to Photon...");
        PhotonNetwork.GameVersion = GameVersion;
        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        if (!modeSelected && !browsingRooms) return;
        if (singlePlayer) { if (!PhotonNetwork.OfflineMode) Connect(); else PhotonNetwork.CreateRoom("Solo"); return; }
        status = () => GameLocalization.T("Loading rooms...");
        if (!PhotonNetwork.InLobby) PhotonNetwork.JoinLobby(TypedLobby.Default);
        else RunPendingAction();
    }

    public void BrowseRooms()
    {
        if (modeSelected || YandexAds.Busy) return;
        browsingRooms = true;
        pendingAction = OnlineAction.None;
        startMenu?.ShowRoomBrowser(true);
        Connect();
    }

    public void LeaveRoomBrowser()
    {
        if (modeSelected) return;
        browsingRooms = false;
        pendingAction = OnlineAction.None;
        startMenu?.ShowRoomBrowser(false);
        rooms.Clear(); roomListRevision++;
        if (PhotonNetwork.NetworkClientState != ClientState.Disconnected && PhotonNetwork.NetworkClientState != ClientState.PeerCreated)
            PhotonNetwork.Disconnect();
    }

    public void RefreshRooms()
    {
        if (!browsingRooms || pendingAction != OnlineAction.None) return;
        rooms.Clear(); roomListRevision++;
        status = () => GameLocalization.T("Loading rooms...");
        if (!PhotonNetwork.IsConnected) { Connect(); return; }
        if (PhotonNetwork.InLobby) PhotonNetwork.LeaveLobby();
        else if (PhotonNetwork.IsConnectedAndReady) PhotonNetwork.JoinLobby(TypedLobby.Default);
    }

    public override void OnLeftLobby()
    {
        if (browsingRooms && PhotonNetwork.IsConnectedAndReady) PhotonNetwork.JoinLobby(TypedLobby.Default);
    }

    public override void OnJoinedLobby()
    {
        if (!browsingRooms) return;
        status = () => GameLocalization.T("Finding available rooms...");
        roomListRevision++;
        RunPendingAction();
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        if (!browsingRooms) return;
        foreach (RoomInfo room in roomList)
        {
            if (room.RemovedFromList) rooms.Remove(room.Name);
            else rooms[room.Name] = room;
        }
        roomListRevision++;
        if (pendingAction == OnlineAction.None)
            status = () => GameLocalization.Format("Rooms found: {0}", rooms.Count);
    }

    public void QuickJoin()
    {
        if (modeSelected || YandexAds.Busy) return;
        browsingRooms = true;
        pendingRoomName = null;
        generatedRoomName = false;
        quickJoinRetries = 0;
        pendingAction = OnlineAction.QuickJoin;
        status = () => GameLocalization.T("Finding a match...");
        Connect();
    }

    public void JoinSelectedRoom(string roomName)
    {
        if (!browsingRooms || string.IsNullOrEmpty(roomName) || pendingAction != OnlineAction.None) return;
        if (!rooms.TryGetValue(roomName, out var room) || !room.IsOpen || (room.MaxPlayers > 0 && room.PlayerCount >= room.MaxPlayers)) return;
        pendingRoomName = roomName;
        pendingAction = OnlineAction.JoinRoom;
        status = () => GameLocalization.Format("Joining {0}...", roomName);
        Connect();
    }

    public void CreateNamedRoom(string roomName)
    {
        if (!browsingRooms || pendingAction != OnlineAction.None) return;
        pendingRoomName = string.IsNullOrWhiteSpace(roomName) ? NewRoomName() : roomName.Trim();
        generatedRoomName = string.IsNullOrWhiteSpace(roomName);
        createRetries = 0;
        if (pendingRoomName.Length > 32) pendingRoomName = pendingRoomName.Substring(0, 32);
        pendingAction = OnlineAction.CreateRoom;
        status = () => GameLocalization.Format("Creating {0}...", pendingRoomName);
        Connect();
    }

    private string NewRoomName() => $"{roomPrefix}-{Guid.NewGuid():N}".Substring(0, Mathf.Min(24, roomPrefix.Length + 9));

    private void RunPendingAction()
    {
        // Photon returns to the master server after a failed random join, outside the lobby.
        // Creating the fallback room is valid there and must not wait for OnJoinedLobby.
        if (!PhotonNetwork.IsConnectedAndReady) return;
        bool started = true;
        switch (pendingAction)
        {
            case OnlineAction.QuickJoin:
                started = PhotonNetwork.JoinRandomRoom(null, 0, MatchmakingMode.FillRoom, TypedLobby.Default, null); break;
            case OnlineAction.JoinRoom:
                started = PhotonNetwork.JoinRoom(pendingRoomName); break;
            case OnlineAction.CreateRoom:
                started = PhotonNetwork.CreateRoom(pendingRoomName,
                    new RoomOptions { MaxPlayers = maxPlayers, IsOpen = true, IsVisible = true }, TypedLobby.Default); break;
        }
        if (!started) { pendingAction = OnlineAction.None; status = () => GameLocalization.T("Room request failed. Try again."); }
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        if (pendingAction != OnlineAction.QuickJoin) return;
        pendingRoomName = NewRoomName();
        generatedRoomName = true;
        createRetries = 0;
        pendingAction = OnlineAction.CreateRoom;
        status = () => GameLocalization.T("Creating a new match...");
        RunPendingAction();
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        // A generated name can collide; retry without changing a player's chosen name.
        if (pendingAction == OnlineAction.CreateRoom && generatedRoomName &&
            returnCode == ErrorCode.GameIdAlreadyExists && createRetries++ < 3)
        { pendingRoomName = NewRoomName(); RunPendingAction(); return; }
        pendingAction = OnlineAction.None;
        status = () => GameLocalization.Format("Could not create room ({0}): {1}", returnCode, message);
    }

    public override void OnJoinedRoom()
    {
        if (!modeSelected && !browsingRooms) { PhotonNetwork.LeaveRoom(); return; }
        pendingAction = OnlineAction.None;
        browsingRooms = false;
        if (!modeSelected)
        {
            YandexAds.FirstEntry();
            modeSelected = true;
            singlePlayer = PhotonNetwork.OfflineMode;
            if (startMenu != null) { startMenu.gameObject.SetActive(false); Destroy(startMenu.gameObject); startMenu = null; }
        }
        status = () => GameLocalization.Format("Комната {0} ({1}/{2})", PhotonNetwork.CurrentRoom.Name, PhotonNetwork.CurrentRoom.PlayerCount, maxPlayers);
        if (selectedTeam == 0) AutoPickTeam();
        ShowDeploymentScreen();
    }

    /// <summary>Auto-balance: join the weaker side (humans + bots), random on tie.</summary>
    private void AutoPickTeam()
    {
        int team1 = 0, team2 = 0;
        foreach (var player in PhotonNetwork.PlayerList)
        {
            if (player.CustomProperties["team"] is int t) { if (t == 2) team2++; else team1++; }
        }
        foreach (var bot in FindObjectsByType<BotController>(FindObjectsSortMode.None))
        {
            if (bot == null) continue;
            if (bot.Team == 2) team2++; else team1++;
        }
        int team = team1 == team2 ? UnityEngine.Random.Range(1, 3) : team2 < team1 ? 2 : 1;
        selectedTeam = team;
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { TeamProperty, team } });
        status = () => GameLocalization.Format("Вы автоматически вступили в команду {0}.", team);
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        status = () => GameLocalization.Format("Комната {0} ({1}/{2})", PhotonNetwork.CurrentRoom.Name, PhotonNetwork.CurrentRoom.PlayerCount, maxPlayers);
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        status = () => GameLocalization.Format("Комната {0} ({1}/{2})", PhotonNetwork.CurrentRoom.Name, PhotonNetwork.CurrentRoom.PlayerCount, maxPlayers);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        if (pendingAction == OnlineAction.QuickJoin && quickJoinRetries++ < 2 &&
            (returnCode == ErrorCode.GameFull || returnCode == ErrorCode.GameClosed || returnCode == ErrorCode.GameDoesNotExist))
        {
            status = () => GameLocalization.T("Finding another match...");
            RunPendingAction();
            return;
        }
        pendingAction = OnlineAction.None;
        if (pendingRoomName != null) rooms.Remove(pendingRoomName);
        roomListRevision++;
        status = () => GameLocalization.Format("Could not join room ({0}): {1}", returnCode, message);
        if (browsingRooms && PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.InLobby)
            PhotonNetwork.JoinLobby(TypedLobby.Default);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        playerSpawned = false;
        pendingAction = OnlineAction.None;
        rooms.Clear(); roomListRevision++;
        if (modeSelected && singlePlayer) { PhotonNetwork.OfflineMode = true; return; }
        status = () => GameLocalization.Format("Соединение потеряно: {0}", cause);
        if (modeSelected) ShowDeploymentScreen();
    }

    private void ShowDeploymentScreen()
    {
        if (deploymentScreen == null) deploymentScreen = DeploymentScreen.Create(this);
        deploymentScreen.gameObject.SetActive(true);
    }

    public void ShowAfterDeath(PlayerHealth player, float delay)
    {
        if (player == null || !player.IsDead || BotController.IsBot(player) ||
            (PhotonNetwork.InRoom && !player.photonView.IsMine)) return;
        pendingCorpse = player.gameObject;
        deployAvailableAt = Time.unscaledTime + Mathf.Max(0f, delay);
        playerSpawned = false;
        ShowDeploymentScreen();
    }

    public void Deploy()
    {
        if (!CanDeploy) return;
        SpawnPlayer();
        if (!playerSpawned) return;
        if (pendingCorpse != null) PhotonNetwork.Destroy(pendingCorpse);
        pendingCorpse = null;
        if (deploymentScreen != null) deploymentScreen.gameObject.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnDestroy()
    {
        if (startMenu != null) Destroy(startMenu.gameObject);
        if (deploymentScreen != null) Destroy(deploymentScreen.gameObject);
    }

    private void SpawnPlayer()
    {
        if (playerSpawned)
        {
            return;
        }

        if (Resources.Load<GameObject>(playerPrefabResourceName) == null)
        {
            Debug.LogWarning($"Player prefab was not found at Resources/{playerPrefabResourceName}. Add it to Assets/Resources to spawn players.");
            return;
        }

        Vector3 position = GetSpawnPosition(selectedTeam);
        PhotonNetwork.Instantiate(playerPrefabResourceName, position, GetSpawnRotation(position));
        playerSpawned = true;
    }

    /// <summary>Respawn after death: destroys the corpse and spawns a fresh player on the team spawn.</summary>
    public void RespawnPlayer(GameObject deadPlayer)
    {
        int team = selectedTeam;
        if (PhotonNetwork.LocalPlayer.CustomProperties["team"] is int t) team = t;
        selectedTeam = team;
        Vector3 position = GetSpawnPosition(team);
        if (deadPlayer != null)
        {
            if (PhotonNetwork.InRoom) PhotonNetwork.Destroy(deadPlayer);
            else Destroy(deadPlayer);
        }
        if (PhotonNetwork.InRoom)
        {
            PhotonNetwork.Instantiate(playerPrefabResourceName, position, GetSpawnRotation(position));
        }
        else if (Resources.Load<GameObject>(playerPrefabResourceName) is GameObject prefab)
        {
            Instantiate(prefab, position, GetSpawnRotation(position));
        }
        playerSpawned = true;
    }

    public Quaternion GetSpawnRotation(Vector3 position)
    {
        Vector3 direction = mapCenter - position;
        direction.y = 0f;
        return direction.sqrMagnitude > .0001f
            ? Quaternion.LookRotation(direction, Vector3.up)
            : Quaternion.identity;
    }

    private Vector3 GetSpawnPosition(int team)
    {
        TeamSpawnPoint[] spawnPoints = FindObjectsOfType<TeamSpawnPoint>();
        int matchingPoints = 0;

        foreach (TeamSpawnPoint spawnPoint in spawnPoints)
        {
            if (spawnPoint.Team == team)
            {
                matchingPoints++;
            }
        }

        if (matchingPoints == 0)
        {
            Debug.LogWarning($"No spawn points found for team {team}. Using fallback position.");
            return fallbackSpawnPosition;
        }

        int spawnIndex = Mathf.Max(0, PhotonNetwork.LocalPlayer.ActorNumber - 1) % matchingPoints;
        foreach (TeamSpawnPoint spawnPoint in spawnPoints)
        {
            if (spawnPoint.Team == team && spawnIndex-- == 0)
            {
                return spawnPoint.transform.position;
            }
        }

        return fallbackSpawnPosition;
    }

    private void OnGUI()
    {
        if (!modeSelected || playerSpawned || autoJoinOnStart) return;
        GUI.Box(new Rect(16f, 16f, 430f, 128f), GameLocalization.T("BlockField test lobby"));
        GUI.Label(new Rect(30f, 45f, 400f, 24f), status());
        GUI.Label(new Rect(30f, 70f, 400f, 24f), GameLocalization.Format("Регион: {0}", PhotonNetwork.CloudRegion));

        if (selectedTeam == 0 && !autoJoinOnStart)
        {
            if (GUI.Button(new Rect(30f, 100f, 180f, 28f), GameLocalization.T("Join Team 1")))
            {
                SelectTeam(1);
            }

            if (GUI.Button(new Rect(230f, 100f, 180f, 28f), GameLocalization.T("Join Team 2")))
            {
                SelectTeam(2);
            }
        }
    }
}
