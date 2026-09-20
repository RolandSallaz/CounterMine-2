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
    [SerializeField] private WeaponIdleSynchronizer weaponAnimation;
    [SerializeField, Min(1f)] private float roundsPerMinute = 600f;
    [SerializeField] private bool automatic = true;
    [SerializeField, Min(0f)] private float heatPerShot = .14f;
    [SerializeField, Min(0f)] private float heatRecovery = .5f;
    [SerializeField, Min(1f)] private float sustainedFireMultiplier = 1.6f;
    [SerializeField] private Vector2 cameraPitch = new Vector2(.35f, .6f);
    [SerializeField, Min(0f)] private float cameraYaw = .2f;
    private float currentKickMultiplier = 1f;
    [Header("Spread (degrees, drives bullets and crosshair)")]
    [SerializeField, Min(0f)] private float hipSpread = 1.2f;
    [SerializeField, Min(0f)] private float heatSpread = 3.5f;
    [SerializeField, Min(0f)] private float moveSpread = 4.2f;
    [SerializeField, Min(0f)] private float airSpread = 2.2f;
    [SerializeField, Range(0f, 1f)] private float crouchSpreadMultiplier = .7f;

    [System.Serializable]
    public sealed class Tuning
    {
        public RecoilAnimData animation;
        public Vector2 cameraPitch = new Vector2(.2f, .3f);
        public bool rotateAroundGrip;
        [Min(0f)] public float cameraYaw = .12f;
        [Min(0f)] public float heatPerShot = .12f, heatRecovery = .8f;
        [Min(1f)] public float sustainedFireMultiplier = 1.25f;
        [Min(0f)] public float hipSpread = 1f, heatSpread = 2f, moveSpread = 2.5f, airSpread = 3f;
        [Range(0f, 1f)] public float crouchSpreadMultiplier = .7f;
    }
    private Tuning defaultTuning;
    private Transform recoilGrip;

    private void ApplyTuning(Tuning tuning)
    {
        // Capture before the first switch, including switches before Awake/OnEnable.
        defaultTuning ??= new Tuning {
            animation = recoilProfile, cameraPitch = cameraPitch, cameraYaw = cameraYaw,
            heatPerShot = heatPerShot, heatRecovery = heatRecovery,
            sustainedFireMultiplier = sustainedFireMultiplier, hipSpread = hipSpread,
            heatSpread = heatSpread, moveSpread = moveSpread, airSpread = airSpread,
            crouchSpreadMultiplier = crouchSpreadMultiplier
        };
        var value = tuning != null && tuning.animation != null ? tuning : defaultTuning;
        recoilProfile = value.animation;
        cameraPitch = value.cameraPitch;
        cameraYaw = value.cameraYaw;
        heatPerShot = value.heatPerShot;
        heatRecovery = value.heatRecovery;
        sustainedFireMultiplier = value.sustainedFireMultiplier;
        hipSpread = value.hipSpread;
        heatSpread = value.heatSpread;
        moveSpread = value.moveSpread;
        airSpread = value.airSpread;
        crouchSpreadMultiplier = value.crouchSpreadMultiplier;
    }

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
    public void ConfigureFire(float rpm, bool fullAuto, float kickMultiplier = 1f, Tuning tuning = null, Transform grip = null)
    {
        ApplyTuning(tuning);
        recoilGrip = tuning != null && tuning.rotateAroundGrip ? grip : null;
        roundsPerMinute = Mathf.Max(1f, rpm);
        automatic = fullAuto;
        currentKickMultiplier = kickMultiplier <= 0f ? 1f : kickMultiplier;
        shotStrength = 1f;
        if (hasRestPose) { transform.localPosition = restPosition; transform.localRotation = restRotation; }
        Heat = 0;
        nextShotTime = Time.timeAsDouble;
        if (initialized && recoilAnimation != null && recoilProfile != null)
        {
            recoilAnimation.Stop();
            recoilAnimation.Init(recoilProfile, roundsPerMinute);
            recoilAnimation.fireMode = FireMode.Semi;
        }
    }

    private float CurrentKick() => Mathf.Max(.1f, currentKickMultiplier);
    /// <summary>Single source of truth: current bullet spread cone (degrees) and crosshair gap.</summary>
    public float CurrentSpreadDegrees => ComputeSpread();

    private float ComputeSpread()
    {
        float aim = aimController != null ? aimController.AimAmount : 0f;
        float spread = hipSpread + heatSpread * Heat;
        if (playerController != null)
        {
            spread += moveSpread * Mathf.Clamp01(playerController.HorizontalSpeed / Mathf.Max(.01f, playerController.TopSpeed));
            if (!playerController.IsGrounded) spread += airSpread;
            if (playerController.IsCrouching) spread *= crouchSpreadMultiplier;
        }
        // ADS removes spread entirely: only hip fire deviates.
        return Mathf.Max(0f, spread * (1f - aim));
    }

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
            (weaponAnimation == null || weaponAnimation.CanFire) &&
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
        float kick = CurrentKick();
        float baseStrength = Mathf.Lerp(1f, sustainedFireMultiplier, Heat) * Mathf.Lerp(1.1f, .9f, ergonomics);
        // Visual spring (LateUpdate) scales fully with the per-weapon kick.
        shotStrength = baseStrength * kick;
        // Blend ADS continuously outside the plugin's boolean aiming flag to avoid a midpoint snap.
        recoilAnimation.isAiming = false;
        recoilAnimation.Play();
        // Camera impulse is tuned independently of the weapon mesh.
        float cameraStrength = baseStrength * Mathf.Lerp(1f, .65f, aim);
        cameraLook?.AddRecoil(new Vector2(Random.Range(-cameraYaw, cameraYaw), Random.Range(cameraPitch.x, cameraPitch.y)) * cameraStrength);
        animationController?.PlayWeaponFire();
        Heat = Mathf.Clamp01(Heat + heatPerShot);
        ShotsFired++;
    }

    private void LateUpdate()
    {
        if (!initialized) return;
        if (weaponAnimation != null && weaponAnimation.IsPlayingAction)
        {
            transform.localPosition = restPosition;
            transform.localRotation = restRotation;
            return;
        }
        float aim = aimController != null ? aimController.AimAmount : 0f;
        Vector3 rotationScale = Vector3.Lerp(Vector3.one, recoilProfile.aimRot, aim);
        Vector3 positionScale = Vector3.Lerp(Vector3.one, recoilProfile.aimLoc, aim);
        Vector3 rotation = Vector3.Scale(recoilAnimation.OutRot, rotationScale) * shotStrength;
        Vector3 position = Vector3.Scale(recoilAnimation.OutLoc, positionScale) * shotStrength;
        Quaternion kickRotation = Quaternion.Euler(Vector3.ClampMagnitude(rotation, 8f));
        // Rotate around the firing hand, so muzzle rise does not lift the whole weapon rig.
        Vector3 pivot = recoilGrip != null ? transform.InverseTransformPoint(recoilGrip.position) : Vector3.zero;
        transform.localPosition = restPosition + Vector3.ClampMagnitude(position, .015f)
            + restRotation * (pivot - kickRotation * pivot);
        transform.localRotation = restRotation * kickRotation;
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
