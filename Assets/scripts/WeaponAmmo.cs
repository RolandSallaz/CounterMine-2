using Photon.Pun;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Per-weapon magazines. Normal weapons have infinite reserve; reward weapons cannot reload.
/// Owner-authoritative: dry shots are blocked on the shooter, so the master never sees them.</summary>
[DisallowMultipleComponent]
public sealed class WeaponAmmo : MonoBehaviour
{
    [SerializeField, Min(1)] private int magazineSize = 30;
    [SerializeField, Min(.1f)] private float fallbackReloadSeconds = 2.6f;
    [SerializeField] private bool autoReloadOnEmpty = true;
    [SerializeField] private WeaponIdleSynchronizer weaponAnimation;
    [SerializeField] private PlayerHealth health;

    public int MagazineSize => magazineSize;
    public int MagAmmo { get; private set; }
    public bool IsReloading { get; private set; }
    public bool FiniteReserve { get; private set; }
    public int SavedAmmo(string id) => equippedWeapon == id ? MagAmmo : magazines.TryGetValue(id, out int count) ? count : 0;
    public void SetFiniteAmmo(string id, int count)
    {
        magazines[id] = Mathf.Clamp(count, 0, MilkorRewards.Capacity);
        if (equippedWeapon == id) { MagAmmo = magazines[id]; IsReloading = false; }
    }
    public bool CanShoot => !IsReloading && MagAmmo > 0;
    public bool ReloadsOneRoundAtATime => equippedWeapon == "rsh12" || equippedWeapon == "winchester1897";
    public bool CanInterruptReload
    {
        get
        {
            if (!IsReloading || !ReloadsOneRoundAtATime) return false;
            weaponAnimation?.RefreshIndividualReloadProgress();
            return MagAmmo > 0 || (animDriven && weaponAnimation != null && weaponAnimation.CompletedReloadRounds > creditedReloadRounds);
        }
    }

    private PhotonView photonView;
    private bool animDriven;
    private float reloadEndsAt;
    private float nextDrySound;
    private string equippedWeapon;
    private int creditedReloadRounds;
    private bool suppressAutoReloadOnce;
    private readonly System.Collections.Generic.Dictionary<string,int> magazines = new System.Collections.Generic.Dictionary<string,int>();
    public void SelectWeapon(string id, int capacity, bool finiteReserve = false)
    {
        if (equippedWeapon == id) return;
        if (!string.IsNullOrEmpty(equippedWeapon)) magazines[equippedWeapon] = MagAmmo;
        equippedWeapon = id;
        FiniteReserve = finiteReserve;
        magazineSize = Mathf.Max(1,capacity);
        MagAmmo = magazines.TryGetValue(id,out int saved) ? Mathf.Clamp(saved,0,magazineSize) : finiteReserve ? 0 : magazineSize;
        // Switching interrupts a reload; it must not refill either magazine.
        IsReloading = false; animDriven = false;
        suppressAutoReloadOnce=false;
    }
    private bool IsOwner => !PhotonNetwork.InRoom || (photonView != null && photonView.IsMine);

    private void Awake()
    {
        photonView = GetComponent<PhotonView>();
        weaponAnimation ??= GetComponentInChildren<WeaponIdleSynchronizer>(true);
        health ??= GetComponent<PlayerHealth>();
        MagAmmo = magazineSize;
    }

    public void Consume()
    {
        if (MagAmmo <= 0) return;
        MagAmmo--;
        bool reloadEmpty=autoReloadOnEmpty&&!suppressAutoReloadOnce;
        suppressAutoReloadOnce=false;
        if (MagAmmo == 0 && reloadEmpty) TryStartReload();
    }

    public void HandleDryFire()
    {
        if (!IsReloading && Time.time >= nextDrySound && weaponAnimation != null)
        {
            var profile = weaponAnimation.AudioProfile;
            if (profile != null) GameAudio.Play(profile.dryFire, transform.position + Vector3.up, .35f, 10f, !BotController.IsBot(this) && IsOwner);
            nextDrySound = Time.time + .25f;
        }
        if (!IsReloading && autoReloadOnEmpty && MagAmmo <= 0) TryStartReload();
    }

    public bool TryStartReload()
    {
        if (FiniteReserve || IsReloading || MagAmmo >= magazineSize || weaponAnimation == null) return false;
        if (health != null && health.IsDead) return false;
        if (weaponAnimation.PlayWeaponAction("reload"))
        {
            creditedReloadRounds = 0;
            IsReloading = true; animDriven = true; return true;
        }
        // No reload clip in the weapon catalog: timed fallback. CanFire stays true, IsReloading gates fire.
        IsReloading = true; animDriven = false;
        reloadEndsAt = Time.time + (ReloadsOneRoundAtATime ? fallbackReloadSeconds / magazineSize : fallbackReloadSeconds);
        return true;
    }

    private void CreditInsertedRounds()
    {
        if (!animDriven || weaponAnimation == null) return;
        weaponAnimation.RefreshIndividualReloadProgress();
        int inserted=weaponAnimation.CompletedReloadRounds;
        MagAmmo=Mathf.Min(magazineSize,MagAmmo+Mathf.Max(0,inserted-creditedReloadRounds));
        creditedReloadRounds=inserted;
    }

    public bool TryInterruptReloadForShot()
    {
        if (!IsReloading || !ReloadsOneRoundAtATime || !IsOwner) return false;
        CreditInsertedRounds();
        if (MagAmmo <= 0) return false;
        IsReloading=false; animDriven=false;
        // Firing the only seated round must still cancel this reload, rather
        // than immediately starting the same empty-gun reload again.
        suppressAutoReloadOnce=true;
        if (weaponAnimation != null && weaponAnimation.ActionId == "reload") weaponAnimation.RestartIdle();
        return true;
    }

    public void InterruptReloadForWeaponSwitch()
    {
        if (!IsReloading) return;
        if (ReloadsOneRoundAtATime && IsOwner) CreditInsertedRounds();
        IsReloading=false; animDriven=false;
    }

    private void Update()
    {
        if (!IsOwner || weaponAnimation == null) return;
        // Pick up reloads started through the shared weapon action API.
        if (!IsReloading && weaponAnimation.ActionId == "reload" && MagAmmo < magazineSize)
        {
            IsReloading = true; animDriven = true;
            creditedReloadRounds = 0;
        }
        if (IsReloading)
        {
            if (health != null && health.IsDead)
            {
                IsReloading = false;
                if (weaponAnimation.ActionId == "reload") weaponAnimation.RestartIdle();
                return;
            }
            if (animDriven)
            {
                if (ReloadsOneRoundAtATime)
                {
                    if (weaponAnimation.ActionId == "reload" || weaponAnimation.ActionId == "idle") CreditInsertedRounds();
                    if (weaponAnimation.ActionId != "reload") { IsReloading=false; animDriven=false; }
                }
                else if (weaponAnimation.ActionId == "idle") FinishReload();
                else if (weaponAnimation.ActionId != "reload") IsReloading = false;
            }
            else if (Time.time >= reloadEndsAt) FinishReload();
            return;
        }
        if (health != null && health.IsDead) return;
        if (!BotController.IsBot(this) && weaponAnimation.CanFire && MagAmmo < magazineSize &&
            Application.isFocused && Cursor.lockState == CursorLockMode.Locked &&
            !PlatformLifecycle.InputBlocked && DesktopControls.ReloadPressed)
            TryStartReload();
    }

    private void FinishReload()
    {
        MagAmmo = ReloadsOneRoundAtATime ? Mathf.Min(MagAmmo + 1, magazineSize) : magazineSize;
        if (ReloadsOneRoundAtATime && MagAmmo < magazineSize)
            reloadEndsAt=Time.time+fallbackReloadSeconds/magazineSize;
        else IsReloading = false;
    }
}
