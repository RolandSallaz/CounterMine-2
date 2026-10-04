using System;
using Photon.Pun;
using Animancer;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>Samples first-person arms and weapon from one clock, including clips of different lengths.</summary>
[DisallowMultipleComponent]
public sealed class WeaponIdleSynchronizer : MonoBehaviour
{
    [SerializeField] private AnimancerComponent armsAnimancer;
    [SerializeField] private AnimancerComponent weaponAnimancer;
    [SerializeField] private AnimationClip armsIdleClip;
    [SerializeField] private AnimationClip weaponIdleClip;
    [SerializeField] private AnimationClip armsEquipClip;
    [SerializeField] private AnimationClip weaponEquipClip;
    [SerializeField] private bool playEquipOnEnable = true;
    [SerializeField, Min(0f)] private float playbackSpeed = 1f;

    [Serializable] public sealed class WeaponEntry
    {
        public string id;
        public WeaponAudioProfile audioProfile;
        public AnimancerComponent animator;
        public WeaponAimRig aimRig;
        public Transform muzzle, leftGrip, rightGrip;
        public float maximumMuzzleReach = 1f;
        public int magazineSize = 30;
        public bool finiteReserve;
        public float proceduralEquipSeconds;
        public float proceduralReloadSeconds;
        [Range(0f, 2f)] public float cameraActionScale = 1f;
        public Vector3 reloadPositionOffset = new Vector3(-.1f, .08f, .08f);
        public Vector3 reloadRotationEuler = new Vector3(-5f, 10f, -15f);
        public float roundsPerMinute = 600;
        public bool automatic = true;
        public int damage = 34;
        [Range(1, 16)] public int pelletCount = 1;
        [Range(0f, 15f)] public float pelletSpreadDegrees;
        [Min(1f), InspectorName("Muzzle Velocity (m/s)")] public float muzzleVelocity = 900f;
        [Min(0f), InspectorName("Bullet Gravity (m/s^2)")] public float bulletGravity = 9.81f;
        [Min(1f)] public float fullDamageRange = 60f;
        [Min(1f)] public float maximumRange = 200f;
        [Range(.05f, 1f)] public float minimumDamageFraction = .5f;
        [Min(.1f)] public float recoilKick = 1f;
        public WeaponRecoilController.Tuning recoil;
        [Min(0f)] public float boltTravel = .065f;
        [Min(.02f)] public float boltCycleSeconds = .095f;
        public string boltBoneName;
        public AnimationClip characterIdle, weaponIdle, characterEquip, weaponEquip;
        public ActionEntry[] actions = Array.Empty<ActionEntry>();
    }
    [Serializable] public sealed class ActionEntry
    {
        public string id;
        public AnimationClip characterClip, weaponClip;
    }
    [SerializeField] private string defaultWeaponId = "ak74";
    [SerializeField] private WeaponEntry[] weapons = Array.Empty<WeaponEntry>();
    public event Action StateChanged;
    public string WeaponId { get; private set; }
    public string ActionId { get; private set; } = "idle";
    public double StartedAt { get; private set; }
    public float PlaybackSpeed => playbackSpeed;
    public AnimancerComponent CharacterAnimator => armsAnimancer;
    public AnimationClip PreviewCharacterIdle => armsIdleClip;
    public AnimationClip PreviewWeaponIdle => weaponIdleClip;
    public WeaponEntry FindPreviewWeapon(string id) => Array.Find(weapons, entry => entry != null && entry.id == id);
    public void SetCharacterAnimator(AnimancerComponent animator)
    {
        if (animator == null || animator == armsAnimancer) return;
        if (armsAnimancer != null && armsAnimancer.IsPlayableInitialized) armsAnimancer.Playable.PauseGraph();
        idleBones = null;
        armsAnimancer = animator;
        if (activeArmsClip == null || weaponState == null) return;
        armsState = armsAnimancer.Play(activeArmsClip);
        armsAnimancer.Playable.UpdateMode = DirectorUpdateMode.Manual;
        EvaluatePair(elapsedSeconds);
    }
    public Transform WeaponRoot => weaponAnimancer != null ? weaponAnimancer.transform : null;
    private static double Now => PhotonNetwork.InRoom && !PhotonNetwork.OfflineMode ? PhotonNetwork.Time : Time.timeAsDouble;
    [SerializeField] private Quaternion cameraRestRotation = Quaternion.Euler(-90f, 0f, 0f);
    private Transform cameraBone, cameraSkeleton;
    public Quaternion CameraRotationOffset
    {
        get
        {
            if (!isActiveAndEnabled || armsState == null || armsAnimancer == null) return Quaternion.identity;
            if (cameraSkeleton != armsAnimancer.transform)
            {
                cameraSkeleton = armsAnimancer.transform;
                cameraBone = cameraSkeleton.Find("Root/root/camera");
            }
            
            var relative = cameraBone != null ? Quaternion.Inverse(cameraSkeleton.rotation) * cameraBone.rotation : cameraRestRotation;
            Vector3 actionCamera = ProceduralEquip || ProceduralReload ? ProceduralActionPose.camera : Vector3.zero;
            if (IsIdlePlaying && WeaponId == "winchester1897")
                actionCamera += new Vector3(.24f,-.05f,.10f)*WeaponProceduralMotion.Window(ShotAge,.12f,.25f,.30f,.60f);
            return relative * Quaternion.Inverse(cameraRestRotation) * Quaternion.Euler(actionCamera);
        }
    }
    public static Vector3 ProceduralCameraEuler(string action, float progress, float scale)
    {
        if (scale <= 0f || (action != "equip" && action != "reload")) return Vector3.zero;
        if (WeaponProceduralMotion.Reference != null)
            return WeaponProceduralMotion.Evaluate("hk416",action,progress,scale).camera;
        float t = Mathf.Clamp01(progress);
        float envelope = Mathf.Sin(t * Mathf.PI);
        if (action == "equip")
            return new Vector3(-1.05f * envelope + .24f * Mathf.Sin(t * Mathf.PI * 2f) * envelope,
                .18f * Mathf.Sin(t * Mathf.PI * 2f) * envelope,
                .42f * Mathf.Sin(t * Mathf.PI * 2f) * envelope) * scale;
        return new Vector3((-2.8f - .38f * Mathf.Sin(t * Mathf.PI * 2f) + .16f * Mathf.Sin(t * Mathf.PI * 6f)) * envelope,
            .22f * Mathf.Sin(t * Mathf.PI * 4f) * envelope,
            .48f * Mathf.Sin(t * Mathf.PI * 2f) * envelope) * scale;
    }
    public static float SmoothWindow(float t, float riseStart, float riseEnd, float fallStart, float fallEnd) =>
        Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(riseStart, riseEnd, t)) *
        (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(fallStart, fallEnd, t)));
    private bool applyingNetwork;
    private WeaponEntry currentWeapon;
    private Transform bolt;
    private Vector3 boltRest;
    private bool captureBoltRest;
    private double boltShotAt = double.NegativeInfinity;
    public void PlayShotBolt()
    {
        if (!CanFire) return;
        boltShotAt = Now;
        WeaponRoot.GetComponent<WeaponManualAction>()?.Shot();
    }
    public float ShotAge => (float)(Now - boltShotAt);
    public static float BoltCycle(float age, float duration)
    {
        float phase = age / Mathf.Max(.02f, duration);
        if (phase < 0 || phase >= 1) return 0;
        if (phase < .22f) return Mathf.SmoothStep(0, 1, phase / .22f);
        return 1 - Mathf.SmoothStep(0, 1, (phase - .22f) / .78f);
    }
    private void BindBolt(WeaponEntry entry)
    {
        if (bolt != null && !captureBoltRest) bolt.localPosition = boltRest;
        bolt = null; boltShotAt = double.NegativeInfinity; captureBoltRest = true;
        string boneName = !string.IsNullOrEmpty(entry.boltBoneName) ? entry.boltBoneName : entry.id == "ucp" ? "slide" : "bolt";
        foreach (var bone in entry.animator.GetComponentsInChildren<Transform>(true))
            if (bone.name == boneName) { bolt = bone; break; }
    }
    public WeaponAudioProfile AudioProfile => currentWeapon != null && currentWeapon.audioProfile != null ? currentWeapon.audioProfile : GameAudio.Profile(WeaponId);
    public Transform RightGrip => currentWeapon != null ? currentWeapon.rightGrip : null;
    private bool IsRemote => PhotonNetwork.InRoom && !GetComponentInParent<PhotonView>().IsMine;

    private void EnsureCatalog()
    {
        if (WeaponId != null) return;
        if (weapons.Length == 0)
            weapons = new[] { new WeaponEntry { id = defaultWeaponId, animator = weaponAnimancer,
                characterIdle = armsIdleClip, weaponIdle = weaponIdleClip,
                characterEquip = armsEquipClip, weaponEquip = weaponEquipClip } };
        SelectWeapon(defaultWeaponId);
    }

    private bool SelectWeapon(string id)
    {
        var entry = Array.Find(weapons, w => w != null && w.id == id);
        if (entry == null || entry.animator == null || entry.characterIdle == null || entry.weaponIdle == null) return false;
        if (currentWeapon == entry) return true;
        GetComponentInParent<WeaponAmmo>()?.InterruptReloadForWeaponSwitch();
        if (weaponAnimancer != null && currentWeapon != null && (currentWeapon.proceduralEquipSeconds > 0 || WeaponProceduralMotion.Supports(currentWeapon.id)))
            weaponAnimancer.transform.SetLocalPositionAndRotation(weaponRestPosition, weaponRestRotation);
        weaponRestPosition = entry.animator.transform.localPosition;
        weaponRestRotation = entry.animator.transform.localRotation;
        weaponGripPivot = entry.rightGrip != null ? entry.animator.transform.InverseTransformPoint(entry.rightGrip.position) : Vector3.zero;
        BindBolt(entry);
        if (weaponAnimancer != null && weaponAnimancer.IsPlayableInitialized) weaponAnimancer.Playable.PauseGraph();
        foreach (var w in weapons) if (w != null && w.animator != null) w.animator.gameObject.SetActive(w == entry);
        currentWeapon = entry; WeaponId = entry.id; weaponAnimancer = entry.animator;
        armsIdleClip = entry.characterIdle; weaponIdleClip = entry.weaponIdle;
        armsEquipClip = entry.characterEquip; weaponEquipClip = entry.weaponEquip;
        var root = GetComponentInParent<PhotonView>();
        if (entry.aimRig != null) root.GetComponentInChildren<WeaponAimController>(true)?.Equip(entry.aimRig);
        if (entry.muzzle != null) root.GetComponent<NetworkWeapon>()?.SetMuzzle(entry.muzzle, entry.maximumMuzzleReach);
        root.GetComponent<NetworkWeapon>()?.SetDamage(entry.damage);
        root.GetComponent<NetworkWeapon>()?.SetBallistics(entry.muzzleVelocity, entry.bulletGravity, entry.fullDamageRange, entry.maximumRange, entry.minimumDamageFraction);
        root.GetComponent<WeaponAmmo>()?.SelectWeapon(entry.id, entry.magazineSize, entry.finiteReserve);
        root.GetComponentInChildren<WeaponRecoilController>(true)?.ConfigureFire(entry.roundsPerMinute, entry.automatic, entry.recoilKick, entry.recoil, entry.rightGrip);
        if (entry.leftGrip != null && entry.rightGrip != null)
            foreach (var ik in root.GetComponentsInChildren<WeaponHandIK>(true)) ik.SetGrips(entry.leftGrip, entry.rightGrip);
        return true;
    }

    public bool EquipWeapon(string id)
    {
        if (IsRemote || !isActiveAndEnabled) return false;
        EnsureCatalog();
        if (!SelectWeapon(id)) return false;
        PlayEquip(); return true;
    }

    public bool PlayWeaponAction(string id)
    {
        if (IsRemote && !applyingNetwork) return false;
        if (!isActiveAndEnabled) return false;
        EnsureCatalog();
        if (currentWeapon == null) return false;
        if (id == "idle") { RestartIdle(); return true; }
        if (id == "equip") { PlayEquip(); return true; }
        if (id == "reload" && HasProceduralReload)
        {
            if (ActionId == "reload" && IsPlayingAction) return true;
            var ammo = GetComponentInParent<WeaponAmmo>();
            ReloadRoundCount = IndividualReload ? Mathf.Clamp(networkReloadRoundCount > 0 ? networkReloadRoundCount :
                currentWeapon.magazineSize - (ammo != null ? ammo.MagAmmo : 0), 1, currentWeapon.magazineSize) : 0;
            CompletedReloadRounds = 0;
            ActionId = "reload"; PlayPair(armsIdleClip, weaponIdleClip, true); return true;
        }
        var action = Array.Find(currentWeapon.actions ?? Array.Empty<ActionEntry>(), a => a != null && a.id == id);
        if (action == null || action.characterClip == null || action.weaponClip == null) return false;
        ActionId = id; PlayPair(action.characterClip, action.weaponClip, true); return true;
    }

    public bool ApplyNetworkState(string weaponId, string actionId, double startedAt, float speed, int reloadRounds = 0)
    {
        if (!isActiveAndEnabled || double.IsNaN(startedAt) || double.IsInfinity(startedAt) ||
            float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0 || speed > 10) return false;
        EnsureCatalog();
        var candidate = Array.Find(weapons, w => w != null && w.id == weaponId);
        if (reloadRounds < 0 || (candidate != null && reloadRounds > candidate.magazineSize)) return false;
        if (candidate == null || (actionId != "idle" && actionId != "equip" && !(actionId == "reload" && candidate.proceduralReloadSeconds > 0) &&
            !Array.Exists(candidate.actions ?? Array.Empty<ActionEntry>(), a => a != null && a.id == actionId && a.characterClip != null && a.weaponClip != null))) return false;
        applyingNetwork = true;
        try {
            if (!SelectWeapon(weaponId)) return false;
            networkReloadRoundCount = reloadRounds;
            // A new network timestamp is a new action, even with the same ID.
            if (actionId == "reload" && ActionId == "reload") RestartIdle();
            if (!PlayWeaponAction(actionId)) return false;
            StartedAt = startedAt; playbackSpeed = speed;
            Update(); return true;
        } finally { applyingNetwork = false; networkReloadRoundCount = 0; }
    }

    private Transform[] idleBones;
    private Vector3[] idlePositions, idleScales;
    private Quaternion[] idleRotations;
    private void RestoreIdlePose()
    {
        if (idleBones == null) return;
        for (int i = 0; i < idleBones.Length; i++)
        {
            if (idleBones[i] == armsAnimancer.transform) continue;
            idleBones[i].localPosition = idlePositions[i]; idleBones[i].localRotation = idleRotations[i]; idleBones[i].localScale = idleScales[i];
        }
    }
    private void CaptureIdlePose()
    {
        idleBones = armsAnimancer.GetComponentsInChildren<Transform>(true);
        idlePositions = Array.ConvertAll(idleBones, b => b.localPosition);
        idleRotations = Array.ConvertAll(idleBones, b => b.localRotation);
        idleScales = Array.ConvertAll(idleBones, b => b.localScale);
    }
    private AnimancerState armsState;
    private AnimancerState weaponState;
    private double elapsedSeconds;
    private AnimationClip activeArmsClip;
    private AnimationClip activeWeaponClip;
    public bool IsEquipping { get; private set; }
    public bool IsPlayingAction => IsEquipping;
    public bool IsIdlePlaying => isActiveAndEnabled && armsState != null && weaponState != null && !IsPlayingAction;
    public float ActionProgress => IsPlayingAction && activeArmsClip != null && activeWeaponClip != null
        ? Mathf.Clamp01((float)(elapsedSeconds / Math.Max(.000001, ActionDuration))) : 0;
    public bool ProceduralEquip => IsEquipping && ActionId == "equip" && currentWeapon != null && (currentWeapon.proceduralEquipSeconds > 0 || WeaponProceduralMotion.Supports(currentWeapon.id));
    private bool HasProceduralReload => currentWeapon != null && (currentWeapon.proceduralReloadSeconds > 0 ||
        (WeaponId == "ucp" && WeaponProceduralMotion.Supports(WeaponId)));
    public bool ProceduralReload => IsEquipping && ActionId == "reload" && HasProceduralReload;
    public bool CanPoseHands => IsIdlePlaying || ProceduralEquip || ProceduralReload;
    public bool CanFire => IsIdlePlaying;
    private int networkReloadRoundCount;
    public bool IndividualReload => WeaponId == "rsh12" || WeaponId == "winchester1897";
    public int ReloadRoundCount { get; private set; }
    public int CompletedReloadRounds { get; private set; }
    public float ReloadRoundSeconds => currentWeapon != null ? Mathf.Max(.1f,currentWeapon.proceduralReloadSeconds / Mathf.Max(1,currentWeapon.magazineSize)) : .7f;
    public const float ReloadOpenSeconds = .22f, ReloadCloseSeconds = .38f, ReloadInsertPhase = .62f;
    public float ReloadInsertionProgress => ProceduralReload && IndividualReload && elapsedSeconds >= ReloadOpenSeconds &&
        elapsedSeconds < ReloadOpenSeconds + ReloadRoundCount * ReloadRoundSeconds
        ? (float)((elapsedSeconds-ReloadOpenSeconds)/ReloadRoundSeconds % 1d) : 0f;
    public bool IsInsertingRound => ProceduralReload && IndividualReload && elapsedSeconds >= ReloadOpenSeconds &&
        elapsedSeconds < ReloadOpenSeconds + ReloadRoundCount * ReloadRoundSeconds;
    public int ReloadInsertionIndex => Mathf.Clamp(Mathf.FloorToInt(((float)elapsedSeconds-ReloadOpenSeconds)/ReloadRoundSeconds),0,Mathf.Max(0,ReloadRoundCount-1));
    public float ReloadHoldWeight => ProceduralReload && IndividualReload ? WeaponProceduralMotion.Window((float)elapsedSeconds,
        0f,ReloadOpenSeconds,ReloadOpenSeconds+ReloadRoundCount*ReloadRoundSeconds,ProceduralReloadDuration) : 0f;
    public float ReloadClosingProgress => ProceduralReload && IndividualReload ? Mathf.InverseLerp(
        ReloadOpenSeconds+ReloadRoundCount*ReloadRoundSeconds,ProceduralReloadDuration,(float)elapsedSeconds) : 0f;
    public void RefreshIndividualReloadProgress()
    {
        if (!ProceduralReload || !IndividualReload) return;
        CreditReloadProgress(Math.Max(0,Now-StartedAt)*playbackSpeed);
    }
    private void CreditReloadProgress(double seconds)
    {
        if (!ProceduralReload || !IndividualReload) return;
        int inserted = Mathf.FloorToInt(((float)seconds-ReloadOpenSeconds)/ReloadRoundSeconds + 1f-ReloadInsertPhase);
        CompletedReloadRounds = Mathf.Max(CompletedReloadRounds,Mathf.Clamp(inserted,0,ReloadRoundCount));
    }
    private float ProceduralReloadDuration => IndividualReload ? ReloadOpenSeconds + ReloadRoundCount*ReloadRoundSeconds + ReloadCloseSeconds :
        currentWeapon.proceduralReloadSeconds > 0 ? currentWeapon.proceduralReloadSeconds : 2.1f;
    private WeaponMotionReference.Frame ProceduralActionPose => ProceduralReload && IndividualReload
        ? WeaponProceduralMotion.EvaluateIndividualReload(WeaponId,ReloadHoldWeight,ReloadInsertionProgress,IsInsertingRound,currentWeapon.cameraActionScale)
        : WeaponProceduralMotion.Evaluate(WeaponId,ActionId,ActionProgress,currentWeapon.cameraActionScale);
    private float ActionDuration => ProceduralEquip ? (currentWeapon.proceduralEquipSeconds > 0 ? currentWeapon.proceduralEquipSeconds : .5f) :
        ProceduralReload ? ProceduralReloadDuration :
        activeArmsClip != null && activeWeaponClip != null ? Mathf.Max(activeArmsClip.length, activeWeaponClip.length) : 0;
    private Vector3 weaponRestPosition;
    private Quaternion weaponRestRotation;
    private Vector3 weaponGripPivot;

    public double NormalizedTime => armsState != null && activeArmsClip != null && activeArmsClip.length > 0f ? armsState.NormalizedTimeD : 0;
    public int LastEvaluatedFrame { get; private set; } = -1;

    private void OnEnable()
    {
        EnsureCatalog();
        if (IsRemote) { RestartIdle(); return; }
        if (playEquipOnEnable && armsEquipClip != null && weaponEquipClip != null) PlayEquip();
        else RestartIdle();
    }

    public void PlayEquip()
    {
        if (IsRemote && !applyingNetwork) return;
        if (!isActiveAndEnabled) return;
        ActionId = "equip";
        if (currentWeapon != null && (currentWeapon.proceduralEquipSeconds > 0 || WeaponProceduralMotion.Supports(currentWeapon.id)))
        { PlayPair(armsIdleClip, weaponIdleClip, true); return; }
        if (armsEquipClip == null || weaponEquipClip == null) { RestartIdle(); return; }
        PlayPair(armsEquipClip, weaponEquipClip, true);
    }

    // Reloads and other authored weapon actions use the same IK/fire gate.
    public void PlayAction(AnimationClip characterClip, AnimationClip weaponClip)
    {
        EnsureCatalog();
        if (currentWeapon == null) return;
        var action = Array.Find(currentWeapon.actions ?? Array.Empty<ActionEntry>(), a => a != null && a.characterClip == characterClip && a.weaponClip == weaponClip);
        if (action != null) { PlayWeaponAction(action.id); return; }
        if (PhotonNetwork.InRoom) { Debug.LogWarning("Register this action in the weapon catalog before network playback.", this); return; }
        ActionId = "custom"; PlayPair(characterClip, weaponClip, true);
    }

    public void RestartIdle()
    {
        ActionId = "idle";
        PlayPair(armsIdleClip, weaponIdleClip, false);
    }

    private void PlayPair(AnimationClip armsClip, AnimationClip weaponClip, bool equip)
    {
        boltShotAt = double.NegativeInfinity;
        idleBones = null;
        armsState = null;
        weaponState = null;
        LastEvaluatedFrame = -1;
        IsEquipping = false;
        if (armsAnimancer == null || weaponAnimancer == null ||
            armsAnimancer == weaponAnimancer || armsClip == null || weaponClip == null ||
            armsAnimancer.Animator == null || weaponAnimancer.Animator == null)
        {
            Debug.LogError("Assign separate arms/weapon Animancers and non-empty idle clips.", this);
            return;
        }

        activeArmsClip = armsClip;
        activeWeaponClip = weaponClip;
        IsEquipping = equip;
        armsState = armsAnimancer.Play(armsClip);
        weaponState = weaponAnimancer.Play(weaponClip);
        // Neither graph advances independently: both poses are evaluated at the exact same phase.
        armsAnimancer.Playable.UpdateMode = DirectorUpdateMode.Manual;
        weaponAnimancer.Playable.UpdateMode = DirectorUpdateMode.Manual;
        elapsedSeconds = 0;
        StartedAt = Now;
        EvaluatePair(elapsedSeconds);
        if (!applyingNetwork) StateChanged?.Invoke();
    }

    private void Update()
    {
        if (armsState == null || weaponState == null) return;
        elapsedSeconds = Math.Max(0, Now - StartedAt) * playbackSpeed;
        CreditReloadProgress(elapsedSeconds);
        if (IsEquipping && elapsedSeconds >= ActionDuration)
        {
            RestartIdle();
            return;
        }
        EvaluatePair(elapsedSeconds);
    }

    private void EvaluatePair(double seconds)
    {
        CreditReloadProgress(seconds);
        double duration = IsEquipping ? ActionDuration :
            activeArmsClip.length > 0f ? activeArmsClip.length : activeWeaponClip.length;
        double phase = duration > 0 ? seconds / duration : 0;
        if (IsEquipping) phase = System.Math.Min(phase, 1);
        else phase -= System.Math.Floor(phase);
        // A single-frame pose may have zero length; sample it at time zero.
        armsState.TimeD = phase * activeArmsClip.length;
        weaponState.TimeD = phase * activeWeaponClip.length;
        // A zero-duration idle can skip native transform writes at unchanged time.
        // Never let last frame's IK become the next frame's animation input.
        if (CanPoseHands && activeArmsClip.length == 0f) RestoreIdlePose();
        armsAnimancer.Evaluate(0f);
        weaponAnimancer.Evaluate(0f);
        if (!IsEquipping && bolt != null && currentWeapon != null)
        {
            if (captureBoltRest) { boltRest = bolt.localPosition; captureBoltRest = false; }
            float amount = BoltCycle((float)(Now - boltShotAt), currentWeapon.boltCycleSeconds);
            if (WeaponId == "l115a3") amount = WeaponProceduralMotion.Window(ShotAge,.20f,.45f,.55f,1.10f);
            Vector3 backwards = currentWeapon.muzzle != null ? -currentWeapon.muzzle.forward : -WeaponRoot.forward;
            // Travel is in world metres, independent of the FBX import/bone scale.
            Vector3 travel = bolt.parent.InverseTransformVector(backwards * currentWeapon.boltTravel);
            bolt.localPosition = boltRest + travel * amount;
        }
        if (CanPoseHands && activeArmsClip.length == 0f && idleBones == null) CaptureIdlePose();
        if (currentWeapon != null && (currentWeapon.proceduralEquipSeconds > 0 || WeaponProceduralMotion.Supports(currentWeapon.id)))
        {
            Vector3 offset = Vector3.zero, euler = Vector3.zero;
            if ((ProceduralEquip || ProceduralReload) && WeaponProceduralMotion.Supports(WeaponId))
            {
                var pose = ProceduralActionPose;
                offset = pose.position; euler = pose.rotation;
            }
            else if (ProceduralEquip)
            {
                float t = Mathf.Clamp01((float)(seconds / ActionDuration));
                float enter = 1f - Mathf.SmoothStep(0f, 1f, t);
                float settle = Mathf.Sin(Mathf.PI * Mathf.Clamp01((t - .72f) / .28f)) * .012f;
                offset = new Vector3(.025f, .015f, .055f) * enter + Vector3.up * settle;
                euler = new Vector3(6f, -8f, 10f) * enter;
            }
            else if (ProceduralReload)
            {
                float t = Mathf.Clamp01((float)(seconds / ActionDuration));
                float inspect = SmoothWindow(t, .04f, .2f, .78f, .97f);
                float extract = SmoothWindow(t, .23f, .36f, .58f, .72f);
                float seat = Mathf.Sin(Mathf.PI * Mathf.InverseLerp(.73f, .86f, t)) * SmoothWindow(t, .72f, .77f, .84f, .89f);
                offset = currentWeapon.reloadPositionOffset * inspect + new Vector3(-.008f, -.006f, -.012f) * extract + Vector3.up * (.009f * seat);
                euler = currentWeapon.reloadRotationEuler * inspect + new Vector3(-1.2f, 0f, 1.4f) * seat;
            }
            Quaternion actionRotation = Quaternion.Euler(euler);
            // Swing around the trigger hand rather than the imported model origin.
            Vector3 pivot = weaponGripPivot;
            WeaponRoot.localPosition = weaponRestPosition + weaponRestRotation * (offset + pivot - actionRotation * pivot);
            WeaponRoot.localRotation = weaponRestRotation * actionRotation;
        }
        LastEvaluatedFrame = Time.frameCount;
    }

    private void OnDisable()
    {
        LastEvaluatedFrame = -1;
        IsEquipping = false;
        if (armsAnimancer != null && armsAnimancer.IsPlayableInitialized)
            armsAnimancer.Playable.PauseGraph();
        if (weaponAnimancer != null && weaponAnimancer.IsPlayableInitialized)
            weaponAnimancer.Playable.PauseGraph();
        idleBones = null;
        armsState = null;
        weaponState = null;
    }
}
