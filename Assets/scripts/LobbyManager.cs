using Photon.Pun;
using Photon.Realtime;
using UnityEngine;
using Hashtable = ExitGames.Client.Photon.Hashtable;

public class LobbyManager : MonoBehaviourPunCallbacks
{
    private const string GameVersion = "CounterMine-0.1";
    private const string TeamProperty = "team";

    [SerializeField] private string roomName = "CounterMine-Test";
    [SerializeField] private byte maxPlayers = 16;
    [SerializeField] private string playerPrefabResourceName = "Player";
    [SerializeField] private Vector3 fallbackSpawnPosition = new Vector3(0f, 2f, 0f);

    private string status = "Choose a team.";
    private bool playerSpawned;
    private int selectedTeam;

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
    }

    private void SelectTeam(int team)
    {
        selectedTeam = team;
        PhotonNetwork.LocalPlayer.SetCustomProperties(new Hashtable { { TeamProperty, team } });
        status = $"Team {team} selected. Connecting to Photon...";
        Connect();
    }

    public void Connect()
    {
        if (PhotonNetwork.IsConnected)
        {
            JoinRoom();
            return;
        }

        status = "Connecting to Photon...";
        PhotonNetwork.ConnectUsingSettings();
        PhotonNetwork.GameVersion = GameVersion;
    }

    public override void OnConnectedToMaster()
    {
        status = "Connected. Joining room...";
        JoinRoom();
    }

    public override void OnJoinedRoom()
    {
        status = $"In room {PhotonNetwork.CurrentRoom.Name} ({PhotonNetwork.CurrentRoom.PlayerCount}/{maxPlayers})";
        SpawnPlayer();
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
        status = $"Could not join room ({returnCode}): {message}";
        Debug.LogError(status);
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        playerSpawned = false;
        status = $"Disconnected: {cause}";
    }

    private void JoinRoom()
    {
        if (PhotonNetwork.InRoom)
        {
            return;
        }

        RoomOptions options = new RoomOptions { MaxPlayers = maxPlayers };
        PhotonNetwork.JoinOrCreateRoom(roomName, options, TypedLobby.Default);
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

        PhotonNetwork.Instantiate(playerPrefabResourceName, GetSpawnPosition(selectedTeam), Quaternion.identity);
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
            PhotonNetwork.Instantiate(playerPrefabResourceName, position, Quaternion.identity);
        }
        else if (Resources.Load<GameObject>(playerPrefabResourceName) is GameObject prefab)
        {
            Instantiate(prefab, position, Quaternion.identity);
        }
        playerSpawned = true;
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

        if (selectedTeam == 0)
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
