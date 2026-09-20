using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class LobbyManager : MonoBehaviourPunCallbacks
{
    private const string GameVersion = "CounterMine-0.1";
    private const string TeamProperty = "team";

    [SerializeField] private string roomPrefix = "CounterMine";
    [SerializeField] private byte maxPlayers = 16;
    [SerializeField, Min(1)] private int maxRoomsToTry = 50;
    [SerializeField] private string playerPrefabResourceName = "Player";
    [SerializeField] private Vector3 fallbackSpawnPosition = new Vector3(0f, 2f, 0f);
    [SerializeField] private Vector3 mapCenter = Vector3.zero;
    [SerializeField] private bool autoJoinOnStart = true;

    private string status = "Choose a team.";
    private bool playerSpawned;
    private int selectedTeam;
    private int matchmakingIndex = 1;

    private void Start()
    {
        if (GetComponent<BotRoomSpawner>() == null) gameObject.AddComponent<BotRoomSpawner>();
        PhotonNetwork.AutomaticallySyncScene = true;
        KillRewards.EnsureSubscribed();
        YandexPlayerData.Load();
#if UNITY_WEBGL && !UNITY_EDITOR
        YandexCloudSave.RequestPlayerName(playerName =>
        {
            if (!string.IsNullOrEmpty(playerName)) PhotonNetwork.NickName = playerName;
        });
#endif
        if (autoJoinOnStart) Connect();
    }

    private void SelectTeam(int team)
    {
        GameAudio.Effect("UI/click", Vector3.zero, .5f, 1f, true);
        selectedTeam = team;
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { TeamProperty, team } });
        status = $"Team {team} selected. Connecting to Photon...";
        Connect();
    }

    public void Connect()
    {
        if (PhotonNetwork.IsConnected)
        {
            matchmakingIndex = 1;
            JoinFirstAvailableRoom();
            return;
        }

        status = "Connecting to Photon...";
        PhotonNetwork.ConnectUsingSettings();
        PhotonNetwork.GameVersion = GameVersion;
    }

    public override void OnConnectedToMaster()
    {
        status = "Connected. Joining room...";
        matchmakingIndex = 1;
        JoinFirstAvailableRoom();
    }

    public override void OnJoinedRoom()
    {
        status = $"In room {PhotonNetwork.CurrentRoom.Name} ({PhotonNetwork.CurrentRoom.PlayerCount}/{maxPlayers})";
        if (selectedTeam == 0) AutoPickTeam();
        SpawnPlayer();
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
        status = $"Auto-joined team {team}.";
    }

    public override void OnPlayerEnteredRoom(Player newPlayer)
    {
        status = $"In room {PhotonNetwork.CurrentRoom.Name} ({PhotonNetwork.CurrentRoom.PlayerCount}/{maxPlayers})";
    }

    public override void OnPlayerLeftRoom(Player otherPlayer)
    {
        status = $"In room {PhotonNetwork.CurrentRoom.Name} ({PhotonNetwork.CurrentRoom.PlayerCount}/{maxPlayers})";
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        // Bucket matchmaking: full room -> next bucket, missing room -> create it.
        if (returnCode == ErrorCode.GameFull)
        {
            matchmakingIndex++;
            if (matchmakingIndex > Mathf.Max(1, maxRoomsToTry))
            {
                status = "All rooms are full, try again later.";
                Debug.LogError(status);
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
        status = $"Could not join room ({returnCode}): {message}";
        Debug.LogError(status);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        playerSpawned = false;
        status = $"Disconnected: {cause}";
    }

    private string BucketName(int index) => $"{roomPrefix}-{index}";

    private void JoinFirstAvailableRoom()
    {
        if (PhotonNetwork.InRoom)
        {
            return;
        }

        matchmakingIndex = Mathf.Max(1, matchmakingIndex);
        status = $"Looking for a room ({BucketName(matchmakingIndex)})...";
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

        int spawnIndex = (PhotonNetwork.LocalPlayer.ActorNumber - 1) % matchingPoints;
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
        if (playerSpawned) return;
        GUI.Box(new Rect(16f, 16f, 430f, 128f), "CounterMine test lobby");
        GUI.Label(new Rect(30f, 45f, 400f, 24f), status);
        GUI.Label(new Rect(30f, 70f, 400f, 24f), $"Region: {PhotonNetwork.CloudRegion}");

        if (selectedTeam == 0 && !autoJoinOnStart)
        {
            if (GUI.Button(new Rect(30f, 100f, 180f, 28f), "Join Team 1"))
            {
                SelectTeam(1);
            }

            if (GUI.Button(new Rect(230f, 100f, 180f, 28f), "Join Team 2"))
            {
                SelectTeam(2);
            }
        }
    }
}
