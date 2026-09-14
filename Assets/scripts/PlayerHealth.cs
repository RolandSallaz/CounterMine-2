using System.Collections.Generic;
using ExitGames.Client.Photon;
using Photon.Pun;
using UnityEngine;

[RequireComponent(typeof(PhotonView), typeof(CharacterController))]
public sealed class PlayerHealth : MonoBehaviourPunCallbacks
{
    public static readonly HashSet<PlayerHealth> ActivePlayers = new HashSet<PlayerHealth>();
    [SerializeField, Min(1)] private int maximumHealth = 100;
    [SerializeField] private PlayerDeathController deathController;
    private CharacterController capsule;
    private int revision;
    private int registeredViewId;
    public int CurrentHealth { get; private set; }
    public bool IsDead => CurrentHealth <= 0 || (deathController != null && deathController.IsDead);
    private string PropertyKey => "hp/" + photonView.ViewID;
    private struct Sample { public double time; public Vector3 bottom, top; public float radius; }
    private readonly Sample[] history = new Sample[64];
    private int historyCount;
    private int historyNext;

    private void Awake()
    {
        capsule = GetComponent<CharacterController>();
        deathController ??= GetComponent<PlayerDeathController>();
        CurrentHealth = maximumHealth;
    }
    public override void OnEnable() { base.OnEnable(); ActivePlayers.Add(this); }
    public override void OnDisable() { ActivePlayers.Remove(this); base.OnDisable(); }
    private void Start() { registeredViewId = photonView.ViewID; ReadSnapshot(); Record(NetworkTime); }
    private void LateUpdate() => Record(NetworkTime);
    private static double NetworkTime => PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;

    private Sample CurrentCapsule(double time)
    {
        capsule ??= GetComponent<CharacterController>();
        Vector3 scale = transform.lossyScale;
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float halfAxis = Mathf.Max(0f, capsule.height * Mathf.Abs(scale.y) * .5f - radius);
        Vector3 center = transform.TransformPoint(capsule.center);
        return new Sample { time = time, bottom = center - transform.up * halfAxis, top = center + transform.up * halfAxis, radius = radius };
    }
    private void Record(double time)
    {
        if (historyCount > 0 && time - history[(historyNext - 1 + history.Length) % history.Length].time < 1d / 60d) return;
        history[historyNext] = CurrentCapsule(time);
        historyNext = (historyNext + 1) % history.Length;
        historyCount = Mathf.Min(historyCount + 1, history.Length);
    }
    public void GetCapsule(double time, out Vector3 bottom, out Vector3 top, out float radius)
    {
        var sample = CurrentCapsule(time);
        if (historyCount > 0)
        {
            int oldest = (historyNext - historyCount + history.Length) % history.Length;
            sample = history[oldest];
            for (int i = 1; i < historyCount; i++)
            {
                var next = history[(oldest + i) % history.Length];
                if (next.time >= time)
                {
                    float blend = (float)System.Math.Clamp((time - sample.time) / System.Math.Max(.000001, next.time - sample.time), 0, 1);
                    sample.bottom = Vector3.Lerp(sample.bottom, next.bottom, blend);
                    sample.top = Vector3.Lerp(sample.top, next.top, blend);
                    sample.radius = Mathf.Lerp(sample.radius, next.radius, blend);
                    break;
                }
                sample = next;
            }
        }
        bottom = sample.bottom; top = sample.top; radius = sample.radius;
    }

    public void ApplyMasterDamage(int amount, Vector3 force, Vector3 point)
    {
        if ((PhotonNetwork.InRoom && !PhotonNetwork.IsMasterClient) || IsDead || amount <= 0) return;
        int nextHealth = Mathf.Max(0, CurrentHealth - amount);
        int nextRevision = revision + 1;
        ApplySnapshot(nextRevision, nextHealth, force, point);
        if (PhotonNetwork.InRoom)
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { PropertyKey, new object[] { nextRevision, nextHealth, force, point } } });
    }
    private void ApplySnapshot(int nextRevision, int hp, Vector3 force, Vector3 point)
    {
        if (nextRevision <= revision) return;
        revision = nextRevision;
        CurrentHealth = Mathf.Clamp(hp, 0, maximumHealth);
        if (CurrentHealth == 0) deathController?.ApplyNetworkDeath(force, point);
    }
    private void ReadSnapshot()
    {
        if (!PhotonNetwork.InRoom) return;
        if (PhotonNetwork.CurrentRoom.CustomProperties[PropertyKey] is object[] state && state.Length == 4 &&
            state[0] is int version && state[1] is int hp && state[2] is Vector3 force && state[3] is Vector3 point)
            ApplySnapshot(version, hp, force, point);
    }
    public override void OnRoomPropertiesUpdate(Hashtable propertiesThatChanged)
    {
        if (propertiesThatChanged.ContainsKey(PropertyKey)) ReadSnapshot();
    }
    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient) => ReadSnapshot();
    private void OnDestroy()
    {
        ActivePlayers.Remove(this);
        if (PhotonNetwork.InRoom && PhotonNetwork.IsMasterClient && registeredViewId != 0)
            PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { "hp/" + registeredViewId, null } });
    }
}
