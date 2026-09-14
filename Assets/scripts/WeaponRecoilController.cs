using Kinemation.Recoilly;
using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Shot cadence and dynamic recoil, driven by the installed Recoilly solver.</summary>
[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
public sealed class WeaponRecoilController : MonoBehaviour
{
    [SerializeField] private RecoilAnimation recoilAnimation;
    [SerializeField] private RecoilAnimData recoilProfile;
    [SerializeField] private WeaponAimController aimController;
    [SerializeField] private PlayerCameraLook cameraLook;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private PlayerDeathController deathController;
    [SerializeField] private PlayerAnimancerController animationController;
    [SerializeField] private NetworkWeapon networkWeapon;
    [SerializeField, Min(1f)] private float roundsPerMinute = 600f;
    [SerializeField] private bool automatic = true;
    [SerializeField, Min(0f)] private float heatPerShot = .14f;
    [SerializeField, Min(0f)] private float heatRecovery = .5f;
    [SerializeField, Min(1f)] private float sustainedFireMultiplier = 1.6f;
    [SerializeField] private Vector2 cameraPitch = new Vector2(.35f, .6f);
    [SerializeField, Min(0f)] private float cameraYaw = .2f;

    private PhotonView owner;
    private Vector3 restPosition;
    private Quaternion restRotation;
    private double nextShotTime;
    private float shotStrength = 1f;
    private bool initialized;
    private bool hasRestPose;
    public Vector3 NeutralLocalPosition => hasRestPose ? restPosition : transform.localPosition;
    public Quaternion NeutralLocalRotation => hasRestPose ? restRotation : transform.localRotation;
    public float Heat { get; private set; }
    public int ShotsFired { get; private set; }
    public float RoundsPerMinute => roundsPerMinute;

    private void Awake()
    {
        owner = GetComponentInParent<PhotonView>();
        networkWeapon ??= GetComponentInParent<NetworkWeapon>();
        restPosition = transform.localPosition;
        restRotation = transform.localRotation;
        hasRestPose = true;
    }

    private void OnEnable()
    {
        if (recoilAnimation == null || recoilProfile == null)
        {
            Debug.LogError("Assign the Recoilly component and recoil profile.", this);
            return;
        }
        recoilAnimation.Init(recoilProfile, Mathf.Max(1f, roundsPerMinute));
        // Each accepted shot produces exactly one impulse. The controller owns automatic cadence.
        recoilAnimation.fireMode = FireMode.Semi;
        recoilAnimation.enabled = true;
        initialized = true;
        nextShotTime = 0;
        Heat = 0f;
        shotStrength = 1f;
    }

    private void Update()
    {
        if (!initialized) return;
        Heat = Mathf.MoveTowards(Heat, 0f, heatRecovery * Time.deltaTime);
        bool allowed = (owner == null || (owner.IsMine && (!PhotonNetwork.InRoom || owner.OwnerActorNr == PhotonNetwork.LocalPlayer.ActorNumber))) && cameraLook != null && cameraLook.isActiveAndEnabled &&
            (deathController == null || !deathController.IsDead) &&
            (playerController == null || !playerController.IsSprinting) &&
            Application.isFocused && Cursor.lockState == CursorLockMode.Locked &&
            Keyboard.current?.escapeKey.wasPressedThisFrame != true && Time.deltaTime > 0f;
        bool trigger = Mouse.current != null && (automatic ? Mouse.current.leftButton.isPressed : Mouse.current.leftButton.wasPressedThisFrame);
        if (!allowed || !trigger) nextShotTime = System.Math.Max(nextShotTime, Time.timeAsDouble);
        if (allowed && trigger && ConsumeShotTime(Time.timeAsDouble)) FireShot();
    }

    private bool ConsumeShotTime(double now)
    {
        if (now < nextShotTime) return false;
        double interval = 60d / Mathf.Max(1f, roundsPerMinute);
        // Keep normal frame quantization, but never catch up with adjacent shots after a hitch.
        if (now - nextShotTime > interval * .5d) nextShotTime = now;
        nextShotTime += interval;
        return true;
    }

    private void FireShot()
    {
        // Capture the shot direction before applying this shot's camera kick.
        if (networkWeapon == null || !networkWeapon.FireLocalShot()) return;
        float aim = aimController != null ? aimController.AimAmount : 0f;
        float ergonomics = aimController != null ? aimController.ErgonomicsNormalized : .55f;
        shotStrength = Mathf.Lerp(1f, sustainedFireMultiplier, Heat) * Mathf.Lerp(1.1f, .9f, ergonomics);
        // Blend ADS continuously outside the plugin's boolean aiming flag to avoid a midpoint snap.
        recoilAnimation.isAiming = false;
        recoilAnimation.Play();
        float cameraStrength = shotStrength * Mathf.Lerp(1f, .65f, aim);
        cameraLook?.AddRecoil(new Vector2(Random.Range(-cameraYaw, cameraYaw), Random.Range(cameraPitch.x, cameraPitch.y)) * cameraStrength);
        animationController?.PlayWeaponFire();
        Heat = Mathf.Clamp01(Heat + heatPerShot);
        ShotsFired++;
    }

    private void LateUpdate()
    {
        if (!initialized) return;
        float aim = aimController != null ? aimController.AimAmount : 0f;
        Vector3 rotationScale = Vector3.Lerp(Vector3.one, recoilProfile.aimRot, aim);
        Vector3 positionScale = Vector3.Lerp(Vector3.one, recoilProfile.aimLoc, aim);
        Vector3 rotation = Vector3.Scale(recoilAnimation.OutRot, rotationScale) * shotStrength;
        Vector3 position = Vector3.Scale(recoilAnimation.OutLoc, positionScale) * shotStrength;
        transform.localPosition = restPosition + Vector3.ClampMagnitude(position, .015f);
        transform.localRotation = restRotation * Quaternion.Euler(Vector3.ClampMagnitude(rotation, 8f));
    }

    private void OnDisable()
    {
        if (!initialized) return;
        recoilAnimation.Stop();
        recoilAnimation.enabled = false;
        transform.localPosition = restPosition;
        transform.localRotation = restRotation;
        Heat = 0f;
        initialized = false;
    }
}
