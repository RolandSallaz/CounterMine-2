using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

public class LobbyManager : MonoBehaviourPunCallbacks
{
    private const string GameVersion = "CounterMine-0.1";

    [SerializeField] private string roomName = "CounterMine-Test";
    [SerializeField] private byte maxPlayers = 16;
    [SerializeField] private string playerPrefabResourceName = "Player";
    [SerializeField] private Vector3 spawnPosition = new Vector3(0f, 2f, 0f);

    private string status = "Starting...";
    private bool playerSpawned;

    private void Start()
    {
        PhotonNetwork.AutomaticallySyncScene = true;
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

        PhotonNetwork.Instantiate(playerPrefabResourceName, spawnPosition, Quaternion.identity);
        playerSpawned = true;
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(16f, 16f, 430f, 88f), "CounterMine test lobby");
        GUI.Label(new Rect(30f, 45f, 400f, 24f), status);
        GUI.Label(new Rect(30f, 70f, 400f, 24f), $"Region: {PhotonNetwork.CloudRegion}");
    }
}
