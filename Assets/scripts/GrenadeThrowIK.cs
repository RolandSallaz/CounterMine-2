using System;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Independent left-arm presentation. Never changes the weapon action or CanFire.</summary>
[DisallowMultipleComponent]
public sealed class GrenadeThrowIK : MonoBehaviourPun
{
    [SerializeField, Min(.3f)] private float duration = .85f;
    [SerializeField, Range(.4f, .8f)] private float releasePhase = .57f;
    private WeaponIdleSynchronizer animationSource;
    private PlayerHealth health;
    private Camera view;
    private double startedAt;
    private double lastStart = double.NegativeInfinity;
    private bool playing, released;
    public event Action Released;
    private double Now => PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;
    public bool IsPlaying => playing;

    private void Awake()
    {
        animationSource = GetComponentInChildren<WeaponIdleSynchronizer>(true);
        health = GetComponent<PlayerHealth>();
        view = GetComponentInChildren<Camera>(true);
    }

    private bool CanAnimate => isActiveAndEnabled && health != null && !health.IsDead &&
        animationSource != null && animationSource.IsIdlePlaying;

    private void Update()
    {
        if (playing)
        {
            if (!CanAnimate) { playing = false; return; }
            float phase = (float)(Now - startedAt) / duration;
            if (!released && phase >= releasePhase)
            {
                released = true;
                Released?.Invoke();
            }
            if (phase >= 1f) playing = false;
        }
        if (!BotController.IsBot(this) && (!PhotonNetwork.InRoom || photonView.IsMine) &&
            Application.isFocused && Cursor.lockState == CursorLockMode.Locked &&
            Keyboard.current?.gKey.wasPressedThisFrame == true) TryThrow();
    }

    public bool TryThrow()
    {
        if ((PhotonNetwork.InRoom && !photonView.IsMine) || playing || !CanAnimate) return false;
        var stock = GetComponent<GrenadeThrower>();
        if (stock != null && !stock.HasGrenades) return false;
        double time = Now;
        Begin(time);
        if (PhotonNetwork.InRoom)
            GetComponent<NetworkWeaponPresentation>().SendGrenadeThrow(time);
        return true;
    }

    public void Begin(double time)
    {
        double age = Now - time;
        if (double.IsNaN(time) || double.IsInfinity(time) || time <= lastStart ||
            age < -.25 || age >= duration || !CanAnimate) return;
        lastStart = startedAt = time;
        playing = true;
        // Do not replay an already elapsed release on a delayed remote animation.
        released = age >= duration * releasePhase;
    }

    public bool Sample(Transform hand, Transform grip, out Vector3 position,
        out Quaternion rotation, out Vector3 elbow)
    {
        position = grip.position; rotation = grip.rotation;
        elbow = hand.parent.position;
        if (!playing || !CanAnimate || view == null) return false;
        float t = Mathf.Clamp01((float)(Now - startedAt) / duration);
        Transform shoulder = hand.parent.parent;
        Quaternion basis = view.transform.rotation;
        Vector3 up = transform.up;
        Vector3 facing = basis * Vector3.forward - up * Vector3.Dot(basis * Vector3.forward, up);
        if (facing.sqrMagnitude < .000001f) facing = transform.forward - up * Vector3.Dot(transform.forward, up);
        if (facing.sqrMagnitude < .000001f) facing = basis * Vector3.forward;
        facing.Normalize();
        Vector3 side = Vector3.Cross(up, facing);
        float reach = (Vector3.Distance(shoulder.position, hand.parent.position) +
            Vector3.Distance(hand.parent.position, hand.position)) * .97f;
        // Belt pouch pickup: left hand dips to the waist instead of winding up behind the head.
        Vector3 belt = shoulder.position - up * .32f + facing * .18f + side * .12f;
        // Underhand toss release from the belt: forward, slightly above the shoulder.
        Vector3 release = shoulder.position + up * .22f + facing * (reach * .9f) + side * .14f;
        float blend;
        if (t < .32f)
        {
            blend = Mathf.SmoothStep(0f, 1f, t / .32f);
            position = Vector3.Lerp(grip.position, belt, blend);
        }
        else if (t < releasePhase)
        {
            blend = 1f;
            float swing = Mathf.SmoothStep(0f, 1f, (t - .32f) / (releasePhase - .32f));
            position = Vector3.Lerp(belt, release, swing);
        }
        else
        {
            blend = 1f - Mathf.SmoothStep(0f, 1f, (t - releasePhase) / (1f - releasePhase));
            position = Vector3.Lerp(grip.position, release, blend);
        }
        // Stay inside the arm's reach, even for alternate character proportions.
        position = shoulder.position + Vector3.ClampMagnitude(position - shoulder.position, reach);
        // Toss pitch around camera-right, rather than the imported wrist's local
        // axes, which can turn this pitch into a sideways roll on the rig.
        rotation = Quaternion.Slerp(grip.rotation,
            Quaternion.AngleAxis(-30f, view.transform.right) * grip.rotation, blend);
        elbow = Vector3.Lerp(elbow, shoulder.position - up * .15f - side * .08f + facing * .1f, blend);
        return true;
    }

    private void OnDisable() => playing = false;
}
