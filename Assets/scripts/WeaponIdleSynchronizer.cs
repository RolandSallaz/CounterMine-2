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
        public float roundsPerMinute = 600;
        public bool automatic = true;
        public int damage = 34;
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
    private static double Now => PhotonNetwork.InRoom ? PhotonNetwork.Time : Time.timeAsDouble;
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
            if (cameraBone == null) return Quaternion.identity;
            var relative = Quaternion.Inverse(cameraSkeleton.rotation) * cameraBone.rotation;
            return relative * Quaternion.Inverse(cameraRestRotation);
        }
    }
    private bool applyingNetwork;
    private WeaponEntry currentWeapon;
    private Transform bolt;
    private Vector3 boltRest;
    private bool captureBoltRest;
    private double boltShotAt = double.NegativeInfinity;
    public void PlayShotBolt()
    {
        if (CanFire && bolt != null) boltShotAt = Now;
    }
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
        if (weaponAnimancer != null && currentWeapon != null && currentWeapon.proceduralEquipSeconds > 0)
            weaponAnimancer.transform.SetLocalPositionAndRotation(weaponRestPosition, weaponRestRotation);
        weaponRestPosition = entry.animator.transform.localPosition;
        weaponRestRotation = entry.animator.transform.localRotation;
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
        if (id == "reload" && currentWeapon.proceduralReloadSeconds > 0)
        { ActionId = "reload"; PlayPair(armsIdleClip, weaponIdleClip, true); return true; }
        var action = Array.Find(currentWeapon.actions ?? Array.Empty<ActionEntry>(), a => a != null && a.id == id);
        if (action == null || action.characterClip == null || action.weaponClip == null) return false;
        ActionId = id; PlayPair(action.characterClip, action.weaponClip, true); return true;
    }

    public bool ApplyNetworkState(string weaponId, string actionId, double startedAt, float speed)
    {
        if (!isActiveAndEnabled || double.IsNaN(startedAt) || double.IsInfinity(startedAt) ||
            float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0 || speed > 10) return false;
        EnsureCatalog();
        var candidate = Array.Find(weapons, w => w != null && w.id == weaponId);
        if (candidate == null || (actionId != "idle" && actionId != "equip" && !(actionId == "reload" && candidate.proceduralReloadSeconds > 0) &&
            !Array.Exists(candidate.actions ?? Array.Empty<ActionEntry>(), a => a != null && a.id == actionId && a.characterClip != null && a.weaponClip != null))) return false;
        applyingNetwork = true;
        try {
            if (!SelectWeapon(weaponId)) return false;
            if (!PlayWeaponAction(actionId)) return false;
            StartedAt = startedAt; playbackSpeed = speed;
            Update(); return true;
        } finally { applyingNetwork = false; }
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
    public bool ProceduralEquip => IsEquipping && ActionId == "equip" && currentWeapon != null && currentWeapon.proceduralEquipSeconds > 0;
    public bool ProceduralReload => IsEquipping && ActionId == "reload" && currentWeapon != null && currentWeapon.proceduralReloadSeconds > 0;
    public bool CanPoseHands => IsIdlePlaying || ProceduralEquip || ProceduralReload;
    public bool CanFire => IsIdlePlaying;
    private float ActionDuration => ProceduralEquip ? currentWeapon.proceduralEquipSeconds :
        ProceduralReload ? currentWeapon.proceduralReloadSeconds :
        activeArmsClip != null && activeWeaponClip != null ? Mathf.Max(activeArmsClip.length, activeWeaponClip.length) : 0;
    private Vector3 weaponRestPosition;
    private Quaternion weaponRestRotation;

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
        if (currentWeapon != null && currentWeapon.proceduralEquipSeconds > 0)
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
        if (IsEquipping && elapsedSeconds >= ActionDuration)
        {
            RestartIdle();
            return;
        }
        EvaluatePair(elapsedSeconds);
    }

    private void EvaluatePair(double seconds)
    {
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
            Vector3 backwards = currentWeapon.muzzle != null ? -currentWeapon.muzzle.forward : -WeaponRoot.forward;
            // Travel is in world metres, independent of the FBX import/bone scale.
            Vector3 travel = bolt.parent.InverseTransformVector(backwards * currentWeapon.boltTravel);
            bolt.localPosition = boltRest + travel * amount;
        }
        if (CanPoseHands && activeArmsClip.length == 0f && idleBones == null) CaptureIdlePose();
        if (currentWeapon != null && currentWeapon.proceduralEquipSeconds > 0)
        {
            float t = ProceduralEquip ? Mathf.Clamp01((float)(seconds / ActionDuration)) : 1f;
            float lift = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / .82f));
            float settle = Mathf.Sin(Mathf.Clamp01((t - .65f) / .35f) * Mathf.PI) * .018f;
            WeaponRoot.localPosition = weaponRestPosition + new Vector3(.08f, -.32f, -.14f) * (1f - lift) + Vector3.up * settle;
            WeaponRoot.localRotation = weaponRestRotation * Quaternion.Euler(new Vector3(32f, -18f, 24f) * (1f - lift));
            if (ProceduralReload)
            {
                float tilt = Mathf.Sin(Mathf.Clamp01((float)(seconds / ActionDuration)) * Mathf.PI);
                WeaponRoot.localPosition += new Vector3(.025f, -.065f, -.035f) * tilt;
                WeaponRoot.localRotation = weaponRestRotation * Quaternion.Euler(-8f * tilt, 12f * tilt, -18f * tilt);
            }
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
