using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class LobbyManager : MonoBehaviourPunCallbacks
{
    private const string GameVersion = "CounterMine-0.2-conquest";
    private const string TeamProperty = "team";

    [SerializeField] private string roomPrefix = "CounterMine";
    [SerializeField] private byte maxPlayers = 16;
    [SerializeField, Min(1)] private int maxRoomsToTry = 50;
    [SerializeField] private string playerPrefabResourceName = "Player";
    [SerializeField] private Vector3 fallbackSpawnPosition = new Vector3(0f, 2f, 0f);
    [SerializeField] private Vector3 mapCenter = Vector3.zero;
    [SerializeField] private bool autoJoinOnStart = true;

    private System.Func<string> status = () => GameLocalization.T("Choose a team.");
    private bool playerSpawned;
    private int selectedTeam;
    private int matchmakingIndex = 1;
    private DeploymentScreen deploymentScreen;
    private StartMenuScreen startMenu;
    private bool modeSelected;
    private bool singlePlayer;
    private GameObject pendingCorpse;
    private float deployAvailableAt;
    public bool CanDeploy => !playerSpawned && PhotonNetwork.InRoom && YandexPlayerData.IsLoaded && Time.unscaledTime >= deployAvailableAt;
    public string ConnectionStatus => status();

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
        if (!modeSelected) return;
        if (singlePlayer)
        {
            // OfflineMode invokes OnConnectedToMaster synchronously. Bots and match rules
            // then use the same room lifecycle without contacting Photon servers.
            if (!PhotonNetwork.OfflineMode) PhotonNetwork.OfflineMode = true;
            else if (!PhotonNetwork.InRoom) PhotonNetwork.CreateRoom("Solo");
            return;
        }
        if (PhotonNetwork.IsConnected)
        {
            matchmakingIndex = 1;
            JoinFirstAvailableRoom();
            return;
        }

        status = () => GameLocalization.T("Connecting to Photon...");
        PhotonNetwork.GameVersion = GameVersion;
        PhotonNetwork.ConnectUsingSettings();
    }

    public override void OnConnectedToMaster()
    {
        if (!modeSelected) return;
        if (singlePlayer) { PhotonNetwork.CreateRoom("Solo"); return; }
        status = () => GameLocalization.T("Connected. Joining room...");
        matchmakingIndex = 1;
        JoinFirstAvailableRoom();
    }

    public override void OnJoinedRoom()
    {
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
        int team = team1 == team2 ? Random.Range(1, 3) : team2 < team1 ? 2 : 1;
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
        // Bucket matchmaking: full room -> next bucket, missing room -> create it.
        if (returnCode == ErrorCode.GameFull)
        {
            matchmakingIndex++;
            if (matchmakingIndex > Mathf.Max(1, maxRoomsToTry))
            {
                status = () => GameLocalization.T("All rooms are full, try again later.");
                Debug.LogError(status());
                return;
            }
            JoinFirstAvailableRoom();
            return;
        }
        if (returnCode == ErrorCode.GameDoesNotExist)
        {
            RoomOptions options = new RoomOptions { MaxPlayers = maxPlayers };
            PhotonNetwork.JoinOrCreateRoom(BucketName(matchmakingIndex), options, TypedLobby.Default);
            return;
        }
        status = () => GameLocalization.Format("Ошибка входа ({0}): {1}", returnCode, message);
        Debug.LogError(status());
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        playerSpawned = false;
        status = () => GameLocalization.Format("Соединение потеряно: {0}", cause);
        ShowDeploymentScreen();
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

    private string BucketName(int index) => $"{roomPrefix}-{index}";

    private void JoinFirstAvailableRoom()
    {
        if (PhotonNetwork.InRoom)
        {
            return;
        }

        matchmakingIndex = Mathf.Max(1, matchmakingIndex);
        status = () => GameLocalization.Format("Поиск комнаты ({0})...", BucketName(matchmakingIndex));
        PhotonNetwork.JoinRoom(BucketName(matchmakingIndex));
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
        GUI.Box(new Rect(16f, 16f, 430f, 128f), GameLocalization.T("CounterMine test lobby"));
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
